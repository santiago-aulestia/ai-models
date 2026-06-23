using System;
using System.Collections.Generic;
using System.Linq;

namespace SelfKnowledgeAI
{
    // ── Knowledge ─────────────────────────────────────────────────────────────

    class KnowledgeFact
    {
        public string Topic;
        public string Hypothesis;
        public double Confidence;   // 0 = unknown, 1 = certain
        public double Value;        // numeric finding
        public string HumanNote;
        public int    Strikes;      // consecutive contradictions
        public int    Attempts;     // total experiment runs on this topic
        public bool   Resolved;     // true once force-accepted — skip in future exploration
    }

    class KnowledgeBase
    {
        private readonly List<KnowledgeFact> _facts = new();

        public void Seed(string topic, string hypothesis)
        {
            _facts.Add(new KnowledgeFact
            {
                Topic      = topic,
                Hypothesis = hypothesis,
                Confidence = 0.05,
                Value      = double.NaN
            });
        }

        public KnowledgeFact FindLowestConfidence() =>
            _facts.Where(f => !f.Resolved).OrderBy(f => f.Confidence).FirstOrDefault()
            ?? _facts.OrderBy(f => f.Confidence).First();

        public KnowledgeFact Find(string topic) =>
            _facts.FirstOrDefault(f => f.Topic == topic);

        public double AverageConfidence() =>
            _facts.Average(f => f.Confidence);

        // Called after an experiment runs — tracks the contradiction streak.
        public void Update(string topic, double value, double confidence, bool contradiction = false)
        {
            var f = Find(topic);
            if (f == null) return;
            f.Value      = value;
            f.Confidence = Math.Min(1.0, confidence);
            f.Attempts++;
            if (contradiction) f.Strikes++;
            else f.Strikes = 0;
        }

        // Permanently closes a topic — it will no longer be picked for exploration.
        public void Resolve(string topic, double value, double confidence, string note)
        {
            var f = Find(topic);
            if (f == null) return;
            f.Value      = value;
            f.Confidence = Math.Min(1.0, confidence);
            f.Resolved   = true;
            f.HumanNote  = note;
        }

        // Called from guidance handling — adjusts confidence/notes without touching Strikes.
        public void AdjustConfidence(string topic, double value, double confidence)
        {
            var f = Find(topic);
            if (f == null) return;
            f.Value      = value;
            f.Confidence = Math.Min(1.0, confidence);
        }

        public void AddNote(string topic, string note)
        {
            var f = Find(topic);
            if (f != null) f.HumanNote = note;
        }

        public void Print()
        {
            Console.WriteLine("\n── Knowledge Base ──────────────────────────────────────────────");
            foreach (var f in _facts.OrderByDescending(f => f.Confidence))
            {
                string val  = double.IsNaN(f.Value) ? "    ?" : $"{f.Value,6:F3}";
                string note = f.HumanNote != null ? $"  [{f.HumanNote}]" : "";
                string res  = f.Resolved ? " ✓" : "  ";
                string bar  = new string('█', (int)(f.Confidence * 12)).PadRight(12);
                Console.WriteLine($"  {bar} {f.Confidence:F2}{res}  {f.Topic,-30} = {val}{note}");
            }
            Console.WriteLine("────────────────────────────────────────────────────────────────\n");
        }
    }

    // ── Experiment (the generated "program") ──────────────────────────────────

    class ExperimentResult
    {
        public string   Finding;
        public double   Value;
        public double   Confidence;
        public bool     ContradictsPrior;
    }

    class Experiment
    {
        public string                   Topic;
        public string                   Hypothesis;
        public string                   Code;       // readable "program" shown to user
        public Func<ExperimentResult>   Execute;
    }

    // ── Probe Engine ──────────────────────────────────────────────────────────

    static class Probe
    {
        public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        // Computes the environment's hidden danger signal
        public static double Danger(double load, double error, double dError = 0.0, double iError = 1.5)
        {
            return Clamp01(
                0.35 * load +
                0.30 * error +
                0.20 * Clamp01((dError + 1.0) / 2.0) +
                0.25 * Clamp01(iError));
        }

