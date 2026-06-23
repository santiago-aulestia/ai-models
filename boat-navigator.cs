using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoatNavigatorAI
{
    struct Vec2
    {
        public float X, Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero = new Vec2(0f, 0f);

        public static Vec2 FromAngle(float radians)
        {
            return new Vec2(MathF.Cos(radians), MathF.Sin(radians));
        }

        public float Length()
        {
            return MathF.Sqrt(X * X + Y * Y);
        }

        public Vec2 Normalized()
        {
            float len = Length();
            if (len < 1e-8f) return Zero;
            return new Vec2(X / len, Y / len);
        }

        public Vec2 Rotate(float radians)
        {
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return new Vec2(X * cos - Y * sin, X * sin + Y * cos);
        }

        public static float Dot(Vec2 a, Vec2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
    }

    class BoatState
    {
        public Vec2 Position;
        public Vec2 Velocity;
        public float Heading;
        public List<Vec2> Trail = new List<Vec2>();

        public BoatState Clone()
        {
            var clone = new BoatState();
            clone.Position = Position;
            clone.Velocity = Velocity;
            clone.Heading = Heading;
            clone.Trail = new List<Vec2>(Trail);
            return clone;
        }
    }

    class BoatAction
    {
        public float PortThrottle;
        public float StarThrottle;
        public float SailAngle;
    }

    class StepResult
    {
        public BoatState NextState;
        public float Reward;
        public bool Done;
    }

    class EnvironmentGrid
    {
        public const int Size = 25;

        private float[,] _windAngle = new float[Size, Size];
        private float[,] _windMag = new float[Size, Size];
        private float[,] _curAngle = new float[Size, Size];
        private float[,] _curMag = new float[Size, Size];
        private float _time;
        private Random _rng = new Random(1337);

        public EnvironmentGrid()
        {
            Reset();
        }

        public void Reset()
        {
            _time = 0f;
            for (int c = 0; c < Size; c++)
            {
                for (int r = 0; r < Size; r++)
                {
                    _windAngle[c, r] = MathF.Sin(c * 0.5f) * MathF.Cos(r * 0.4f) * MathF.PI + (float)_rng.NextDouble() * 0.5f;
                    _windMag[c, r] = 0.05f + (float)_rng.NextDouble() * 0.07f;
                    _curAngle[c, r] = MathF.Sin(c * 0.5f + 1.3f) * MathF.Cos(r * 0.4f + 1.3f) * MathF.PI + (float)_rng.NextDouble() * 0.5f;
                    _curMag[c, r] = 0.02f + (float)_rng.NextDouble() * 0.04f;
                }
            }
        }

        public void Update()
        {
            _time += 0.01f;
            for (int c = 0; c < Size; c++)
            {
                for (int r = 0; r < Size; r++)
                {
                    _windAngle[c, r] += ((float)_rng.NextDouble() - 0.5f) * 0.04f;
                    _windMag[c, r] = Math.Clamp(_windMag[c, r] + ((float)_rng.NextDouble() - 0.5f) * 0.005f, 0.02f, 0.12f);
                    _curAngle[c, r] += ((float)_rng.NextDouble() - 0.5f) * 0.04f;
                    _curMag[c, r] = Math.Clamp(_curMag[c, r] + ((float)_rng.NextDouble() - 0.5f) * 0.005f, 0.01f, 0.06f);
                }
            }
        }

        public Vec2 GetWindCell(int col, int row)
        {
            return Vec2.FromAngle(_windAngle[col, row]) * _windMag[col, row];
        }

        public Vec2 GetCurrentCell(int col, int row)
        {
            return Vec2.FromAngle(_curAngle[col, row]) * _curMag[col, row];
        }

        public Vec2 GetWindAt(float x, float y)
        {
            x = Math.Clamp(x, 0f, Size - 1f);
            y = Math.Clamp(y, 0f, Size - 1f);
            int c0 = (int)x;
            int r0 = (int)y;
            int c1 = Math.Min(c0 + 1, Size - 1);
            int r1 = Math.Min(r0 + 1, Size - 1);
            float tx = x - c0;
            float ty = y - r0;
            Vec2 v00 = GetWindCell(c0, r0);
            Vec2 v10 = GetWindCell(c1, r0);
            Vec2 v01 = GetWindCell(c0, r1);
            Vec2 v11 = GetWindCell(c1, r1);
            Vec2 top = v00 * (1f - tx) + v10 * tx;
            Vec2 bot = v01 * (1f - tx) + v11 * tx;
            return top * (1f - ty) + bot * ty;
        }

        public Vec2 GetCurrentAt(float x, float y)
        {
            x = Math.Clamp(x, 0f, Size - 1f);
            y = Math.Clamp(y, 0f, Size - 1f);
            int c0 = (int)x;
            int r0 = (int)y;
            int c1 = Math.Min(c0 + 1, Size - 1);
            int r1 = Math.Min(r0 + 1, Size - 1);
            float tx = x - c0;
            float ty = y - r0;
            Vec2 v00 = GetCurrentCell(c0, r0);
            Vec2 v10 = GetCurrentCell(c1, r0);
            Vec2 v01 = GetCurrentCell(c0, r1);
            Vec2 v11 = GetCurrentCell(c1, r1);
            Vec2 top = v00 * (1f - tx) + v10 * tx;
            Vec2 bot = v01 * (1f - tx) + v11 * tx;
            return top * (1f - ty) + bot * ty;
        }
    }

    static class BoatPhysics
    {
        public static readonly Vec2 PointA = new Vec2(2.5f, 2.5f);
        public static readonly Vec2 PointB = new Vec2(22.5f, 22.5f);
        public const float ArrivalRadius = 1.2f;
        public const int TrailLength = 40;
        public const float MaxSpeed = 0.15f;
        public const float Drag = 0.96f;
        public const float EnginePower = 0.06f;
        public const float TurnRate = 0.12f;
        public const float SailEfficiency = 0.04f;

        public static BoatState InitialState()
        {
            var state = new BoatState();
            state.Position = PointA;
            state.Velocity = Vec2.Zero;
            state.Heading = MathF.Atan2(PointB.Y - PointA.Y, PointB.X - PointA.X);
            state.Trail = new List<Vec2>();
            return state;
        }

        public static StepResult Step(BoatState s, BoatAction action, EnvironmentGrid env)
        {
            float dTurn = TurnRate * (action.PortThrottle - action.StarThrottle);
            float newHeading = s.Heading + dTurn;

            Vec2 thrust = Vec2.FromAngle(newHeading) * ((action.PortThrottle + action.StarThrottle) * EnginePower);

            Vec2 wind = env.GetWindAt(s.Position.X, s.Position.Y);
            float windAngle = MathF.Atan2(wind.Y, wind.X);
            float eff = MathF.Cos(windAngle - newHeading) * action.SailAngle * SailEfficiency;
            Vec2 sailForce = wind.Normalized() * eff;

            Vec2 current = env.GetCurrentAt(s.Position.X, s.Position.Y);

            Vec2 newVel = (s.Velocity + thrust + sailForce) * Drag + current * 0.3f;

            if (newVel.Length() > MaxSpeed)
                newVel = newVel.Normalized() * MaxSpeed;

            Vec2 newPos = s.Position + newVel;

            var trail = new List<Vec2>();
            trail.Add(newPos);
            int take = Math.Min(s.Trail.Count, TrailLength - 1);
            for (int i = 0; i < take; i++)
                trail.Add(s.Trail[i]);

            bool oob = newPos.X < 0f || newPos.X >= 25f || newPos.Y < 0f || newPos.Y >= 25f;

            Vec2 clampedPos = new Vec2(Math.Clamp(newPos.X, 0f, 24.9f), Math.Clamp(newPos.Y, 0f, 24.9f));
            float dist = (clampedPos - PointB).Length();

            float reward;
            if (oob)
                reward = -2.0f;
            else if (dist < ArrivalRadius)
                reward = 10.0f;
            else
                reward = 1.0f - dist / 28.28f;

            bool done = oob || dist < ArrivalRadius;

            var nextState = new BoatState();
            nextState.Position = newPos;
            nextState.Velocity = newVel;
            nextState.Heading = newHeading;
            nextState.Trail = trail;

            return new StepResult { NextState = nextState, Reward = reward, Done = done };
        }
    }

    class PolicyNetwork
    {
        private float[,] W1 = new float[13, 32];
        private float[] B1 = new float[32];
        private float[,] W2 = new float[32, 16];
        private float[] B2 = new float[16];
        private float[,] W3 = new float[16, 3];
        private float[] B3 = new float[3];

        public PolicyNetwork()
        {
            var rng = new Random(42);
            InitWeights(W1, rng);
            InitWeights(B1, rng);
            InitWeights(W2, rng);
            InitWeights(B2, rng);
            InitWeights(W3, rng);
            InitWeights(B3, rng);
        }

        private void InitWeights(float[,] w, Random rng)
        {
            for (int i = 0; i < w.GetLength(0); i++)
                for (int j = 0; j < w.GetLength(1); j++)
                    w[i, j] = (float)(rng.NextDouble() * 0.5 - 0.25);
        }

        private void InitWeights(float[] w, Random rng)
        {
            for (int i = 0; i < w.Length; i++)
                w[i] = (float)(rng.NextDouble() * 0.5 - 0.25);
        }

        private float Tanh(float x) => MathF.Tanh(x);
        private float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

        public float[] Forward(float[] state13)
        {
            var (_, _, output) = ForwardWithCache(state13);
            return output;
        }

        private (float[] h1, float[] h2, float[] output) ForwardWithCache(float[] x)
        {
            float[] h1 = new float[32];
            for (int j = 0; j < 32; j++)
            {
                float sum = B1[j];
                for (int i = 0; i < 13; i++)
                    sum += W1[i, j] * x[i];
                h1[j] = Tanh(sum);
            }

            float[] h2 = new float[16];
            for (int k = 0; k < 16; k++)
            {
                float sum = B2[k];
                for (int j = 0; j < 32; j++)
                    sum += W2[j, k] * h1[j];
                h2[k] = Tanh(sum);
            }

            float[] output = new float[3];
            for (int m = 0; m < 3; m++)
            {
                float sum = B3[m];
                for (int k = 0; k < 16; k++)
                    sum += W3[k, m] * h2[k];
                output[m] = Sigmoid(sum);
            }

            return (h1, h2, output);
        }

        public void TrainBatch(List<(float[] state, float[] action, float weight)> samples)
        {
            float lr = 0.003f;
            foreach (var (state, action, weight) in samples)
            {
                var (h1, h2, output) = ForwardWithCache(state);

                float[] dOut = new float[3];
                for (int m = 0; m < 3; m++)
                {
                    dOut[m] = weight * (action[m] - output[m]) * output[m] * (1f - output[m]);
                    dOut[m] = Math.Clamp(dOut[m], -1f, 1f);
                }

                for (int k = 0; k < 16; k++)
                    for (int m = 0; m < 3; m++)
                        W3[k, m] += lr * dOut[m] * h2[k];
                for (int m = 0; m < 3; m++)
                    B3[m] += lr * dOut[m];

                float[] dH2 = new float[16];
                for (int k = 0; k < 16; k++)
                {
                    float sum = 0f;
                    for (int m = 0; m < 3; m++)
                        sum += dOut[m] * W3[k, m];
                    dH2[k] = sum * (1f - h2[k] * h2[k]);
                }

                for (int j = 0; j < 32; j++)
                    for (int k = 0; k < 16; k++)
                        W2[j, k] += lr * dH2[k] * h1[j];
                for (int k = 0; k < 16; k++)
                    B2[k] += lr * dH2[k];

                float[] dH1 = new float[32];
                for (int j = 0; j < 32; j++)
                {
                    float sum = 0f;
                    for (int k = 0; k < 16; k++)
                        sum += dH2[k] * W2[j, k];
                    dH1[j] = sum * (1f - h1[j] * h1[j]);
                }

                for (int i = 0; i < 13; i++)
                    for (int j = 0; j < 32; j++)
                        W1[i, j] += lr * dH1[j] * state[i];
                for (int j = 0; j < 32; j++)
                    B1[j] += lr * dH1[j];
            }
        }

        public void Save(string path)
        {
            using var writer = new BinaryWriter(File.Open(path, FileMode.Create));
            WriteMatrix(writer, W1, 13, 32);
            WriteArray(writer, B1, 32);
            WriteMatrix(writer, W2, 32, 16);
            WriteArray(writer, B2, 16);
            WriteMatrix(writer, W3, 16, 3);
            WriteArray(writer, B3, 3);
        }

        private void WriteMatrix(BinaryWriter w, float[,] m, int rows, int cols)
        {
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    w.Write(m[i, j]);
        }

        private void WriteArray(BinaryWriter w, float[] a, int len)
        {
            for (int i = 0; i < len; i++)
                w.Write(a[i]);
        }

        public bool Load(string path)
        {
            if (!File.Exists(path))
                return false;
            using var reader = new BinaryReader(File.Open(path, FileMode.Open));
            ReadMatrix(reader, W1, 13, 32);
            ReadArray(reader, B1, 32);
            ReadMatrix(reader, W2, 32, 16);
            ReadArray(reader, B2, 16);
            ReadMatrix(reader, W3, 16, 3);
            ReadArray(reader, B3, 3);
            return true;
        }

        private void ReadMatrix(BinaryReader r, float[,] m, int rows, int cols)
        {
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    m[i, j] = r.ReadSingle();
        }

        private void ReadArray(BinaryReader r, float[] a, int len)
        {
            for (int i = 0; i < len; i++)
                a[i] = r.ReadSingle();
        }

        public float[] WeightSnapshot()
        {
            var result = new List<float>();
            for (int i = 0; i < 13; i++)
                for (int j = 0; j < 32; j++)
                    result.Add(W1[i, j]);
            result.AddRange(B1);
            for (int j = 0; j < 32; j++)
                for (int k = 0; k < 16; k++)
                    result.Add(W2[j, k]);
            result.AddRange(B2);
            for (int k = 0; k < 16; k++)
                for (int m = 0; m < 3; m++)
                    result.Add(W3[k, m]);
            result.AddRange(B3);
            return result.ToArray();
        }
    }

    class TrainingManager
    {
        private PolicyNetwork _policy = new PolicyNetwork();
        private EnvironmentGrid _env;
        private CancellationTokenSource _cts;
        private object _lock = new object();
        private int _totalEps;
        private float _bestReward = -float.MaxValue;
        private float _recentAvg;
        private List<Vec2> _bestPath = new List<Vec2>();
        private List<float> _history = new List<float>();

        public PolicyNetwork Policy => _policy;
        public int TotalEpisodes => _totalEps;
        public float BestReward => _bestReward;
        public float RecentAvg => _recentAvg;
        public List<Vec2> BestPath => _bestPath;
        public List<float> History => _history;
        public bool Running => _cts != null && !_cts.IsCancellationRequested;

        public event EventHandler Updated;

        public void Start(EnvironmentGrid env, int workers = 8)
        {
            _env = env;
            _cts = new CancellationTokenSource();
            for (int i = 0; i < workers; i++)
            {
                Task.Run(() => TrainLoop(_cts.Token));
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        public static float[] StateToFeatures(BoatState s, EnvironmentGrid env)
        {
            float nx = s.Position.X / 25f;
            float ny = s.Position.Y / 25f;
            float vx = Math.Clamp(s.Velocity.X / BoatPhysics.MaxSpeed, -1f, 1f);
            float vy = Math.Clamp(s.Velocity.Y / BoatPhysics.MaxSpeed, -1f, 1f);
            float sin_h = MathF.Sin(s.Heading);
            float cos_h = MathF.Cos(s.Heading);
            Vec2 toB = BoatPhysics.PointB - s.Position;
            float tdAngle = MathF.Atan2(toB.Y, toB.X);
            float sin_td = MathF.Sin(tdAngle);
            float cos_td = MathF.Cos(tdAngle);
            float dist_n = toB.Length() / 28.28f;
            Vec2 wind = env.GetWindAt(s.Position.X, s.Position.Y);
            float wind_x = Math.Clamp(wind.X, -1f, 1f);
            float wind_y = Math.Clamp(wind.Y, -1f, 1f);
            Vec2 cur = env.GetCurrentAt(s.Position.X, s.Position.Y);
            float cur_x = Math.Clamp(cur.X, -1f, 1f);
            float cur_y = Math.Clamp(cur.Y, -1f, 1f);
            return new float[13] { nx, ny, vx, vy, sin_h, cos_h, sin_td, cos_td, dist_n, wind_x, wind_y, cur_x, cur_y };
        }

        private void TrainLoop(CancellationToken ct)
        {
            EnvironmentGrid localEnv = _env;
            Random rng = new Random(Thread.CurrentThread.ManagedThreadId);

            while (!ct.IsCancellationRequested)
            {
                BoatState state = BoatPhysics.InitialState();
                var steps = new List<(float[] state, float[] action)>();
                float epReward = 0f;

                for (int step = 0; step < 1000; step++)
                {
                    float[] feat = StateToFeatures(state, localEnv);
                    float[] act;
                    lock (_lock)
                        act = _policy.Forward(feat).ToArray();

                    float sigma = 0.15f;
                    act[0] = Math.Clamp(act[0] + (float)Gaussian(rng) * sigma, 0f, 1f);
                    act[1] = Math.Clamp(act[1] + (float)Gaussian(rng) * sigma, 0f, 1f);
                    act[2] = Math.Clamp(act[2] + (float)Gaussian(rng) * sigma, 0f, 1f);

                    var action = new BoatAction
                    {
                        PortThrottle = act[0],
                        StarThrottle = act[1],
                        SailAngle = act[2] * 2f - 1f
                    };

                    StepResult res = BoatPhysics.Step(state, action, localEnv);
                    steps.Add((feat, act));
                    epReward += res.Reward;
                    state = res.NextState;
                    if (res.Done)
                        break;
                }

                lock (_lock)
                {
                    _totalEps++;
                    if (_history.Count >= 100)
                        _history.RemoveAt(0);
                    _history.Add(epReward);
                    _recentAvg = _history.Count > 0 ? _history.Average() : 0f;

                    float weight = Math.Max(0f, epReward) / (MathF.Abs(_recentAvg) + 1f);

                    if (epReward > _recentAvg - 1f)
                    {
                        var batch = steps.Select(s => (s.state, s.action, weight)).ToList();
                        _policy.TrainBatch(batch);
                    }

                    if (epReward > _bestReward)
                    {
                        _bestReward = epReward;
                        _bestPath = state.Trail.ToList();
                        _bestPath.Insert(0, state.Position);
                    }
                }

                Updated?.Invoke(this, EventArgs.Empty);
            }
        }

        private static double Gaussian(Random r)
        {
            double u = r.NextDouble() + 1e-10;
            double v = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
        }
    }

    class NavigatorForm : Form
    {
        private PictureBox gridPb;
        private PictureBox rewardPb;
        private Label lblTitle;
        private Button btnStart;
        private Button btnStop;
        private Button btnReset;
        private Label lblEpisodes;
        private Label lblBest;
        private Label lblAvg;
        private Label lblStatus;
        private TrackBar spdSlider;
        private Panel rightPanel;

        private EnvironmentGrid _env = new EnvironmentGrid();
        private TrainingManager _manager = new TrainingManager();
        private BoatState _demoState;
        private System.Windows.Forms.Timer _animTimer;
        private int _demoStep;

        public NavigatorForm()
        {
            InitializeComponent();
            _env.Reset();
            _demoState = BoatPhysics.InitialState();
            _manager.Updated += (s, e) => BeginInvoke((Action)UpdateStats);
            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 100;
            _animTimer.Tick += OnTick;
            _animTimer.Start();

            btnStart.Click += (s, e) =>
            {
                _manager.Start(_env, 8);
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                lblStatus.Text = "Status: Training";
            };
            btnStop.Click += (s, e) =>
            {
                _manager.Stop();
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                lblStatus.Text = "Status: Stopped";
            };
            btnReset.Click += (s, e) =>
            {
                _manager.Stop();
                _env.Reset();
                _demoState = BoatPhysics.InitialState();
                _demoStep = 0;
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                lblStatus.Text = "Status: Reset";
                gridPb.Invalidate();
            };
            gridPb.Paint += (s, e) => DrawScene(e.Graphics);
            rewardPb.Paint += (s, e) => DrawRewardChart(e.Graphics);
        }

        private void InitializeComponent()
        {
            this.Text = "Boat Navigator AI";
            this.Size = new Size(940, 670);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            gridPb = new PictureBox();
            gridPb.Location = new Point(10, 10);
            gridPb.Size = new Size(600, 600);
            gridPb.BackColor = Color.Black;
            var dgbStyle = typeof(PictureBox).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (dgbStyle != null) dgbStyle.SetValue(gridPb, true);
            this.Controls.Add(gridPb);

            rightPanel = new Panel();
            rightPanel.Location = new Point(625, 10);
            rightPanel.Size = new Size(295, 640);
            this.Controls.Add(rightPanel);

            lblTitle = new Label();
            lblTitle.Text = "⚓ Boat Navigator AI";
            lblTitle.Font = new Font("Arial", 14f, FontStyle.Bold);
            lblTitle.Location = new Point(0, 0);
            lblTitle.AutoSize = true;
            rightPanel.Controls.Add(lblTitle);

            btnStart = new Button();
            btnStart.Text = "▶ Start Training";
            btnStart.Location = new Point(0, 35);
            btnStart.Width = 270;
            rightPanel.Controls.Add(btnStart);

            btnStop = new Button();
            btnStop.Text = "■ Stop";
            btnStop.Location = new Point(0, 65);
            btnStop.Width = 270;
            btnStop.Enabled = false;
            rightPanel.Controls.Add(btnStop);

            btnReset = new Button();
            btnReset.Text = "↺ Reset";
            btnReset.Location = new Point(0, 95);
            btnReset.Width = 270;
            rightPanel.Controls.Add(btnReset);

            lblEpisodes = new Label();
            lblEpisodes.Text = "Episodes: 0";
            lblEpisodes.Location = new Point(0, 135);
            lblEpisodes.AutoSize = true;
            rightPanel.Controls.Add(lblEpisodes);

            lblBest = new Label();
            lblBest.Text = "Best Reward: –";
            lblBest.Location = new Point(0, 158);
            lblBest.AutoSize = true;
            rightPanel.Controls.Add(lblBest);

            lblAvg = new Label();
            lblAvg.Text = "Avg (50): –";
            lblAvg.Location = new Point(0, 181);
            lblAvg.AutoSize = true;
            rightPanel.Controls.Add(lblAvg);

            lblStatus = new Label();
            lblStatus.Text = "Status: Idle";
            lblStatus.Location = new Point(0, 204);
            lblStatus.AutoSize = true;
            rightPanel.Controls.Add(lblStatus);

            rewardPb = new PictureBox();
            rewardPb.Location = new Point(0, 230);
            rewardPb.Size = new Size(270, 130);
            rewardPb.BackColor = Color.FromArgb(20, 20, 30);
            rightPanel.Controls.Add(rewardPb);

            var lblSpeed = new Label();
            lblSpeed.Text = "Demo Speed:";
            lblSpeed.Location = new Point(0, 370);
            lblSpeed.AutoSize = true;
            rightPanel.Controls.Add(lblSpeed);

            spdSlider = new TrackBar();
            spdSlider.Location = new Point(0, 388);
            spdSlider.Minimum = 1;
            spdSlider.Maximum = 10;
            spdSlider.Value = 5;
            spdSlider.Width = 270;
            rightPanel.Controls.Add(spdSlider);
        }

        private void OnTick(object s, EventArgs e)
        {
            _env.Update();
            float[] feat = TrainingManager.StateToFeatures(_demoState, _env);
            float[] act = _manager.Policy.Forward(feat);
            var action = new BoatAction
            {
                PortThrottle = act[0],
                StarThrottle = act[1],
                SailAngle = act[2] * 2f - 1f
            };
            StepResult res = BoatPhysics.Step(_demoState, action, _env);
            _demoState = res.NextState;
            if (res.Done)
            {
                _demoState = BoatPhysics.InitialState();
                _demoStep = 0;
            }
            _demoStep++;
            gridPb.Invalidate();
            rewardPb.Invalidate();
        }

        private void UpdateStats()
        {
            lblEpisodes.Text = $"Episodes: {_manager.TotalEpisodes:N0}";
            lblBest.Text = $"Best Reward: {_manager.BestReward:F1}";
            lblAvg.Text = $"Avg (50): {_manager.RecentAvg:F1}";
        }

        private void DrawScene(Graphics g)
        {
            const int CELL = 24;
            g.Clear(Color.FromArgb(15, 35, 70));

            for (int col = 0; col < 25; col++)
            {
                for (int row = 0; row < 25; row++)
                {
                    float cx = col * CELL + CELL / 2f;
                    float cy = row * CELL + CELL / 2f;
                    Vec2 w = _env.GetWindCell(col, row);
                    Vec2 c = _env.GetCurrentCell(col, row);
                    float turbulence = (w.Length() + c.Length()) / 0.2f;
                    if (turbulence > 1f) turbulence = 1f;
                    int blue = (int)(50 + turbulence * 40);
                    using (var cellBrush = new SolidBrush(Color.FromArgb(15, 35, blue)))
                    {
                        g.FillRectangle(cellBrush, col * CELL, row * CELL, CELL, CELL);
                    }
                    DrawArrow(g, Pens.Yellow, cx, cy, w.X * 80f, w.Y * 80f);
                    DrawArrow(g, Pens.Cyan, cx, cy, c.X * 120f, c.Y * 120f);
                }
            }

            using (var gridPen = new Pen(Color.FromArgb(40, 60, 100), 1f))
            {
                for (int i = 0; i <= 25; i++)
                {
                    g.DrawLine(gridPen, i * CELL, 0, i * CELL, 600);
                    g.DrawLine(gridPen, 0, i * CELL, 600, i * CELL);
                }
            }

            DrawMarker(g, BoatPhysics.PointA, Color.LimeGreen, "A");
            DrawMarker(g, BoatPhysics.PointB, Color.OrangeRed, "B");

            var path = _manager.BestPath;
            if (path.Count > 1)
            {
                for (int i = 1; i < path.Count; i++)
                {
                    int alpha = (int)(255f * i / path.Count);
                    using (var pp = new Pen(Color.FromArgb(alpha, 100, 200, 100), 1f))
                    {
                        g.DrawLine(pp, path[i - 1].X * CELL, path[i - 1].Y * CELL,
                            path[i].X * CELL, path[i].Y * CELL);
                    }
                }
            }

            var trail = _demoState.Trail;
            for (int i = 0; i < trail.Count; i++)
            {
                int alpha = (int)(180f * (1f - (float)i / trail.Count));
                using (var tb = new SolidBrush(Color.FromArgb(alpha, 220, 220, 220)))
                {
                    float tx = trail[i].X * CELL - 2;
                    float ty = trail[i].Y * CELL - 2;
                    g.FillEllipse(tb, tx, ty, 4, 4);
                }
            }

            DrawBoat(g, _demoState.Position, _demoState.Heading);
        }

        private void DrawArrow(Graphics g, Pen pen, float cx, float cy, float dx, float dy)
        {
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) return;
            g.DrawLine(pen, cx, cy, cx + dx, cy + dy);
            float a = MathF.Atan2(dy, dx);
            float hs = 4f;
            g.DrawLine(pen, cx + dx, cy + dy,
                cx + dx - hs * MathF.Cos(a + 2.4f),
                cy + dy - hs * MathF.Sin(a + 2.4f));
            g.DrawLine(pen, cx + dx, cy + dy,
                cx + dx - hs * MathF.Cos(a - 2.4f),
                cy + dy - hs * MathF.Sin(a - 2.4f));
        }

        private void DrawMarker(Graphics g, Vec2 pos, Color color, string label)
        {
            const int CELL = 24;
            float px = pos.X * CELL;
            float py = pos.Y * CELL;
            using (var b = new SolidBrush(color))
            {
                g.FillEllipse(b, px - 9, py - 9, 18, 18);
            }
            using (var f = new Font("Arial", 7f, FontStyle.Bold))
            using (var tb = new SolidBrush(Color.White))
            {
                g.DrawString(label, f, tb, px - 4, py - 5);
            }
        }

        private void DrawBoat(Graphics g, Vec2 pos, float heading)
        {
            const int CELL = 24;
            float px = pos.X * CELL;
            float py = pos.Y * CELL;
            float cos = MathF.Cos(heading);
            float sin = MathF.Sin(heading);
            var pts = new PointF[]
            {
                new PointF(px + cos * 10, py + sin * 10),
                new PointF(px + (-sin - cos * 0.6f) * 5, py + (cos - sin * 0.6f) * 5),
                new PointF(px + (sin - cos * 0.6f) * 5, py + (-cos - sin * 0.6f) * 5)
            };
            using (var b = new SolidBrush(Color.White))
            {
                g.FillPolygon(b, pts);
            }
            using (var p = new Pen(Color.Orange, 1f))
            {
                g.DrawPolygon(p, pts);
            }
        }

        private void DrawRewardChart(Graphics g)
        {
            var hist = _manager.History;
            int w = 270, h = 130;
            g.Clear(Color.FromArgb(20, 20, 30));
            if (hist.Count < 2) return;
            using (var axisPen = new Pen(Color.FromArgb(80, 80, 100)))
            {
                g.DrawLine(axisPen, 0, h - 1, w, h - 1);
            }
            float mn = hist.Min();
            float mx = hist.Max();
            float rng = mx - mn;
            if (rng < 0.1f) rng = 1f;
            using (var lp = new Pen(Color.LimeGreen, 1.5f))
            {
                for (int i = 1; i < hist.Count; i++)
                {
                    float x0 = (i - 1) * (w - 1f) / (hist.Count - 1);
                    float y0 = (h - 2) - (hist[i - 1] - mn) / rng * (h - 3);
                    float x1 = i * (w - 1f) / (hist.Count - 1);
                    float y1 = (h - 2) - (hist[i] - mn) / rng * (h - 3);
                    g.DrawLine(lp, x0, y0, x1, y1);
                }
            }
            float avgY = (h - 2) - (_manager.RecentAvg - mn) / rng * (h - 3);
            using (var ap = new Pen(Color.FromArgb(150, 255, 165, 0), 1f))
            {
                ap.DashStyle = DashStyle.Dash;
                g.DrawLine(ap, 0, avgY, w, avgY);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new NavigatorForm());
        }
    }
}
