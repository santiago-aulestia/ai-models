using System;
using System.IO;

namespace HybridTemporalAI
{
    class Program
    {
        const string WeightsFile = "optimizer-weights.dat";
        const string BitmapFile  = "optimizer-weights.bmp";

        static void Main()
        {
            var rand     = new Random(42);
            var temporal = new TemporalFeatureExtractor(decay: 0.98);
            var network  = new NeuralNetwork(inputSize: 7, hiddenSize: 10, rand);
            bool? lastActionSafe = null;

            int    startStep   = 0;
            double totalReward = 0.0;

            bool resumed = network.Load(WeightsFile, out startStep, out totalReward);
            Console.WriteLine(resumed
                ? $"Resumed from step {startStep:N0}  (avg reward so far: {totalReward / startStep:F4})"
                : "Starting fresh training.");

            int episodes = 100000;

            for (int t = startStep + 1; t <= startStep + episodes; t++)
            {
                double dt = 1.0;

                double load    = rand.NextDouble();
                double error   = rand.NextDouble();
                double urgency = rand.NextDouble();

                temporal.Update(load, error, dt);

                var features = new double[]
                {
                    load, error, urgency,
                    temporal.DLoad, temporal.DError,
                    temporal.ILoad, temporal.IError
                };

                double heuristicScore = HeuristicEngine.ScoreSafeAction(
                    load, error, urgency,
                    temporal.DLoad, temporal.DError,
                    temporal.ILoad, temporal.IError);

                double fuzzyScore = FuzzyEngine.ScoreSafeAction(
                    load, error, urgency,
                    temporal.DError, temporal.IError);

                double neuralScore = network.Predict(features);

                double combined = 0.45 * neuralScore
                                + 0.30 * heuristicScore
                                + 0.25 * fuzzyScore;

                bool chooseSafe = combined >= 0.5;

                double reward = EnvironmentReward(
                    load, error, urgency,
                    temporal.DError, temporal.IError,
                    chooseSafe, lastActionSafe);

                totalReward += reward;

                double target = chooseSafe ? reward : (1.0 - reward);
                network.Train(features, target, learningRate: 0.03);
                lastActionSafe = chooseSafe;

                if (t % 300 == 0)
                {
                    Console.WriteLine(
                        $"Step {t,6} | AvgReward={(totalReward / t):F4} | " +
                        $"NN={neuralScore:F3} H={heuristicScore:F3} F={fuzzyScore:F3} " +
                        $"Combined={combined:F3} Action={(chooseSafe ? "Safe" : "Fast")} Reward={reward:F3}");
                }
            }

            int finalStep = startStep + episodes;
            Console.WriteLine($"\nTraining complete. Total steps: {finalStep:N0}");

            network.Save(WeightsFile, finalStep, totalReward);
            BmpWriter.BackupIfExists(BitmapFile);
            network.SaveBitmap(BitmapFile);
            Console.WriteLine($"Weights saved → {WeightsFile}");
            Console.WriteLine($"Weight map   → {BitmapFile}");
        }

        static double EnvironmentReward(
            double load,
            double error,
            double urgency,
            double dError,
            double iError,
            bool choseSafe,
            bool? lastActionSafe)
        {
            // Real hidden risk: current stress + rising error + accumulated error
            double danger =
                0.35 * load +
                0.30 * error +
                0.20 * Clamp01((dError + 1.0) / 2.0) + // maps [-1,1] to [0,1] style
                0.25 * Clamp01(iError);

            danger = Clamp01(danger);

            bool safeIsBetter = danger >= 0.55;

            double reward = (choseSafe == safeIsBetter) ? 1.0 : 0.15;

            // If urgency is high and system is actually safe, FastAction deserves some bonus
            if (!safeIsBetter && !choseSafe && urgency > 0.75)
                reward += 0.10;

            // Penalize oscillation / unstable switching
            if (lastActionSafe.HasValue && lastActionSafe.Value != choseSafe)
                reward -= 0.05;

            return Clamp01(reward);
        }