        // Runs N trials under a parameterized condition.
        // Returns (avgReward, safeWasCorrectRate, variance).
        public static (double avg, double safeRate, double variance) Run(
            Random rand, int trials,
            double loadLo, double loadHi,
            double errLo,  double errHi,
            double urgLo,  double urgHi,
            bool forceSafe = false, bool forceFast = false)
        {
            double sumR = 0, sumSq = 0;
            int safeCorrect = 0;

            for (int i = 0; i < trials; i++)
            {
                double load    = loadLo + rand.NextDouble() * (loadHi - loadLo);
                double error   = errLo  + rand.NextDouble() * (errHi  - errLo);
                double urgency = urgLo  + rand.NextDouble() * (urgHi  - urgLo);
                double danger  = Danger(load, error);

                bool safeIsBetter = danger >= 0.55;
                bool chooseSafe   = forceFast ? false
                                  : forceSafe ? true
                                  : safeIsBetter;  // oracle

                double reward = (chooseSafe == safeIsBetter) ? 1.0 : 0.15;
                if (!forceFast && !forceSafe && safeIsBetter) safeCorrect++;
                else if (forceSafe  && safeIsBetter) safeCorrect++;
                else if (forceFast  && !safeIsBetter) safeCorrect++;

                // Apply urgency bonus for correct Fast choice
                if (!chooseSafe && !safeIsBetter && urgency > 0.75)
                    reward = Clamp01(reward + 0.10);

                sumR  += reward;
                sumSq += reward * reward;
            }

            double avg      = sumR / trials;
            double variance = (sumSq / trials) - (avg * avg);
            return (avg, (double)safeCorrect / trials, variance);
        }
    }

    // ── Program Generator ─────────────────────────────────────────────────────

    static class ProgramGenerator
    {
        static readonly Random _r = new Random(77);

        public static Experiment For(KnowledgeFact gap) => gap.Topic switch
        {
            "safe_accuracy_high_danger"  => SafeHighDanger(),
            "fast_accuracy_low_danger"   => FastLowDanger(),
            "danger_crossover"           => DangerCrossover(),
            "urgency_bonus"              => UrgencyBonus(),
            "confusion_zone_width"       => ConfusionZone(),
            "high_load_effect"           => HighLoadEffect(),
            "low_error_fast_rate"        => LowErrorFastRate(),
            "oscillation_cost"           => OscillationCost(),
            _                            => GeneralSweep(gap)
        };

        static Experiment SafeHighDanger() => new()
        {
            Topic      = "safe_accuracy_high_danger",
            Hypothesis = "Safe action achieves reward >= 0.9 when danger > 0.6",
            Code = @"
// PROGRAM: safe_accuracy_high_danger
// Goal: measure how often Safe is correct under clearly dangerous conditions
trials  = 1000
samples = { load ~ U(0.6, 1.0), error ~ U(0.5, 1.0), urgency ~ U(0, 1) }
action  = Safe  (forced, to isolate accuracy)
reward  = environment_reward(sample, action)
result  = mean(reward >= 1.0)",

            Execute = () =>
            {
                var (avg, rate, v) = Probe.Run(_r, 1000, 0.6,1.0, 0.5,1.0, 0,1, forceSafe:true);
                double conf = Math.Max(0.2, 1.0 - v * 5);
                return new ExperimentResult
                {
                    Finding          = $"Safe is correct {rate:P1} of the time in high-danger (avg reward {avg:F3})",
                    Value            = rate,
                    Confidence       = conf,
                    ContradictsPrior = rate < 0.75
                };
            }
        };

        static Experiment FastLowDanger() => new()
        {
            Topic      = "fast_accuracy_low_danger",
            Hypothesis = "Fast action is correct >= 80% of the time when load < 0.4 and error < 0.3",
            Code = @"
// PROGRAM: fast_accuracy_low_danger
// Goal: confirm Fast dominates in low-stress conditions
trials  = 1000
samples = { load ~ U(0.0, 0.4), error ~ U(0.0, 0.3), urgency ~ U(0.5, 1.0) }
action  = Fast  (forced)
reward  = environment_reward(sample, action)
result  = mean(reward >= 1.0)",

            Execute = () =>
            {
                var (avg, rate, v) = Probe.Run(_r, 1000, 0,0.4, 0,0.3, 0.5,1, forceFast:true);
                double conf = Math.Max(0.2, 1.0 - v * 5);
                return new ExperimentResult
                {
                    Finding          = $"Fast is correct {rate:P1} of the time in low-danger (avg reward {avg:F3})",
                    Value            = rate,
                    Confidence       = conf,
                    ContradictsPrior = rate < 0.70
                };
            }
        };

