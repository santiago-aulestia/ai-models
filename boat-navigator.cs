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

        public Vec2(float x, float y) { X = x; Y = y; }

        public static Vec2 Zero = new Vec2(0f, 0f);

        public static Vec2 FromAngle(float radians) => new Vec2(MathF.Cos(radians), MathF.Sin(radians));

        public float Length() => MathF.Sqrt(X * X + Y * Y);

        public Vec2 Normalized()
        {
            float len = Length();
            return len < 1e-8f ? Zero : new Vec2(X / len, Y / len);
        }

        public Vec2 Rotate(float radians)
        {
            float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
            return new Vec2(X * cos - Y * sin, X * sin + Y * cos);
        }

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

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
        public int Phase;   // 0 = heading to B, 1 = heading to C

        public BoatState Clone()
        {
            var clone = new BoatState();
            clone.Position = Position;
            clone.Velocity = Velocity;
            clone.Heading = Heading;
            clone.Trail = new List<Vec2>(Trail);
            clone.Phase = Phase;
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
        private int _size;
        public int Size => _size;

        private float[,] _windAngle;
        private float[,] _windMag;
        private float[,] _curAngle;
        private float[,] _curMag;
        private float _time;
        private Random _rng = new Random(1337);

        public EnvironmentGrid(int initialSize = 5)
        {
            _size = initialSize;
            AllocArrays();
            Reset();
        }

        private void AllocArrays()
        {
            _windAngle = new float[_size, _size];
            _windMag   = new float[_size, _size];
            _curAngle  = new float[_size, _size];
            _curMag    = new float[_size, _size];
        }

        public void Resize(int newSize)
        {
            newSize = Math.Clamp(newSize, 5, 25);
            if (newSize == _size) return;
            int n = newSize;
            var wa = new float[n, n]; var wm = new float[n, n];
            var ca = new float[n, n]; var cm = new float[n, n];
            int copy = Math.Min(_size, n);
            for (int c = 0; c < copy; c++)
                for (int r = 0; r < copy; r++)
                {
                    wa[c, r] = _windAngle[c, r]; wm[c, r] = _windMag[c, r];
                    ca[c, r] = _curAngle[c, r];  cm[c, r] = _curMag[c, r];
                }
            for (int c = 0; c < n; c++)
                for (int r = 0; r < n; r++)
                    if (c >= _size || r >= _size)
                    {
                        InitCell(wa, wm, c, r, true);
                        InitCell(ca, cm, c, r, false);
                    }
            _windAngle = wa; _windMag = wm; _curAngle = ca; _curMag = cm;
            _size = n;
        }

        private void InitCell(float[,] angle, float[,] mag, int c, int r, bool isWind)
        {
            float ph = isWind ? 0f : 1.3f;
            angle[c, r] = MathF.Sin(c * 0.5f + ph) * MathF.Cos(r * 0.4f + ph) * MathF.PI + (float)_rng.NextDouble() * 0.5f;
            mag[c, r] = isWind ? 0.05f + (float)_rng.NextDouble() * 0.07f : 0.02f + (float)_rng.NextDouble() * 0.04f;
        }

        public void Reset()
        {
            _time = 0f;
            for (int c = 0; c < _size; c++)
                for (int r = 0; r < _size; r++)
                {
                    _windAngle[c, r] = MathF.Sin(c * 0.5f) * MathF.Cos(r * 0.4f) * MathF.PI + (float)_rng.NextDouble() * 0.5f;
                    _windMag[c, r]   = 0.05f + (float)_rng.NextDouble() * 0.07f;
                    _curAngle[c, r]  = MathF.Sin(c * 0.5f + 1.3f) * MathF.Cos(r * 0.4f + 1.3f) * MathF.PI + (float)_rng.NextDouble() * 0.5f;
                    _curMag[c, r]    = 0.02f + (float)_rng.NextDouble() * 0.04f;
                }
        }

        public void Update()
        {
            _time += 0.01f;
            for (int c = 0; c < _size; c++)
                for (int r = 0; r < _size; r++)
                {
                    _windAngle[c, r] += ((float)_rng.NextDouble() - 0.5f) * 0.04f;
                    _windMag[c, r]    = Math.Clamp(_windMag[c, r]   + ((float)_rng.NextDouble() - 0.5f) * 0.005f, 0.02f, 0.12f);
                    _curAngle[c, r]  += ((float)_rng.NextDouble() - 0.5f) * 0.04f;
                    _curMag[c, r]     = Math.Clamp(_curMag[c, r]    + ((float)_rng.NextDouble() - 0.5f) * 0.005f, 0.01f, 0.06f);
                }
        }

        public Vec2 GetWindCell(int col, int row)    => Vec2.FromAngle(_windAngle[col, row]) * _windMag[col, row];
        public Vec2 GetCurrentCell(int col, int row) => Vec2.FromAngle(_curAngle[col, row])  * _curMag[col, row];

        public float GetWindAngle(int col, int row)      => _windAngle[col, row];
        public float GetWindMagnitude(int col, int row)  => _windMag[col, row];
        public float GetCurrentAngle(int col, int row)   => _curAngle[col, row];
        public float GetCurrentMagnitude(int col, int row) => _curMag[col, row];

        public void SetWindCell(int col, int row, float angleDeg, float mag)
        {
            _windAngle[col, row] = angleDeg * MathF.PI / 180f;
            _windMag[col, row]   = Math.Clamp(mag, 0.02f, 0.12f);
        }

        public void SetCurrentCell(int col, int row, float angleDeg, float mag)
        {
            _curAngle[col, row] = angleDeg * MathF.PI / 180f;
            _curMag[col, row]   = Math.Clamp(mag, 0.01f, 0.06f);
        }

        public Vec2 GetWindAt(float x, float y)
        {
            x = Math.Clamp(x, 0f, _size - 1f);
            y = Math.Clamp(y, 0f, _size - 1f);
            int c0 = (int)x, r0 = (int)y;
            int c1 = Math.Min(c0 + 1, _size - 1), r1 = Math.Min(r0 + 1, _size - 1);
            float tx = x - c0, ty = y - r0;
            Vec2 top = GetWindCell(c0, r0) * (1f - tx) + GetWindCell(c1, r0) * tx;
            Vec2 bot = GetWindCell(c0, r1) * (1f - tx) + GetWindCell(c1, r1) * tx;
            return top * (1f - ty) + bot * ty;
        }

        public Vec2 GetCurrentAt(float x, float y)
        {
            x = Math.Clamp(x, 0f, _size - 1f);
            y = Math.Clamp(y, 0f, _size - 1f);
            int c0 = (int)x, r0 = (int)y;
            int c1 = Math.Min(c0 + 1, _size - 1), r1 = Math.Min(r0 + 1, _size - 1);
            float tx = x - c0, ty = y - r0;
            Vec2 top = GetCurrentCell(c0, r0) * (1f - tx) + GetCurrentCell(c1, r0) * tx;
            Vec2 bot = GetCurrentCell(c0, r1) * (1f - tx) + GetCurrentCell(c1, r1) * tx;
            return top * (1f - ty) + bot * ty;
        }
    }

    static class BoatPhysics
    {
        public static Vec2 PointA = new Vec2(1.0f, 1.0f);
        public static Vec2 PointB = new Vec2(4.0f, 4.0f);
        public static Vec2 PointC = new Vec2(4.0f, 1.0f);
        public static float ArrivalRadius = 1.2f;
        public const int   TrailLength   = 40;
        public const float MaxSpeed       = 0.15f;
        public const float Drag           = 0.96f;
        public const float EnginePower    = 0.06f;
        public const float TurnRate       = 0.12f;
        public const float SailEfficiency = 0.04f;

        public static void UpdatePoints(int gridSize, int cornerA = 0, int cornerB = 3, int cornerC = 1)
        {
            float m = gridSize * 0.1f + 0.5f, f = gridSize - m;
            Vec2[] corners = { new Vec2(m, m), new Vec2(f, m), new Vec2(m, f), new Vec2(f, f) };
            PointA = corners[Math.Clamp(cornerA, 0, 3)];
            PointB = corners[Math.Clamp(cornerB, 0, 3)];
            PointC = corners[Math.Clamp(cornerC, 0, 3)];
        }

        public static BoatState InitialState()
        {
            var state = new BoatState();
            state.Position = PointA;
            state.Velocity = Vec2.Zero;
            state.Heading  = MathF.Atan2(PointB.Y - PointA.Y, PointB.X - PointA.X);
            state.Trail    = new List<Vec2>();
            state.Phase    = 0;
            return state;
        }

        public static StepResult Step(BoatState s, BoatAction action, EnvironmentGrid env)
        {
            float gs = env.Size;

            float newHeading = s.Heading + TurnRate * (action.PortThrottle - action.StarThrottle);
            Vec2  thrust     = Vec2.FromAngle(newHeading) * ((action.PortThrottle + action.StarThrottle) * EnginePower);

            Vec2  wind      = env.GetWindAt(s.Position.X, s.Position.Y);
            float windAngle = MathF.Atan2(wind.Y, wind.X);
            float eff       = MathF.Cos(windAngle - newHeading) * action.SailAngle * SailEfficiency;
            Vec2  sailForce = wind.Normalized() * eff;

            Vec2 current = env.GetCurrentAt(s.Position.X, s.Position.Y);
            Vec2 newVel  = (s.Velocity + thrust + sailForce) * Drag + current * 0.3f;
            if (newVel.Length() > MaxSpeed) newVel = newVel.Normalized() * MaxSpeed;

            Vec2 newPos = s.Position + newVel;

            var trail = new List<Vec2>();
            trail.Add(newPos);
            int take = Math.Min(s.Trail.Count, TrailLength - 1);
            for (int i = 0; i < take; i++) trail.Add(s.Trail[i]);

            bool oob       = newPos.X < 0f || newPos.X >= gs || newPos.Y < 0f || newPos.Y >= gs;
            Vec2 target    = s.Phase == 0 ? PointB : PointC;
            Vec2 clamped   = new Vec2(Math.Clamp(newPos.X, 0f, gs - 0.1f), Math.Clamp(newPos.Y, 0f, gs - 0.1f));
            float dist     = (clamped - target).Length();
            float maxDist  = MathF.Sqrt(2f) * gs;

            int   newPhase = s.Phase;
            float reward;
            bool  done;
            if (oob)
            {
                reward = -2.0f; done = true;
            }
            else if (dist < ArrivalRadius)
            {
                if (s.Phase == 0)
                {
                    reward     = 5.0f; done = false; newPhase = 1;
                    newVel     = Vec2.Zero;
                    newHeading = MathF.Atan2(PointC.Y - newPos.Y, PointC.X - newPos.X);
                }
                else { reward = 10.0f; done = true; }
            }
            else
            {
                reward = 1.0f - dist / maxDist; done = false;
            }

            var next = new BoatState
            {
                Position = newPos,
                Velocity = newVel,
                Heading  = newHeading,
                Trail    = trail,
                Phase    = newPhase
            };
            return new StepResult { NextState = next, Reward = reward, Done = done };
        }
    }

    class PolicyNetwork
    {
        private float[,] W1 = new float[13, 32];
        private float[]  B1 = new float[32];
        private float[,] W2 = new float[32, 16];
        private float[]  B2 = new float[16];
        private float[,] W3 = new float[16, 3];
        private float[]  B3 = new float[3];

        public PolicyNetwork()
        {
            var rng = new Random(42);
            Init(W1, rng); Init(B1, rng); Init(W2, rng); Init(B2, rng); Init(W3, rng); Init(B3, rng);
        }

        private void Init(float[,] w, Random r) { for (int i = 0; i < w.GetLength(0); i++) for (int j = 0; j < w.GetLength(1); j++) w[i,j] = (float)(r.NextDouble() * 0.5 - 0.25); }
        private void Init(float[]  w, Random r) { for (int i = 0; i < w.Length; i++) w[i] = (float)(r.NextDouble() * 0.5 - 0.25); }

        private float Tanh(float x)    => MathF.Tanh(x);
        private float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

        public float[] Forward(float[] state13) { var (_, _, o) = Fwd(state13); return o; }

        private (float[] h1, float[] h2, float[] output) Fwd(float[] x)
        {
            float[] h1 = new float[32];
            for (int j = 0; j < 32; j++) { float s = B1[j]; for (int i = 0; i < 13; i++) s += W1[i,j]*x[i]; h1[j] = Tanh(s); }
            float[] h2 = new float[16];
            for (int k = 0; k < 16; k++) { float s = B2[k]; for (int j = 0; j < 32; j++) s += W2[j,k]*h1[j]; h2[k] = Tanh(s); }
            float[] o  = new float[3];
            for (int m = 0; m < 3;  m++) { float s = B3[m]; for (int k = 0; k < 16; k++) s += W3[k,m]*h2[k]; o[m]  = Sigmoid(s); }
            return (h1, h2, o);
        }

        public void TrainBatch(List<(float[] state, float[] action, float weight)> samples)
        {
            float lr = 0.003f;
            foreach (var (state, action, weight) in samples)
            {
                var (h1, h2, output) = Fwd(state);
                float[] dOut = new float[3];
                for (int m = 0; m < 3; m++)
                    dOut[m] = Math.Clamp(weight * (action[m] - output[m]) * output[m] * (1f - output[m]), -1f, 1f);

                for (int k = 0; k < 16; k++) for (int m = 0; m < 3; m++) W3[k,m] += lr * dOut[m] * h2[k];
                for (int m = 0; m < 3;  m++) B3[m] += lr * dOut[m];

                float[] dH2 = new float[16];
                for (int k = 0; k < 16; k++) { float s = 0f; for (int m = 0; m < 3; m++) s += dOut[m]*W3[k,m]; dH2[k] = s*(1f-h2[k]*h2[k]); }
                for (int j = 0; j < 32; j++) for (int k = 0; k < 16; k++) W2[j,k] += lr * dH2[k] * h1[j];
                for (int k = 0; k < 16; k++) B2[k] += lr * dH2[k];

                float[] dH1 = new float[32];
                for (int j = 0; j < 32; j++) { float s = 0f; for (int k = 0; k < 16; k++) s += dH2[k]*W2[j,k]; dH1[j] = s*(1f-h1[j]*h1[j]); }
                for (int i = 0; i < 13; i++) for (int j = 0; j < 32; j++) W1[i,j] += lr * dH1[j] * state[i];
                for (int j = 0; j < 32; j++) B1[j] += lr * dH1[j];
            }
        }

        public void Save(string path)
        {
            using var w = new BinaryWriter(File.Open(path, FileMode.Create));
            void WM(float[,] m, int r, int c) { for (int i=0;i<r;i++) for (int j=0;j<c;j++) w.Write(m[i,j]); }
            void WA(float[]  a, int n) { for (int i=0;i<n;i++) w.Write(a[i]); }
            WM(W1,13,32); WA(B1,32); WM(W2,32,16); WA(B2,16); WM(W3,16,3); WA(B3,3);
        }

        public bool Load(string path)
        {
            if (!File.Exists(path)) return false;
            using var r = new BinaryReader(File.Open(path, FileMode.Open));
            void RM(float[,] m, int rows, int cols) { for (int i=0;i<rows;i++) for (int j=0;j<cols;j++) m[i,j]=r.ReadSingle(); }
            void RA(float[]  a, int n)   { for (int i=0;i<n;i++) a[i]=r.ReadSingle(); }
            RM(W1,13,32); RA(B1,32); RM(W2,32,16); RA(B2,16); RM(W3,16,3); RA(B3,3);
            return true;
        }

        public float[] WeightSnapshot()
        {
            var list = new List<float>();
            for (int i=0;i<13;i++) for (int j=0;j<32;j++) list.Add(W1[i,j]);
            list.AddRange(B1);
            for (int j=0;j<32;j++) for (int k=0;k<16;k++) list.Add(W2[j,k]);
            list.AddRange(B2);
            for (int k=0;k<16;k++) for (int m=0;m<3;m++) list.Add(W3[k,m]);
            list.AddRange(B3);
            return list.ToArray();
        }
    }

    class TrainingManager
    {
        private PolicyNetwork _policy = new PolicyNetwork();
        private EnvironmentGrid _env;
        private CancellationTokenSource _cts;
        private object _lock    = new object();
        private int   _totalEps;
        private float _bestReward = -float.MaxValue;
        private float _recentAvg;
        private List<Vec2>  _bestPath = new List<Vec2>();
        private List<float> _history  = new List<float>();
        private int   _waypointHits;
        private int   _fullRuns;

        public PolicyNetwork Policy       => _policy;
        public int   TotalEpisodes        => _totalEps;
        public float BestReward           => _bestReward;
        public float RecentAvg            => _recentAvg;
        public List<Vec2>  BestPath       => _bestPath;
        public List<float> History        => _history;
        public int   WaypointHits         => _waypointHits;
        public int   FullRuns             => _fullRuns;
        public bool  Running              => _cts != null && !_cts.IsCancellationRequested;

        public event EventHandler Updated;

        public void Start(EnvironmentGrid env, int workers = 8)
        {
            _env = env;
            _cts = new CancellationTokenSource();
            for (int i = 0; i < workers; i++) Task.Run(() => TrainLoop(_cts.Token));
        }

        public void Stop() => _cts?.Cancel();

        public static float[] StateToFeatures(BoatState s, EnvironmentGrid env)
        {
            float gs   = env.Size;
            float nx   = s.Position.X / gs;
            float ny   = s.Position.Y / gs;
            float vx   = Math.Clamp(s.Velocity.X / BoatPhysics.MaxSpeed, -1f, 1f);
            float vy   = Math.Clamp(s.Velocity.Y / BoatPhysics.MaxSpeed, -1f, 1f);
            float sinh = MathF.Sin(s.Heading);
            float cosh = MathF.Cos(s.Heading);
            Vec2  curTarget = s.Phase == 0 ? BoatPhysics.PointB : BoatPhysics.PointC;
            Vec2  toB  = curTarget - s.Position;
            float tdA  = MathF.Atan2(toB.Y, toB.X);
            float sin_td = MathF.Sin(tdA), cos_td = MathF.Cos(tdA);
            float dist_n = toB.Length() / (MathF.Sqrt(2f) * gs);
            Vec2  wind = env.GetWindAt(s.Position.X, s.Position.Y);
            Vec2  cur  = env.GetCurrentAt(s.Position.X, s.Position.Y);
            return new float[13]
            {
                nx, ny, vx, vy, sinh, cosh, sin_td, cos_td, dist_n,
                Math.Clamp(wind.X,-1f,1f), Math.Clamp(wind.Y,-1f,1f),
                Math.Clamp(cur.X, -1f,1f), Math.Clamp(cur.Y, -1f,1f)
            };
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
                bool hitB = false, hitC = false;

                for (int step = 0; step < 1000 && !ct.IsCancellationRequested; step++)
                {
                    float[] feat = StateToFeatures(state, localEnv);
                    float[] act;
                    lock (_lock) act = _policy.Forward(feat).ToArray();

                    const float sigma = 0.15f;
                    act[0] = Math.Clamp(act[0] + (float)Gauss(rng) * sigma, 0f, 1f);
                    act[1] = Math.Clamp(act[1] + (float)Gauss(rng) * sigma, 0f, 1f);
                    act[2] = Math.Clamp(act[2] + (float)Gauss(rng) * sigma, 0f, 1f);

                    var action = new BoatAction { PortThrottle = act[0], StarThrottle = act[1], SailAngle = act[2] * 2f - 1f };
                    StepResult res = BoatPhysics.Step(state, action, localEnv);
                    steps.Add((feat, act));
                    epReward += res.Reward;
                    if (res.NextState.Phase > state.Phase) hitB = true;
                    if (res.Done && res.Reward > 5f)       hitC = true;
                    state = res.NextState;
                    if (res.Done) break;
                }

                lock (_lock)
                {
                    _totalEps++;
                    if (hitB) _waypointHits++;
                    if (hitC) _fullRuns++;
                    if (_history.Count >= 100) _history.RemoveAt(0);
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

        private static double Gauss(Random r)
        {
            double u = r.NextDouble() + 1e-10, v = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
        }
    }

    class NavigatorForm : Form
    {
        // --- left side ---
        private PictureBox gridPb;

        // --- right panel controls ---
        private Panel       rightPanel;
        private Label       lblTitle;
        private Button      btnStart, btnStop, btnReset;
        private Label         lblGridSize;
        private NumericUpDown numGridSize;
        private NumericUpDown numRadius;
        private ComboBox      cmbA, cmbB, cmbC;
        private Label       lblEpisodes, lblBest, lblAvg, lblArrival, lblStatus;
        private PictureBox  rewardPb;
        private Label       lblSpeed;
        private TrackBar    spdSlider;
        private Label       lblCellHeader, lblCellCoord;
        private Label       lblWindDirL, lblWindMagL, lblCurDirL, lblCurMagL;
        private NumericUpDown numWindDir, numWindMag, numCurDir, numCurMag;

        // --- state ---
        private EnvironmentGrid  _env     = new EnvironmentGrid(5);
        private TrainingManager  _manager = new TrainingManager();
        private BoatState        _demoState;
        private System.Windows.Forms.Timer _animTimer;
        private int              _demoStep;
        private (int col, int row)? _selectedCell;
        private bool             _suppressEdit;

        private int CellSize => 600 / _env.Size;

        public NavigatorForm()
        {
            BoatPhysics.UpdatePoints(_env.Size);
            _demoState = BoatPhysics.InitialState();
            InitializeComponent();
            _manager.Updated += (s, e) => { if (!IsDisposed) BeginInvoke((Action)UpdateStats); };
            _animTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _animTimer.Tick += OnTick;
            _animTimer.Start();

            btnStart.Click += (s, e) =>
            {
                _manager.Start(_env, 8);
                btnStart.Enabled = false; btnStop.Enabled = true;
                lblStatus.Text = "Status: Training";
            };
            btnStop.Click += (s, e) =>
            {
                _manager.Stop();
                btnStart.Enabled = true; btnStop.Enabled = false;
                lblStatus.Text = "Status: Stopped";
                WriteSessionLog();
            };
            btnReset.Click += (s, e) =>
            {
                _manager.Stop();
                _env.Reset();
                _demoState = BoatPhysics.InitialState();
                _demoStep  = 0;
                _selectedCell = null;
                lblCellCoord.Text = "Click grid to select";
                ClearCellEditor();
                btnStart.Enabled = true; btnStop.Enabled = false;
                lblStatus.Text = "Status: Reset";
                gridPb.Invalidate();
            };
            numGridSize.ValueChanged += (s, e) =>
            {
                int newSize = (int)numGridSize.Value;
                if (newSize == _env.Size) return;
                _manager.Stop();
                _env.Resize(newSize);
                BoatPhysics.UpdatePoints(_env.Size, cmbA.SelectedIndex, cmbB.SelectedIndex, cmbC.SelectedIndex);
                _manager = new TrainingManager();
                _manager.Updated += (ms, me) => { if (!IsDisposed) BeginInvoke((Action)UpdateStats); };
                _demoState    = BoatPhysics.InitialState();
                _demoStep     = 0;
                _selectedCell = null;
                lblCellCoord.Text = "Click grid to select";
                ClearCellEditor();
                btnStart.Enabled   = true; btnStop.Enabled = false;
                lblStatus.Text     = "Status: Idle";
                UpdateStats();
                gridPb.Invalidate();
            };

            Action applyCorners = () =>
            {
                _manager.Stop();
                BoatPhysics.UpdatePoints(_env.Size, cmbA.SelectedIndex, cmbB.SelectedIndex, cmbC.SelectedIndex);
                _manager = new TrainingManager();
                _manager.Updated += (ms, me) => { if (!IsDisposed) BeginInvoke((Action)UpdateStats); };
                _demoState = BoatPhysics.InitialState();
                _demoStep  = 0;
                btnStart.Enabled = true; btnStop.Enabled = false;
                lblStatus.Text = "Status: Idle";
                UpdateStats();
                gridPb.Invalidate();
            };
            cmbA.SelectedIndexChanged += (s, e) => applyCorners();
            cmbB.SelectedIndexChanged += (s, e) => applyCorners();
            cmbC.SelectedIndexChanged += (s, e) => applyCorners();

            numRadius.ValueChanged += (s, e) =>
            {
                BoatPhysics.ArrivalRadius = (float)numRadius.Value;
                gridPb.Invalidate();
            };

            gridPb.Paint      += (s, e) => DrawScene(e.Graphics);
            rewardPb.Paint    += (s, e) => DrawRewardChart(e.Graphics);

            gridPb.MouseClick += (s, e) =>
            {
                int cs = CellSize;
                int col = e.X / cs, row = e.Y / cs;
                if (col >= 0 && col < _env.Size && row >= 0 && row < _env.Size)
                {
                    _selectedCell = (col, row);
                    LoadCellEditor(col, row);
                    gridPb.Invalidate();
                }
            };

            numWindDir.ValueChanged += (s, e) => ApplyCellEdit();
            numWindMag.ValueChanged += (s, e) => ApplyCellEdit();
            numCurDir.ValueChanged  += (s, e) => ApplyCellEdit();
            numCurMag.ValueChanged  += (s, e) => ApplyCellEdit();
            spdSlider.ValueChanged  += (s, e) => _animTimer.Interval = Math.Max(16, 500 / spdSlider.Value);
        }

        private void LoadCellEditor(int col, int row)
        {
            _suppressEdit = true;
            float wDeg = _env.GetWindAngle(col, row) * 180f / MathF.PI;
            wDeg = ((wDeg % 360f) + 360f) % 360f;
            numWindDir.Value = (decimal)Math.Round(wDeg);
            numWindMag.Value = (decimal)Math.Round(_env.GetWindMagnitude(col, row), 3);
            float cDeg = _env.GetCurrentAngle(col, row) * 180f / MathF.PI;
            cDeg = ((cDeg % 360f) + 360f) % 360f;
            numCurDir.Value = (decimal)Math.Round(cDeg);
            numCurMag.Value = (decimal)Math.Round(_env.GetCurrentMagnitude(col, row), 3);
            lblCellCoord.Text = $"Cell ({col}, {row})";
            _suppressEdit = false;
        }

        private void ApplyCellEdit()
        {
            if (_suppressEdit || !_selectedCell.HasValue) return;
            var (col, row) = _selectedCell.Value;
            _env.SetWindCell(col, row, (float)numWindDir.Value, (float)numWindMag.Value);
            _env.SetCurrentCell(col, row, (float)numCurDir.Value, (float)numCurMag.Value);
            gridPb.Invalidate();
        }

        private void ClearCellEditor()
        {
            _suppressEdit = true;
            numWindDir.Value = 0; numWindMag.Value = (decimal)0.05;
            numCurDir.Value  = 0; numCurMag.Value  = (decimal)0.02;
            _suppressEdit = false;
        }

        private void InitializeComponent()
        {
            this.Text            = "Boat Navigator AI";
            this.Size            = new Size(940, 670);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;

            gridPb = new PictureBox { Location = new Point(10, 10), Size = new Size(600, 600), BackColor = Color.Black };
            var db = typeof(PictureBox).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            db?.SetValue(gridPb, true);
            this.Controls.Add(gridPb);

            rightPanel = new Panel { Location = new Point(625, 10), Size = new Size(295, 640) };
            this.Controls.Add(rightPanel);

            int y = 0;

            lblTitle = new Label { Text = "⚓ Boat Navigator AI", Font = new Font("Arial", 13f, FontStyle.Bold), Location = new Point(0, y), AutoSize = true };
            rightPanel.Controls.Add(lblTitle); y += 30;

            btnStart = new Button { Text = "▶ Start Training", Location = new Point(0, y), Width = 270 };
            rightPanel.Controls.Add(btnStart); y += 28;

            btnStop = new Button { Text = "■ Stop", Location = new Point(0, y), Width = 270, Enabled = false };
            rightPanel.Controls.Add(btnStop); y += 28;

            btnReset = new Button { Text = "↺ Reset", Location = new Point(0, y), Width = 270 };
            rightPanel.Controls.Add(btnReset); y += 32;

            lblGridSize = new Label { Text = "Grid Size:", Location = new Point(0, y + 3), AutoSize = true };
            rightPanel.Controls.Add(lblGridSize);
            numGridSize = new NumericUpDown { Location = new Point(88, y), Width = 72, Minimum = 5, Maximum = 25, Value = 5, Increment = 1, DecimalPlaces = 0 };
            rightPanel.Controls.Add(numGridSize); y += 30;

            var lblRadius = new Label { Text = "Arrival r:", Location = new Point(0, y + 3), AutoSize = true };
            numRadius = new NumericUpDown { Location = new Point(88, y), Width = 72, Minimum = (decimal)0.3, Maximum = (decimal)5.0, Value = (decimal)1.2, Increment = (decimal)0.1, DecimalPlaces = 1 };
            rightPanel.Controls.Add(lblRadius); rightPanel.Controls.Add(numRadius); y += 30;

            var lwpA = new Label { Text = "A:", Location = new Point(0, y + 3), AutoSize = true };
            cmbA = new ComboBox { Location = new Point(18, y), Width = 72, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbA.Items.AddRange(new string[] { "Top-Left", "Top-Right", "Bot-Left", "Bot-Right" }); cmbA.SelectedIndex = 0;
            var lwpB = new Label { Text = "B:", Location = new Point(95, y + 3), AutoSize = true };
            cmbB = new ComboBox { Location = new Point(113, y), Width = 72, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbB.Items.AddRange(new string[] { "Top-Left", "Top-Right", "Bot-Left", "Bot-Right" }); cmbB.SelectedIndex = 3;
            var lwpC = new Label { Text = "C:", Location = new Point(190, y + 3), AutoSize = true };
            cmbC = new ComboBox { Location = new Point(208, y), Width = 62, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbC.Items.AddRange(new string[] { "Top-Left", "Top-Right", "Bot-Left", "Bot-Right" }); cmbC.SelectedIndex = 1;
            rightPanel.Controls.Add(lwpA); rightPanel.Controls.Add(cmbA);
            rightPanel.Controls.Add(lwpB); rightPanel.Controls.Add(cmbB);
            rightPanel.Controls.Add(lwpC); rightPanel.Controls.Add(cmbC);
            y += 30;

            lblEpisodes = new Label { Text = "Episodes: 0",    Location = new Point(0, y), AutoSize = true }; rightPanel.Controls.Add(lblEpisodes); y += 20;
            lblBest     = new Label { Text = "Best Reward: –", Location = new Point(0, y), AutoSize = true }; rightPanel.Controls.Add(lblBest);     y += 20;
            lblAvg      = new Label { Text = "Avg (50): –",    Location = new Point(0, y), AutoSize = true }; rightPanel.Controls.Add(lblAvg);      y += 20;
            lblArrival  = new Label { Text = "→B: 0  ✓A→B→C: 0", Location = new Point(0, y), AutoSize = true }; rightPanel.Controls.Add(lblArrival);  y += 20;
            lblStatus   = new Label { Text = "Status: Idle",   Location = new Point(0, y), AutoSize = true }; rightPanel.Controls.Add(lblStatus);   y += 22;

            rewardPb = new PictureBox { Location = new Point(0, y), Size = new Size(270, 110), BackColor = Color.FromArgb(20, 20, 30) };
            rightPanel.Controls.Add(rewardPb); y += 118;

            lblSpeed = new Label { Text = "Demo Speed:", Location = new Point(0, y), AutoSize = true };
            rightPanel.Controls.Add(lblSpeed); y += 18;
            spdSlider = new TrackBar { Location = new Point(0, y), Minimum = 1, Maximum = 10, Value = 5, Width = 270 };
            rightPanel.Controls.Add(spdSlider); y += 40;

            // ── Cell Editor ──────────────────────────────────────
            lblCellHeader = new Label { Text = "── Cell Editor ──", Font = new Font("Arial", 8f, FontStyle.Bold), Location = new Point(0, y), AutoSize = true };
            rightPanel.Controls.Add(lblCellHeader); y += 20;

            lblCellCoord = new Label { Text = "Click grid to select", Location = new Point(0, y), AutoSize = true };
            rightPanel.Controls.Add(lblCellCoord); y += 22;

            lblWindDirL = new Label { Text = "Wind Dir (°):", Location = new Point(0, y + 3), AutoSize = true };
            numWindDir  = new NumericUpDown { Location = new Point(100, y), Width = 168, Minimum = 0, Maximum = 360, DecimalPlaces = 0, Increment = 5 };
            rightPanel.Controls.Add(lblWindDirL); rightPanel.Controls.Add(numWindDir); y += 28;

            lblWindMagL = new Label { Text = "Wind Mag:", Location = new Point(0, y + 3), AutoSize = true };
            numWindMag  = new NumericUpDown { Location = new Point(100, y), Width = 168, Minimum = (decimal)0.02, Maximum = (decimal)0.12, DecimalPlaces = 3, Increment = (decimal)0.005, Value = (decimal)0.05 };
            rightPanel.Controls.Add(lblWindMagL); rightPanel.Controls.Add(numWindMag); y += 28;

            lblCurDirL = new Label { Text = "Curr Dir (°):", Location = new Point(0, y + 3), AutoSize = true };
            numCurDir  = new NumericUpDown { Location = new Point(100, y), Width = 168, Minimum = 0, Maximum = 360, DecimalPlaces = 0, Increment = 5 };
            rightPanel.Controls.Add(lblCurDirL); rightPanel.Controls.Add(numCurDir); y += 28;

            lblCurMagL = new Label { Text = "Curr Mag:", Location = new Point(0, y + 3), AutoSize = true };
            numCurMag  = new NumericUpDown { Location = new Point(100, y), Width = 168, Minimum = (decimal)0.01, Maximum = (decimal)0.06, DecimalPlaces = 3, Increment = (decimal)0.002, Value = (decimal)0.02 };
            rightPanel.Controls.Add(lblCurMagL); rightPanel.Controls.Add(numCurMag);
        }

        private void OnTick(object s, EventArgs e)
        {
            _env.Update();
            float[] feat = TrainingManager.StateToFeatures(_demoState, _env);
            float[] act  = _manager.Policy.Forward(feat);
            var action = new BoatAction { PortThrottle = act[0], StarThrottle = act[1], SailAngle = act[2] * 2f - 1f };
            StepResult res = BoatPhysics.Step(_demoState, action, _env);
            _demoState = res.NextState;
            if (res.Done) { _demoState = BoatPhysics.InitialState(); _demoStep = 0; }
            _demoStep++;
            gridPb.Invalidate();
            rewardPb.Invalidate();
        }

        private void UpdateStats()
        {
            lblEpisodes.Text = $"Episodes: {_manager.TotalEpisodes:N0}";
            lblBest.Text     = $"Best Reward: {_manager.BestReward:F1}";
            lblAvg.Text      = $"Avg (50): {_manager.RecentAvg:F1}";
            lblArrival.Text  = $"→B: {_manager.WaypointHits:N0}  ✓A→B→C: {_manager.FullRuns:N0}";
        }

        private void WriteSessionLog()
        {
            try
            {
                Directory.CreateDirectory(".data");
                string ts   = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string path = $".data/session-{ts}.log";
                float bHitPct  = _manager.TotalEpisodes > 0
                    ? 100f * _manager.WaypointHits / _manager.TotalEpisodes : 0f;
                float fullPct  = _manager.TotalEpisodes > 0
                    ? 100f * _manager.FullRuns      / _manager.TotalEpisodes : 0f;
                string corners = $"A={cmbA.Text}  B={cmbB.Text}  C={cmbC.Text}";
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== Boat Navigator Session  {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                sb.AppendLine($"Grid size      : {_env.Size}x{_env.Size}");
                sb.AppendLine($"Waypoints      : {corners}");
                sb.AppendLine($"Episodes       : {_manager.TotalEpisodes:N0}");
                sb.AppendLine($"→B hits        : {_manager.WaypointHits:N0}  ({bHitPct:F1}%)");
                sb.AppendLine($"✓ Full A→B→C  : {_manager.FullRuns:N0}  ({fullPct:F1}%)");
                sb.AppendLine($"Best reward    : {_manager.BestReward:F2}");
                sb.AppendLine($"Avg (last 100) : {_manager.RecentAvg:F2}");
                sb.AppendLine($"PointA         : ({BoatPhysics.PointA.X:F2}, {BoatPhysics.PointA.Y:F2})");
                sb.AppendLine($"PointB         : ({BoatPhysics.PointB.X:F2}, {BoatPhysics.PointB.Y:F2})");
                sb.AppendLine($"PointC         : ({BoatPhysics.PointC.X:F2}, {BoatPhysics.PointC.Y:F2})");
                File.WriteAllText(path, sb.ToString());
                lblStatus.Text = $"Status: Stopped  (log saved)";
            }
            catch { }
        }

        private void DrawScene(Graphics g)
        {
            int  CELL  = CellSize;
            int  size  = _env.Size;
            float aScale = CELL / 24f;

            g.Clear(Color.FromArgb(15, 35, 70));

            for (int col = 0; col < size; col++)
            {
                for (int row = 0; row < size; row++)
                {
                    float cx = col * CELL + CELL / 2f;
                    float cy = row * CELL + CELL / 2f;
                    Vec2 w = _env.GetWindCell(col, row);
                    Vec2 c = _env.GetCurrentCell(col, row);
                    float turb = Math.Min(1f, (w.Length() + c.Length()) / 0.2f);
                    int blue = (int)(50 + turb * 40);
                    using (var cb = new SolidBrush(Color.FromArgb(15, 35, blue)))
                        g.FillRectangle(cb, col * CELL, row * CELL, CELL, CELL);

                    DrawArrow(g, Pens.Yellow, cx, cy, w.X * 80f * aScale, w.Y * 80f * aScale);
                    DrawArrow(g, Pens.Cyan,   cx, cy, c.X * 120f * aScale, c.Y * 120f * aScale);
                }
            }

            using (var gp = new Pen(Color.FromArgb(40, 60, 100), 1f))
                for (int i = 0; i <= size; i++)
                {
                    g.DrawLine(gp, i * CELL, 0, i * CELL, size * CELL);
                    g.DrawLine(gp, 0, i * CELL, size * CELL, i * CELL);
                }

            if (_selectedCell.HasValue)
            {
                var (sc, sr) = _selectedCell.Value;
                using (var sp = new Pen(Color.White, 2f))
                    g.DrawRectangle(sp, sc * CELL + 1, sr * CELL + 1, CELL - 2, CELL - 2);
            }

            DrawMarker(g, BoatPhysics.PointA, Color.LimeGreen,    "A");
            DrawMarker(g, BoatPhysics.PointB, Color.OrangeRed,    "B");
            DrawMarker(g, BoatPhysics.PointC, Color.MediumPurple, "C");

            var path = _manager.BestPath;
            if (path.Count > 1)
                for (int i = 1; i < path.Count; i++)
                {
                    int alpha = (int)(255f * i / path.Count);
                    using (var pp = new Pen(Color.FromArgb(alpha, 100, 200, 100), 1f))
                        g.DrawLine(pp, path[i-1].X * CELL, path[i-1].Y * CELL, path[i].X * CELL, path[i].Y * CELL);
                }

            var trail = _demoState.Trail;
            for (int i = 0; i < trail.Count; i++)
            {
                int alpha = (int)(180f * (1f - (float)i / trail.Count));
                using (var tb = new SolidBrush(Color.FromArgb(alpha, 220, 220, 220)))
                    g.FillEllipse(tb, trail[i].X * CELL - 2, trail[i].Y * CELL - 2, 4, 4);
            }

            DrawBoat(g, _demoState.Position, _demoState.Heading);
        }

        private void DrawArrow(Graphics g, Pen pen, float cx, float cy, float dx, float dy)
        {
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) return;
            float maxLen = CellSize * 0.45f;
            if (len > maxLen) { float s = maxLen / len; dx *= s; dy *= s; }
            g.DrawLine(pen, cx, cy, cx + dx, cy + dy);
            float a = MathF.Atan2(dy, dx);
            float hs = Math.Max(3f, CellSize * 0.13f);
            g.DrawLine(pen, cx+dx, cy+dy, cx+dx - hs*MathF.Cos(a+2.4f), cy+dy - hs*MathF.Sin(a+2.4f));
            g.DrawLine(pen, cx+dx, cy+dy, cx+dx - hs*MathF.Cos(a-2.4f), cy+dy - hs*MathF.Sin(a-2.4f));
        }

        private void DrawMarker(Graphics g, Vec2 pos, Color color, string label)
        {
            int   CELL = CellSize;
            float px   = pos.X * CELL, py = pos.Y * CELL;
            float zr   = BoatPhysics.ArrivalRadius * CELL;
            using (var zb = new SolidBrush(Color.FromArgb(40, color)))
                g.FillEllipse(zb, px - zr, py - zr, zr * 2, zr * 2);
            using (var zp = new Pen(Color.FromArgb(160, color), 1.5f))
                g.DrawEllipse(zp, px - zr, py - zr, zr * 2, zr * 2);
            int dr = Math.Max(5, CELL / 5);
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, px - dr, py - dr, dr * 2, dr * 2);
            using (var f = new Font("Arial", Math.Max(6f, CELL / 5f), FontStyle.Bold))
            using (var tb = new SolidBrush(Color.White))
                g.DrawString(label, f, tb, px - dr / 2f, py - dr / 1.8f);
        }

        private void DrawBoat(Graphics g, Vec2 pos, float heading)
        {
            int   CELL = CellSize;
            float px   = pos.X * CELL, py = pos.Y * CELL;
            float cos  = MathF.Cos(heading), sin = MathF.Sin(heading);
            float nose = Math.Max(6f, CELL * 0.4f);
            float wing = Math.Max(4f, CELL * 0.22f);
            var pts = new PointF[]
            {
                new PointF(px + cos * nose, py + sin * nose),
                new PointF(px + (-sin - cos * 0.6f) * wing, py + (cos - sin * 0.6f) * wing),
                new PointF(px + ( sin - cos * 0.6f) * wing, py + (-cos - sin * 0.6f) * wing)
            };
            using (var b = new SolidBrush(Color.White))  g.FillPolygon(b, pts);
            using (var p = new Pen(Color.Orange, 1f))     g.DrawPolygon(p, pts);
        }

        private void DrawRewardChart(Graphics g)
        {
            var hist = _manager.History;
            int w = 270, h = 110;
            g.Clear(Color.FromArgb(20, 20, 30));
            if (hist.Count < 2) return;
            using (var ap = new Pen(Color.FromArgb(80, 80, 100))) g.DrawLine(ap, 0, h-1, w, h-1);
            float mn = hist.Min(), mx = hist.Max(), rng = mx - mn;
            if (rng < 0.1f) rng = 1f;
            using (var lp = new Pen(Color.LimeGreen, 1.5f))
                for (int i = 1; i < hist.Count; i++)
                {
                    float x0 = (i-1) * (w-1f) / (hist.Count-1), y0 = (h-2) - (hist[i-1]-mn)/rng*(h-3);
                    float x1 =  i    * (w-1f) / (hist.Count-1), y1 = (h-2) - (hist[i]  -mn)/rng*(h-3);
                    g.DrawLine(lp, x0, y0, x1, y1);
                }
            float avgY = (h-2) - (_manager.RecentAvg - mn) / rng * (h-3);
            using (var dp = new Pen(Color.FromArgb(150, 255, 165, 0), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(dp, 0, avgY, w, avgY);
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