        static double Clamp01(double x)
        {
            if (x < 0) return 0;
            if (x > 1) return 1;
            return x;
        }
    }

    public class TemporalFeatureExtractor
    {
        private double prevLoad;
        private double prevError;
        private bool initialized = false;
        private readonly double decay;

        public double DLoad { get; private set; }
        public double DError { get; private set; }
        public double ILoad { get; private set; }
        public double IError { get; private set; }

        public TemporalFeatureExtractor(double decay = 0.98)
        {
            this.decay = decay;
        }

        public void Update(double load, double error, double dt)
        {
            if (!initialized)
            {
                prevLoad = load;
                prevError = error;
                initialized = true;
            }

            // Derivatives
            DLoad = (load - prevLoad) / dt;
            DError = (error - prevError) / dt;

            // Decayed integrals (memory with forgetting)
            ILoad = decay * ILoad + load * dt;
            IError = decay * IError + error * dt;

            // Clamp to keep values stable
            ILoad = Math.Min(5.0, ILoad);
            IError = Math.Min(5.0, IError);

            prevLoad = load;
            prevError = error;
        }
    }

    public static class HeuristicEngine
    {
        public static double ScoreSafeAction(
            double load,
            double error,
            double urgency,
            double dLoad,
            double dError,
            double iLoad,
            double iError)
        {
            double score = 0.0;

            // Current state
            score += 0.30 * load;
            score += 0.35 * error;

            // Rising error is dangerous
            if (dError > 0)
                score += 0.20 * Math.Min(1.0, dError);

            // Persistent error matters a lot
            score += 0.25 * Math.Min(1.0, iError / 3.0);

            // High urgency pushes toward FastAction
            score -= 0.25 * urgency;

            // Strong safety overrides
            if (error > 0.75 && dError > 0.10)
                score += 0.15;

            if (iError > 2.5)
                score += 0.20;

            return Clamp01(score);
        }

        static double Clamp01(double x)
        {
            if (x < 0) return 0;
            if (x > 1) return 1;
            return x;
        }
    }

    public static class FuzzyEngine
    {
        public static double ScoreSafeAction(
            double load,
            double error,
            double urgency,
            double dError,
            double iError)
        {
            double loadHigh = High(load);
            double errorHigh = High(error);
            double urgencyHigh = High(urgency);
            double errorRising = Rising(dError);
            double integratedErrorLarge = LargeIntegral(iError);

            // Fuzzy rules
            // R1: IF error is high THEN Safe strong
            double r1 = errorHigh;

            // R2: IF error is rising THEN Safe moderate-strong
            double r2 = errorRising;

            // R3: IF integrated error is large THEN Safe very strong
            double r3 = integratedErrorLarge;

            // R4: IF load is high AND error is rising THEN Safe very strong
            double r4 = Math.Min(loadHigh, errorRising);

            // R5: IF urgency is high AND error is not high THEN Safe weak
            double errorNotHigh = 1.0 - errorHigh;
            double r5 = Math.Min(urgencyHigh, errorNotHigh);

            // Weighted average approximation of fuzzy output
            double numerator =
                r1 * 0.80 +
                r2 * 0.70 +
                r3 * 0.95 +
                r4 * 0.90 +
                r5 * 0.20;

            double denominator = r1 + r2 + r3 + r4 + r5;

            if (denominator == 0)
                return 0.5;

            return numerator / denominator;
        }

        static double High(double x)
        {
            if (x <= 0.5) return 0.0;
            if (x >= 1.0) return 1.0;
            return (x - 0.5) / 0.5;
        }

        static double Rising(double dx)
        {
            // Maps derivative to [0,1] where positive rise is stronger membership
            if (dx <= 0.0) return 0.0;
            if (dx >= 1.0) return 1.0;
            return dx;
        }

        static double LargeIntegral(double integral)
        {
            // Assume 0..3+ is meaningful region
            if (integral <= 1.0) return 0.0;
            if (integral >= 3.0) return 1.0;
            return (integral - 1.0) / 2.0;
        }
    }