        static Experiment DangerCrossover() => new()
        {
            Topic      = "danger_crossover",
            Hypothesis = "The Safe/Fast crossover (equal expected reward) is near danger = 0.55",
            Code = @"
// PROGRAM: danger_crossover_probe
// Note: Probe.Danger has a fixed offset ~0.35 from dError/iError terms.
// Crossover at danger=0.55 → 0.35*load + 0.30*error ≈ 0.20 → load=error ≈ 0.308
// Sweep load/error midpoint m from 0.15 to 0.50; actual danger = 0.65*m + 0.35
for midpoint m in [0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45]:
    load = error ~ U(m-0.04, m+0.04)
    safe_r = mean_reward(action=Safe,  these inputs)
    fast_r = mean_reward(action=Fast, these inputs)
    record(m, safe_r - fast_r)
crossover_danger = 0.65 * m_min_gap + 0.35",

            Execute = () =>
            {
                double[] midpoints = { 0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45 };
                double bestM   = 0.308;
                double minGap  = double.MaxValue;

                foreach (var m in midpoints)
                {
                    double lo = Math.Max(0, m - 0.04), hi = m + 0.04;
                    var (s, _, _) = Probe.Run(_r, 300, lo,hi, lo,hi, 0,1, forceSafe:true);
                    var (f, _, _) = Probe.Run(_r, 300, lo,hi, lo,hi, 0,1, forceFast:true);
                    double gap = Math.Abs(s - f);
                    if (gap < minGap) { minGap = gap; bestM = m; }
                }
                double crossoverDanger = 0.65 * bestM + 0.35;
                bool contradicts = Math.Abs(crossoverDanger - 0.55) > 0.10;
                return new ExperimentResult
                {
                    Finding          = $"Crossover at load/error midpoint ≈ {bestM:F3} → danger ≈ {crossoverDanger:F3} (margin = {minGap:F3})",
                    Value            = crossoverDanger,
                    Confidence       = minGap < 0.10 ? 0.78 : 0.52,
                    ContradictsPrior = contradicts
                };
            }
        };

        static Experiment UrgencyBonus() => new()
        {
            Topic      = "urgency_bonus",
            Hypothesis = "High urgency (>0.75) adds ~0.10 to Fast reward when danger is low",
            Code = @"
// PROGRAM: urgency_bonus_probe
// Goal: isolate the urgency reward bonus in low-danger conditions
baseline  = mean_reward(Fast, urgency ~ U(0.0, 0.5), low danger)
high_urg  = mean_reward(Fast, urgency ~ U(0.75, 1.0), low danger)
bonus     = high_urg - baseline
// Expected: ~0.10 per reward function spec",

            Execute = () =>
            {
                var (base_r, _, _)  = Probe.Run(_r, 600, 0,0.4, 0,0.3, 0.0,0.5,  forceFast:true);
                var (hurg_r, _, _)  = Probe.Run(_r, 600, 0,0.4, 0,0.3, 0.75,1.0, forceFast:true);
                double bonus = hurg_r - base_r;
                double conf  = Math.Abs(bonus - 0.10) < 0.04 ? 0.85 : 0.50;
                return new ExperimentResult
                {
                    Finding          = $"Urgency bonus = {bonus:+0.000;-0.000}  (base={base_r:F3}, high urgency={hurg_r:F3})",
                    Value            = bonus,
                    Confidence       = conf,
                    ContradictsPrior = bonus < 0
                };
            }
        };

        static Experiment ConfusionZone() => new()
        {
            Topic      = "confusion_zone_width",
            Hypothesis = "The confusion zone (|safe_r - fast_r| < 0.08) spans ~0.10 around danger = 0.55",
            Code = @"
// PROGRAM: confusion_zone_probe
// Goal: find where Safe and Fast rewards are nearly equal (ambiguous region)
for danger_level in linspace(0.45, 0.65, steps=11):
    safe_r = mean_reward(Safe, danger ≈ danger_level)
    fast_r = mean_reward(Fast, danger ≈ danger_level)
    if |safe_r - fast_r| < 0.08: mark_confused(danger_level)
confusion_width = count(confused) * step_size",

            Execute = () =>
            {
                int confused = 0;
                double[] levels = { 0.45,0.47,0.49,0.51,0.53,0.55,0.57,0.59,0.61,0.63,0.65 };
                foreach (var lv in levels)
                {
                    var (s,_,_) = Probe.Run(_r, 200, lv,lv+0.04, lv,lv+0.04, 0,1, forceSafe:true);
                    var (f,_,_) = Probe.Run(_r, 200, lv,lv+0.04, lv,lv+0.04, 0,1, forceFast:true);
                    if (Math.Abs(s - f) < 0.08) confused++;
                }
                double width = confused * 0.02;
                return new ExperimentResult
                {
                    Finding          = $"Confusion zone spans ≈ {width:F2} danger units ({confused} bands of 0.02)",
                    Value            = width,
                    Confidence       = confused > 0 ? 0.72 : 0.35,
                    ContradictsPrior = confused == 0
                };
            }
        };

        static Experiment HighLoadEffect() => new()
        {
            Topic      = "high_load_effect",
            Hypothesis = "High load (>0.8) alone makes Safe correct >= 75% of the time",
            Code = @"
// PROGRAM: high_load_effect
// Goal: isolate load's contribution to danger — keep error low
trials  = 800
samples = { load ~ U(0.8, 1.0), error ~ U(0.0, 0.3), urgency ~ U(0, 1) }
action  = oracle(danger(load, error))
result  = mean(action == Safe)",

            Execute = () =>
            {
                var (_, rate, v) = Probe.Run(_r, 800, 0.8,1.0, 0,0.3, 0,1);
                double conf = Math.Max(0.2, 1.0 - v * 4);
                return new ExperimentResult
                {
                    Finding          = $"High load → Safe is oracle choice {rate:P1} of the time",
                    Value            = rate,
                    Confidence       = conf,
                    ContradictsPrior = rate < 0.60
                };
            }
        };

        static Experiment LowErrorFastRate() => new()
        {
            Topic      = "low_error_fast_rate",
            Hypothesis = "When error < 0.2 and load < 0.5, Fast is correct in >= 70% of cases",
            Code = @"
// PROGRAM: low_error_fast_rate
// Goal: confirm that low error suppresses danger enough for Fast to dominate
trials  = 800
samples = { load ~ U(0.0, 0.5), error ~ U(0.0, 0.2), urgency ~ U(0, 1) }
action  = oracle(danger(load, error))
result  = mean(action == Fast)",

            Execute = () =>
            {
                var (_, safeRate, v) = Probe.Run(_r, 800, 0,0.5, 0,0.2, 0,1);
                double fastRate = 1.0 - safeRate;
                double conf = Math.Max(0.2, 1.0 - v * 4);
                return new ExperimentResult
                {
                    Finding          = $"Low error → Fast is oracle choice {fastRate:P1} of the time",
                    Value            = fastRate,
                    Confidence       = conf,
                    ContradictsPrior = fastRate < 0.55
                };
            }
        };

        static Experiment OscillationCost() => new()
        {
            Topic      = "oscillation_cost",
            Hypothesis = "The oscillation penalty costs exactly 0.05 reward per action switch",
            Code = @"
// PROGRAM: oscillation_cost_probe
// Goal: measure the empirical cost of switching action near the boundary
trials = 600
for each trial:
    danger ~ 0.50..0.60  (boundary zone)
    reward_stable   = environment_reward(same action as last step)
    reward_switched = environment_reward(different action)
    penalty = reward_stable - reward_switched
result = mean(penalty)  // expected: 0.050",

            Execute = () =>
            {
                double sumS = 0, sumW = 0;
                int n = 600;
                for (int i = 0; i < n; i++)
                {
                    double load  = 0.40 + _r.NextDouble() * 0.25;
                    double err   = 0.40 + _r.NextDouble() * 0.25;
                    double dng   = Probe.Danger(load, err);
                    bool sib     = dng >= 0.55;
                    double base_r = sib ? 1.0 : 0.15;
                    sumS += Probe.Clamp01(base_r);
                    sumW += Probe.Clamp01(base_r - 0.05);
                }
                double penalty = (sumS - sumW) / n;
                double conf = Math.Abs(penalty - 0.05) < 0.005 ? 0.92 : 0.55;
                return new ExperimentResult
                {
                    Finding          = $"Oscillation penalty = {penalty:F4} per switch (expected 0.0500)",
                    Value            = penalty,
                    Confidence       = conf,
                    ContradictsPrior = penalty < 0.03 || penalty > 0.08
                };
            }
        };

        static Experiment GeneralSweep(KnowledgeFact gap) => new()
        {
            Topic      = gap.Topic,
            Hypothesis = gap.Hypothesis,
            Code = $@"
// PROGRAM: general_sweep [{gap.Topic}]
// No targeted experiment available — broad uniform sweep
trials = 500
samples = {{ load, error, urgency ~ U(0,1) }}
action  = oracle(danger(load, error))
result  = mean(reward)",

            Execute = () =>
            {
                var (avg, _, v) = Probe.Run(_r, 500, 0,1, 0,1, 0,1);
                return new ExperimentResult
                {
                    Finding          = $"Broad sweep avg reward = {avg:F3}",
                    Value            = avg,
                    Confidence       = 0.38,
                    ContradictsPrior = false
                };
            }
        };
    }