    public class NeuralNetwork
    {
        private readonly int inputSize;
        private readonly int hiddenSize;
        private readonly double[,] w1;
        private readonly double[] b1;
        private readonly double[] w2;
        private double b2;
        private readonly Random rand;

        public NeuralNetwork(int inputSize, int hiddenSize, Random rand)
        {
            this.inputSize = inputSize;
            this.hiddenSize = hiddenSize;
            this.rand = rand;

            w1 = new double[inputSize, hiddenSize];
            b1 = new double[hiddenSize];
            w2 = new double[hiddenSize];

            Init();
        }

        private void Init()
        {
            for (int i = 0; i < inputSize; i++)
                for (int h = 0; h < hiddenSize; h++)
                    w1[i, h] = RandWeight();

            for (int h = 0; h < hiddenSize; h++)
            {
                b1[h] = RandWeight();
                w2[h] = RandWeight();
            }

            b2 = RandWeight();
        }

        private double RandWeight() => (rand.NextDouble() * 2 - 1) * 0.25;

        public double Predict(double[] x)
        {
            Forward(x, out _, out double y);
            return y;
        }

        public void Train(double[] x, double target, double learningRate)
        {
            Forward(x, out double[] h, out double y);

            double outputError = target - y;
            double outputDelta = outputError * SigmoidDerivativeFromOutput(y);

            double[] hiddenDelta = new double[hiddenSize];
            for (int j = 0; j < hiddenSize; j++)
            {
                double err = outputDelta * w2[j];
                hiddenDelta[j] = err * SigmoidDerivativeFromOutput(h[j]);
            }

            // hidden -> output
            for (int j = 0; j < hiddenSize; j++)
                w2[j] += learningRate * outputDelta * h[j];

            b2 += learningRate * outputDelta;

            // input -> hidden
            for (int i = 0; i < inputSize; i++)
            {
                for (int j = 0; j < hiddenSize; j++)
                {
                    w1[i, j] += learningRate * hiddenDelta[j] * x[i];
                }
            }

            for (int j = 0; j < hiddenSize; j++)
                b1[j] += learningRate * hiddenDelta[j];
        }

        private void Forward(double[] x, out double[] h, out double y)
        {
            h = new double[hiddenSize];

            for (int j = 0; j < hiddenSize; j++)
            {
                double sum = b1[j];
                for (int i = 0; i < inputSize; i++)
                    sum += x[i] * w1[i, j];
                h[j] = Sigmoid(sum);
            }

            double outSum = b2;
            for (int j = 0; j < hiddenSize; j++)
                outSum += h[j] * w2[j];

            y = Sigmoid(outSum);
        }

        private static double Sigmoid(double x) =>
            1.0 / (1.0 + Math.Exp(-x));

        private static double SigmoidDerivativeFromOutput(double y) =>
            y * (1.0 - y);

        public void Save(string path, int stepCount, double totalReward)
        {
            using var writer = new StreamWriter(path);
            writer.WriteLine(stepCount);
            writer.WriteLine(totalReward.ToString("R"));
            for (int i = 0; i < inputSize; i++)
                for (int h = 0; h < hiddenSize; h++)
                    writer.WriteLine(w1[i, h].ToString("R"));
            for (int h = 0; h < hiddenSize; h++)
                writer.WriteLine(b1[h].ToString("R"));
            for (int h = 0; h < hiddenSize; h++)
                writer.WriteLine(w2[h].ToString("R"));
            writer.WriteLine(b2.ToString("R"));
        }

        public bool Load(string path, out int stepCount, out double totalReward)
        {
            stepCount = 0; totalReward = 0;
            if (!File.Exists(path)) return false;
            using var reader = new StreamReader(path);
            stepCount   = int.Parse(reader.ReadLine()!);
            totalReward = double.Parse(reader.ReadLine()!);
            for (int i = 0; i < inputSize; i++)
                for (int h = 0; h < hiddenSize; h++)
                    w1[i, h] = double.Parse(reader.ReadLine()!);
            for (int h = 0; h < hiddenSize; h++)
                b1[h] = double.Parse(reader.ReadLine()!);
            for (int h = 0; h < hiddenSize; h++)
                w2[h] = double.Parse(reader.ReadLine()!);
            b2 = double.Parse(reader.ReadLine()!);
            return true;
        }