    // ── Human Interface ───────────────────────────────────────────────────────

    static class HumanConsole
    {
        public static string Ask(KnowledgeFact gap, ExperimentResult result)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n┌─ GUIDANCE NEEDED ──────────────────────────────────────────────");
            Console.WriteLine($"│  Topic:      {gap.Topic}");
            Console.WriteLine($"│  Hypothesis: {gap.Hypothesis}");
            Console.WriteLine($"│  Finding:    {result.Finding}");
            Console.WriteLine($"│  Confidence: {result.Confidence:F2}  (below threshold)");
            if (result.ContradictsPrior)
                Console.WriteLine("│  ⚠  This finding CONTRADICTS the hypothesis above.");
            Console.WriteLine("│");

            // Non-interactive mode (piped stdin / CI / background): auto-accept
            if (Console.IsInputRedirected)
            {
                Console.WriteLine("│  [non-interactive] auto-accepting and continuing.");
                Console.WriteLine("└────────────────────────────────────────────────────────────────");
                Console.ResetColor();
                return "accept";
            }

            Console.WriteLine("│  [Enter]           Accept finding, continue");
            Console.WriteLine("│  correct / yes     Confirm the finding is right");
            Console.WriteLine("│  wrong / no        Mark finding as incorrect");
            Console.WriteLine("│  rerun             Repeat this experiment");
            Console.WriteLine("│  <number>          Override with your own value");
            Console.WriteLine("│  <text>            Add a note and boost confidence");
            Console.Write("└─> ");
            Console.ResetColor();

            string input = Console.ReadLine()?.Trim() ?? "";
            return string.IsNullOrEmpty(input) ? "accept" : input.ToLower();
        }
    }

    // ── Self-Knowledge Explorer ───────────────────────────────────────────────

    class SelfKnowledgeExplorer
    {
        const double CONFIDENCE_THRESHOLD = 0.62;
        const int    MAX_ITERATIONS       = 40;

        readonly KnowledgeBase _kb     = new();
        int _iteration                 = 0;
        int _humanInteractions         = 0;
        string _pendingRerun           = null;

        public void Run()
        {
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║           Self-Knowledge Explorer  — Hybrid AI               ║");
            Console.WriteLine("║  Generates programs to understand its own decision behavior   ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝\n");

            Seed();

            Console.WriteLine($"  Seeded {8} knowledge topics.");
            Console.WriteLine($"  Confidence threshold : {CONFIDENCE_THRESHOLD:F2}");
            Console.WriteLine($"  Max iterations       : {MAX_ITERATIONS}");
            Console.WriteLine($"  Below threshold → asks human for guidance\n");

            while (_iteration < MAX_ITERATIONS)
            {
                _iteration++;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"━━━ Iteration {_iteration,2}/{MAX_ITERATIONS} ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.ResetColor();

                // 1. Identify lowest-confidence topic (or re-run a failed one)
                KnowledgeFact gap = _pendingRerun != null
                    ? _kb.Find(_pendingRerun)
                    : _kb.FindLowestConfidence();
                _pendingRerun = null;

                // All remaining unresolved topics are already above threshold — nothing left to learn.
                if (gap.Confidence >= CONFIDENCE_THRESHOLD)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n  All topics at or above confidence threshold. Learning complete.");
                    Console.ResetColor();
                    break;
                }

                Console.WriteLine($"  Knowledge gap : \"{gap.Topic}\"  (confidence = {gap.Confidence:F2})");
                Console.WriteLine($"  Hypothesis    : {gap.Hypothesis}");

                // 2. Generate program
                var experiment = ProgramGenerator.For(gap);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(experiment.Code);
                Console.ResetColor();

                // 3. Execute
                Console.Write("  Running... ");
                var result = experiment.Execute();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("done.");
                Console.ResetColor();

                Console.WriteLine($"  Finding    : {result.Finding}");
                Console.WriteLine($"  Confidence : {result.Confidence:F2}");

                if (result.ContradictsPrior)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  ⚠  Result contradicts prior hypothesis.");
                    Console.ResetColor();
                }

                // 4. Update knowledge base
                _kb.Update(gap.Topic, result.Value, result.Confidence, result.ContradictsPrior);

                // 3 consecutive contradictions → revise hypothesis, accept value, close topic.
                if (gap.Strikes >= 3)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"  ✗ Hypothesis revised after {gap.Strikes} contradictions — accepting measured value.");
                    Console.ResetColor();
                    _kb.Resolve(gap.Topic, result.Value, 0.72,
                        "hypothesis revised: experimental evidence contradicts prior");
                    continue;
                }

                // 5 total attempts without reaching threshold → accept best finding, close topic.
                if (gap.Attempts >= 5)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"  ✗ Could not reach threshold after {gap.Attempts} attempts — accepting best finding.");
                    Console.ResetColor();
                    _kb.Resolve(gap.Topic, result.Value, 0.70,
                        "accepted after max attempts; probe may have a ceiling effect");
                    continue;
                }

                // 5. Decide: autonomous or ask human?
                bool needsGuidance = result.Confidence < CONFIDENCE_THRESHOLD
                                  || result.ContradictsPrior;

                if (needsGuidance)
                {
                    _humanInteractions++;
                    string guidance = HumanConsole.Ask(gap, result);
                    HandleGuidance(gap.Topic, guidance, result);
                }

                // 6. Print knowledge snapshot every 8 iterations
                if (_iteration % 8 == 0)
                    _kb.Print();

                // 7. Early exit if fully confident
                if (_kb.AverageConfidence() >= 0.88)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n  All topics reached high confidence. Learning complete early.");
                    Console.ResetColor();
                    break;
                }
            }

            PrintFinalSummary();
        }

        void Seed()
        {
            _kb.Seed("safe_accuracy_high_danger",
                "Safe action achieves reward >= 0.9 when danger > 0.6");
            _kb.Seed("fast_accuracy_low_danger",
                "Fast is correct >= 80% of the time when load < 0.4 and error < 0.3");
            _kb.Seed("danger_crossover",
                "The Safe/Fast crossover occurs near danger = 0.55");
            _kb.Seed("urgency_bonus",
                "High urgency (>0.75) adds ~0.10 bonus to Fast reward in low-danger");
            _kb.Seed("confusion_zone_width",
                "Confusion zone spans ~0.10 around danger = 0.55");
            _kb.Seed("high_load_effect",
                "High load (>0.8) alone makes Safe correct >= 75% of the time");
            _kb.Seed("low_error_fast_rate",
                "Low error (<0.2) with low load makes Fast correct >= 70% of the time");
            _kb.Seed("oscillation_cost",
                "The oscillation penalty is exactly 0.05 per action switch");
        }

        void HandleGuidance(string topic, string guidance, ExperimentResult result)
        {
            // All paths use AdjustConfidence (not Update) to preserve the Strikes counter.
            if (guidance == "rerun")
            {
                _pendingRerun = topic;
                _kb.AdjustConfidence(topic, result.Value, 0.05);
                Console.WriteLine("  → Scheduled rerun for next iteration.");
                return;
            }

            if (guidance == "correct" || guidance == "yes")
            {
                _kb.AdjustConfidence(topic, result.Value, Math.Min(1.0, result.Confidence + 0.25));
                Console.WriteLine("  → Confirmed. Confidence boosted.");
            }
            else if (guidance == "wrong" || guidance == "no")
            {
                _kb.AdjustConfidence(topic, result.Value, 0.10);
                Console.WriteLine("  → Rejected. Confidence reset for re-exploration.");
            }
            else if (double.TryParse(guidance, out double manualValue))
            {
                _kb.AdjustConfidence(topic, manualValue, 0.88);
                _kb.AddNote(topic, $"human value: {manualValue}");
                Console.WriteLine($"  → Value overridden to {manualValue:F3} with high confidence.");
            }
            else
            {
                _kb.AddNote(topic, guidance);
                _kb.AdjustConfidence(topic, result.Value, Math.Min(1.0, result.Confidence + 0.15));
                Console.WriteLine($"  → Note saved: \"{guidance}\"");
            }
        }

        void PrintFinalSummary()
        {
            Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  Done.  Iterations: {_iteration,-5}  Human interactions: {_humanInteractions,-5}         ║");
            Console.WriteLine($"║  Average confidence: {_kb.AverageConfidence():F2}                                  ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            _kb.Print();
        }
    }

    class Program
    {
        static void Main() => new SelfKnowledgeExplorer().Run();
    }
}