        public void SaveBitmap(string path)
        {
            int cell   = 20;
            int rows   = inputSize + 2;   // w1 rows + b1 row + w2 row
            int width  = hiddenSize * cell;
            int height = rows * cell;
            byte[] px  = new byte[width * height * 3];

            double mx = 0.1;
            for (int i = 0; i < inputSize; i++)
                for (int j = 0; j < hiddenSize; j++)
                    if (Math.Abs(w1[i, j]) > mx) mx = Math.Abs(w1[i, j]);
            for (int j = 0; j < hiddenSize; j++)
            {
                if (Math.Abs(b1[j]) > mx) mx = Math.Abs(b1[j]);
                if (Math.Abs(w2[j]) > mx) mx = Math.Abs(w2[j]);
            }

            void DrawCell(int row, int col, double val)
            {
                double t = (val / mx + 1.0) * 0.5;
                var (r, g, bv) = BmpWriter.WeightColor(t);
                int x0 = col * cell, y0 = row * cell;
                for (int dy = 0; dy < cell; dy++)
                    for (int dx = 0; dx < cell; dx++)
                    {
                        bool border = dx == 0 || dy == 0 || dx == cell - 1 || dy == cell - 1;
                        int idx = ((y0 + dy) * width + (x0 + dx)) * 3;
                        if (border) { px[idx] = px[idx + 1] = px[idx + 2] = 15; }
                        else        { px[idx] = r; px[idx + 1] = g; px[idx + 2] = bv; }
                    }
            }

            for (int i = 0; i < inputSize; i++)
                for (int j = 0; j < hiddenSize; j++)
                    DrawCell(i, j, w1[i, j]);
            for (int j = 0; j < hiddenSize; j++)
                DrawCell(inputSize,     j, b1[j]);
            for (int j = 0; j < hiddenSize; j++)
                DrawCell(inputSize + 1, j, w2[j]);

            BmpWriter.Save(path, width, height, px);
        }
    }

    static class BmpWriter
    {
        public static void Save(string path, int width, int height, byte[] rgb)
        {
            int rowBytes = width * 3;
            int pad      = (4 - rowBytes % 4) % 4;
            int dataSize = (rowBytes + pad) * height;

            using var f = File.Create(path);
            void W4(int v) { f.WriteByte((byte)v); f.WriteByte((byte)(v >> 8)); f.WriteByte((byte)(v >> 16)); f.WriteByte((byte)(v >> 24)); }
            void W2(int v) { f.WriteByte((byte)v); f.WriteByte((byte)(v >> 8)); }

            f.WriteByte(0x42); f.WriteByte(0x4D);
            W4(54 + dataSize); W4(0); W4(54);
            W4(40); W4(width); W4(height); W2(1); W2(24);
            W4(0); W4(dataSize); W4(2835); W4(2835); W4(0); W4(0);

            var padBytes = new byte[pad];
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 3;
                    f.WriteByte(rgb[i + 2]); f.WriteByte(rgb[i + 1]); f.WriteByte(rgb[i]);
                }
                f.Write(padBytes, 0, pad);
            }
        }

        // Blue → White → Red  (t=0 most negative, t=0.5 zero, t=1 most positive)
        public static (byte r, byte g, byte b) WeightColor(double t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            if (t < 0.5) { byte v = (byte)(t * 2 * 255); return (v, v, 255); }
            byte u = (byte)((1 - (t - 0.5) * 2) * 255);
            return (255, u, u);
        }

        public static void BackupIfExists(string path)
        {
            if (!File.Exists(path)) return;
            string dir  = Path.GetDirectoryName(path) ?? ".";
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext  = Path.GetExtension(path);
            int n = 1;
            string backup;
            do { backup = Path.Combine(dir, $"{stem}.{n++}{ext}"); } while (File.Exists(backup));
            File.Move(path, backup);
            Console.WriteLine($"  Backed up  → {Path.GetFileName(backup)}");
        }
    }
}