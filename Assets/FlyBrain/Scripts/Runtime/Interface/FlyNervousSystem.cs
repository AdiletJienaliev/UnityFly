using System.Collections.Generic;
using System.Linq;
using FlyBrain.Brain;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// Closes the loop body -> brain -> body.
    /// Senses are written as Poisson rates into identified FlyWire sensory neurons (with a gain set by hunger,
    /// thirst, sleep and light); firing rates of identified descending and motor neurons are read back and decoded
    /// into motor commands. Reflexes of the connectome (feeding, grooming, escape, backing up, freezing, orienting,
    /// flight steering and landing) take precedence over the motivational layer (<see cref="FlyMind"/>), which
    /// supplies what the connectome model lacks: internal needs, navigation, locomotor rhythms and flight control.
    /// The neuron identities come from the FlyWire annotations and the Shiu et al. (2024) paper.
    /// </summary>
    public sealed class FlyNervousSystem : MonoBehaviour
    {
        public BrainService Brain;
        public FlyMotor Motor;
        public FlyRig Rig;

        [Tooltip("Autonomous behavior (needs, navigation, voluntary flight). Off = the fly only reacts through brain reflexes.")]
        public bool Autonomy = true;

        public SensoryState Senses { get; private set; }
        public MotorCommand Command { get; private set; }
        public string Reason { get; private set; } = "";
        /// <summary>True when the current behavior was decided by the connectome, false for the motivational layer / VNC.</summary>
        public bool ReasonFromBrain { get; private set; }
        public string BehaviorName { get; private set; } = "";
        public bool IsWired { get; private set; }
        public FlyPhysiology Physiology { get; } = new FlyPhysiology();
        public FlyMind Mind { get; private set; }
        public BrainDrive Drive { get; private set; }
        public readonly Ethogram Ethogram = new Ethogram();

        // sensory populations
        public NeuronPopulation Sugar, Water, Bitter, JoL, JoR, JoFL, JoFR, EyeBristleL, EyeBristleR;
        public NeuronPopulation LoomL, LoomR, Lc16, ObjectL, ObjectR;
        NeuronPopulation[] _eyeChunksL, _eyeChunksR;
        // descending and motor populations
        public NeuronPopulation Mn9, ProboscisMN, IngestionMN, ADN1, ADN2, GfL, GfR, AlarmDN, Mdn;
        public NeuronPopulation Dna01L, Dna01R, Dna02L, Dna02R, Dna04L, Dna04R, Dnb01L, Dnb01R, P9L, P9R, LandingDN;
        public NeuronPopulation NeckL, NeckR, AntennaMNL, AntennaMNR;

        FlySensors _sensors;
        FlyMode _mode = FlyMode.Walk;
        float _modeTime;
        int _landings;
        float _sinceLanding = 10f;
        public float SugarEaten => Physiology.SugarIntake;

        public void Init(BrainService brain, FlyMotor motor, FlyRig rig)
        {
            Brain = brain;
            Motor = motor;
            Rig = rig;
            _sensors = new FlySensors(rig, motor);
            Mind = new FlyMind(rig, motor, Physiology);
        }

        void Wire()
        {
            var c = Brain.Catalog;
            var L = Side.Left;
            var R = Side.Right;
            var A = Side.Unknown;
            NeuronPopulation P(string key, string label, int[] idx) => Brain.AddPopulation(key, label, idx);

            // senses
            Sugar = P("sugar", "Сахар GRN (Gr5a)", c.Group("paper_sugar_GRN_a").Concat(c.Group("paper_sugar_GRN_b")).ToArray());
            Water = P("water", "Вода GRN (ppk28)", c.Group("paper_water_GRN"));
            Bitter = P("bitter", "Горькое GRN (Gr66a)", c.FindByType(A, "LB1a,LB1d", "LB1b", "LB1c"));
            JoL = P("joL", "JO-C/E антенна L", c.FindByType(L, "JO-C*", "JO-E*"));
            JoR = P("joR", "JO-C/E антенна R", c.FindByType(R, "JO-C*", "JO-E*"));
            JoFL = P("jofL", "JO-F антенна L", c.FindByType(L, "JO-F*"));
            JoFR = P("jofR", "JO-F антенна R", c.FindByType(R, "JO-F*"));
            EyeBristleL = P("eyeL", "Щетинки глаза L", c.FindBySubClass(L, "eye bristle"));
            EyeBristleR = P("eyeR", "Щетинки глаза R", c.FindBySubClass(R, "eye bristle"));
            _eyeChunksL = Chunks(EyeBristleL, FlySensors.EyeBristleChunks);
            _eyeChunksR = Chunks(EyeBristleR, FlySensors.EyeBristleChunks);
            LoomL = P("loomL", "LC4+LPLC2 глаз L", c.FindByType(L, "LC4", "LPLC2"));
            LoomR = P("loomR", "LC4+LPLC2 глаз R", c.FindByType(R, "LC4", "LPLC2"));
            Lc16 = P("lc16", "LC16 (спереди)", c.FindByType(A, "LC16"));
            ObjectL = P("lc10L", "LC10a глаз L", c.FindByType(L, "LC10a"));
            ObjectR = P("lc10R", "LC10a глаз R", c.FindByType(R, "LC10a"));

            // motor side
            Mn9 = P("mn9", "MN9 (хоботок)", c.FindByType(A, "CB0701"));
            ProboscisMN = P("probMN", "Мотонейроны хоботка", c.FindBySubClass(A, "proboscis_motor_neuron"));
            IngestionMN = P("ingMN", "Мотонейроны глотания", c.FindBySubClass(A, "ingestion_motor_neuron"));
            ADN1 = P("adn1", "aDN1 (DNg62)", c.FindByType(A, "DNg62"));
            ADN2 = P("adn2", "aDN2 (DNge078)", c.FindByType(A, "DNge078"));
            GfL = P("gfL", "Giant Fiber L (DNp01)", c.FindByType(L, "DNp01"));
            GfR = P("gfR", "Giant Fiber R (DNp01)", c.FindByType(R, "DNp01"));
            AlarmDN = P("alarm", "DNp02/04/06/11", c.FindByType(A, "DNp02", "DNp04", "DNp06", "DNp11"));
            Mdn = P("mdn", "MDN (moonwalker)", c.FindByType(A, "MDN"));
            Dna01L = P("dna01L", "DNa01 L", c.FindByType(L, "DNa01"));
            Dna01R = P("dna01R", "DNa01 R", c.FindByType(R, "DNa01"));
            Dna02L = P("dna02L", "DNa02 L", c.FindByType(L, "DNa02"));
            Dna02R = P("dna02R", "DNa02 R", c.FindByType(R, "DNa02"));
            Dna04L = P("dna04L", "DNa04 L", c.FindByType(L, "DNa04"));
            Dna04R = P("dna04R", "DNa04 R", c.FindByType(R, "DNa04"));
            Dnb01L = P("dnb01L", "DNb01 L", c.FindByType(L, "DNb01"));
            Dnb01R = P("dnb01R", "DNb01 R", c.FindByType(R, "DNb01"));
            LandingDN = P("landDN", "DNp103/DNg40/DNp70", c.FindByType(A, "DNp103", "DNg40", "DNp70"));
            P9L = P("p9L", "P9 (DNp09) L", c.FindByType(L, "DNp09"));
            P9R = P("p9R", "P9 (DNp09) R", c.FindByType(R, "DNp09"));
            NeckL = P("neckL", "Шейные MN L", c.FindBySubClass(L, "neck_motor_neuron"));
            NeckR = P("neckR", "Шейные MN R", c.FindBySubClass(R, "neck_motor_neuron"));
            AntennaMNL = P("antL", "Антенн. MN L", c.FindBySubClass(L, "antennal_motor_neuron"));
            AntennaMNR = P("antR", "Антенн. MN R", c.FindBySubClass(R, "antennal_motor_neuron"));

            foreach (var p in new[] { GfL, GfR, LandingDN }) p.FastTauMs = 15f;
            foreach (var p in Brain.Populations.Where(p => p.IsEmpty)) Debug.LogWarning("[FlyBrain] empty population " + p.Label);
            IsWired = true;
        }

        static NeuronPopulation[] Chunks(NeuronPopulation p, int count)
        {
            var result = new NeuronPopulation[count];
            int size = (p.Indices.Length + count - 1) / count;
            for (int k = 0; k < count; k++)
                result[k] = new NeuronPopulation(p.Key + k, p.Label, p.Indices.Skip(k * size).Take(size).ToArray());
            return result;
        }

        void Update()
        {
            if (_sensors == null) return;
            if (!IsWired && Brain.IsReady) Wire();
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            var env = FlyEnvironment.Current;

            var s = _sensors.Update(dt);
            Senses = s;
            float bio = env != null && env.Clock != null ? env.Clock.BioTimeScale : 60f;
            float temperature = env != null && env.Clock != null ? env.Clock.Temperature : 22f;
            Physiology.Tick(dt, bio, temperature, Motor.IsAirborne, Mathf.Abs(Motor.Speed));

            // startle from approaching animals even before (or without) a giant fiber escape
            if (s.ThreatLoom > 0.15f) Mind.Startle(s.ThreatLoom * 0.8f, s.ThreatDirection);

            if (IsWired) WriteSenses(s);

            var drive = DecodeBrain();
            if ((Motor.IsAirborne || Motor.IsSettling || _sinceLanding < 0.6f) && s.ThreatLoom < 0.3f) drive.Escape = false;
            Drive = drive;
            var intent = Autonomy ? Mind.Update(dt, s, drive, env) : default;
            var cmd = Compose(dt, drive, intent);
            Command = cmd;
            Motor.Command = cmd;

            // landing events
            _sinceLanding += dt;
            if (Motor.Landings != _landings)
            {
                _landings = Motor.Landings;
                _sinceLanding = 0;
                Mind.OnLanded();
            }

            // eating and drinking empty the liquid and fill the crop
            if (!Motor.IsAirborne && cmd.Pump > 0.1f)
            {
                if (s.TouchedFood != null && s.LabellumInFood && cmd.Mode == FlyMode.Feed && s.TouchedFood.Taste == Taste.Sugar)
                {
                    float amount = dt * 0.06f * cmd.Pump * s.TouchedFood.Concentration;
                    Physiology.Ingest(Taste.Sugar, s.TouchedFood.Concentration, amount);
                    s.TouchedFood.Consume(amount * 0.4f);
                }
                if (s.TouchedWater != null && (s.LabellumInWater || cmd.Mode == FlyMode.Drink))
                {
                    float amount = dt * 0.06f * cmd.Pump;
                    Physiology.Ingest(Taste.Water, 1f, amount);
                    s.TouchedWater.Consume(amount * 0.3f);
                }
            }

            Ethogram.Record(Time.time, BehaviorName, Ethogram.ColorFor(Motor.IsAirborne ? FlyMode.Flight : cmd.Mode));
        }

        void WriteSenses(SensoryState s)
        {
            bool air = Motor.IsAirborne;
            // arousal threshold rises in sleep; vision needs light
            float sleep = Physiology.Asleep ? Mathf.Lerp(0.45f, 0.15f, Physiology.SleepDepth) : 1f;
            float light = Mathf.Clamp01(s.Light * 1.6f);
            Brain.SetSensorRate(Sugar, air ? 0 : s.Sugar * Physiology.SugarGain);
            Brain.SetSensorRate(Water, air ? 0 : s.Water * Physiology.WaterGain);
            Brain.SetSensorRate(Bitter, air ? 0 : s.Bitter);
            Brain.SetSensorRate(JoL, s.JoL * sleep);
            Brain.SetSensorRate(JoR, s.JoR * sleep);
            Brain.SetSensorRate(JoFL, s.JoFL * sleep);
            Brain.SetSensorRate(JoFR, s.JoFR * sleep);
            for (int k = 0; k < FlySensors.EyeBristleChunks; k++)
            {
                Brain.SetSensorRate(_eyeChunksL[k], k < s.DustL ? FlySensors.EyeBristleHz * Mathf.Max(sleep, 0.5f) : 0);
                Brain.SetSensorRate(_eyeChunksR[k], k < s.DustR ? FlySensors.EyeBristleHz * Mathf.Max(sleep, 0.5f) : 0);
            }
            Brain.SetSensorRate(LoomL, s.LoomL * sleep);
            Brain.SetSensorRate(LoomR, s.LoomR * sleep);
            Brain.SetSensorRate(Lc16, s.FrontalLoom * sleep);
            Brain.SetSensorRate(ObjectL, s.ObjectL * sleep * (light > 0 ? 1 : 0));
            Brain.SetSensorRate(ObjectR, s.ObjectR * sleep * (light > 0 ? 1 : 0));
        }

        BrainDrive DecodeBrain()
        {
            var d = new BrainDrive { Ready = IsWired };
            if (!IsWired) return d;
            d.Feed = Mathf.Clamp01((Mn9.Rate - 3f) / 22f);
            d.Groom = Mathf.Clamp01(((ADN1.Rate + ADN2.Rate) * 0.5f - 2f) / 10f);
            d.Backward = Mathf.Clamp01((Mdn.Rate - 2f) / 20f);
            d.Freeze = Mathf.Clamp01((AlarmDN.Rate - 6f) / 45f);
            d.Escape = Mathf.Max(GfL.FastRate, GfR.FastRate) > 20f;
            d.TurnHz = (Dna02R.Rate - Dna02L.Rate) + 0.6f * (Dna01R.Rate - Dna01L.Rate);
            return d;
        }

        MotorCommand Compose(float dt, BrainDrive d, Intent intent)
        {
            var cmd = new MotorCommand
            {
                Mode = FlyMode.Walk,
                HeadYaw = intent.HeadYaw,
                HeadPitch = intent.HeadPitch,
                AntennaL = intent.AntennaL,
                AntennaR = intent.AntennaR,
                FlightDirection = intent.FlightDirection,
                FlightSpeed = intent.FlightSpeed,
                HasLandingTarget = intent.HasLandingTarget,
                LandingPoint = intent.LandingPoint,
                LandingNormal = intent.LandingNormal,
                LandAnywhere = intent.LandAnywhere,
                GroomKind = intent.Groom,
                Sleep = intent.SleepDepth,
            };

            if (!IsWired)
            {
                ApplyIntent(ref cmd, intent);
                Reason = "Мозг загружается… (поведение вне модели мозга)";
                ReasonFromBrain = false;
                BehaviorName = Autonomy ? FlyMind.Name(Mind.Current) : "Ожидание мозга";
                return cmd;
            }

            float mn9 = Mn9.Rate;
            float aDN = (ADN1.Rate + ADN2.Rate) * 0.5f;
            float gf = Mathf.Max(GfL.FastRate, GfR.FastRate);
            float alarm = AlarmDN.Rate;
            float mdn = Mdn.Rate;
            float p9 = (P9L.Rate + P9R.Rate) * 0.5f;

            cmd.Proboscis = Mathf.Max(d.Feed, intent.Probe);
            cmd.Labellum = Mathf.Clamp01((ProboscisMN.Rate - 4f) / 20f);
            cmd.Pump = Mathf.Clamp01((IngestionMN.Rate - 4f) / 25f);
            cmd.Groom = d.Groom;
            cmd.Alarm = d.Freeze;
            float neck = Mathf.Clamp((NeckR.Rate - NeckL.Rate) / 30f, -1, 1);
            if (Mathf.Abs(neck) > 0.05f) cmd.HeadYaw = neck;
            cmd.AntennaL = Mathf.Max(cmd.AntennaL, Mathf.Clamp(AntennaMNL.Rate / 30f, 0, 1));
            cmd.AntennaR = Mathf.Max(cmd.AntennaR, Mathf.Clamp(AntennaMNR.Rate / 30f, 0, 1));
            cmd.ThreatDirection = Senses.ThreatDirection;

            // behavior selection with hysteresis, most urgent first
            _modeTime += dt;
            FlyMode next;
            bool groomWins = d.Groom > d.Feed + 0.35f;
            if (d.Escape) next = FlyMode.Flight;
            else if (d.Backward > (_mode == FlyMode.Backward ? 0.12f : 0.3f)) next = FlyMode.Backward;
            else if (d.Freeze > (_mode == FlyMode.Freeze ? 0.2f : 0.45f)) next = FlyMode.Freeze;
            else if (d.Feed > (_mode == FlyMode.Feed ? 0.1f : 0.3f) && !groomWins) next = FlyMode.Feed;
            else if (d.Groom > (_mode == FlyMode.Groom ? 0.12f : 0.35f)) next = FlyMode.Groom;
            else next = FlyMode.Walk;
            if (next != _mode && (_modeTime > 0.35f || next == FlyMode.Flight || next == FlyMode.Backward))
            {
                _mode = next;
                _modeTime = 0;
            }

            // looming from the surface the fly is landing on excites LPLC2 and the giant fiber too; that input is used
            // for the landing reflex, not for an escape (unless an animal is looming at the same time)
            if ((Motor.IsAirborne || Motor.IsSettling || _sinceLanding < 0.6f) && Senses.ThreatLoom < 0.3f)
            {
                if (_mode == FlyMode.Flight) _mode = FlyMode.Walk;
                d.Escape = false;
            }

            float turn = Mathf.Clamp(d.TurnHz * 0.05f, -5f, 5f);
            ReasonFromBrain = true;
            if (Motor.IsAirborne)
            {
                // flight: steering DNs (DNa02, DNa01, DNa04, DNb01) turn, looming-sensitive DNs extend the legs
                float steerHz = (Dna02R.Rate - Dna02L.Rate) + 0.6f * (Dna01R.Rate - Dna01L.Rate) + 0.8f * (Dna04R.Rate - Dna04L.Rate) + 0.4f * (Dnb01R.Rate - Dnb01L.Rate);
                cmd.SteerTurn = Mathf.Clamp(steerHz * 0.12f, -14f, 14f);
                cmd.LandingReflex = Mathf.Clamp01((LandingDN.FastRate - 15f) / 60f);
                cmd.Mode = FlyMode.Walk;
                if (Mathf.Abs(steerHz) > 25f)
                {
                    Reason = $"DNa02/DNa04 {(steerHz > 0 ? "R" : "L")} → вираж в полёте";
                }
                else if (cmd.LandingReflex > 0.3f)
                {
                    Reason = $"LPLC2 → DNp103/DNg40/DNp70 {LandingDN.FastRate:F0} Гц → ноги к посадке";
                }
                else
                {
                    Reason = Mind.Detail.Length > 0 ? Mind.Detail : "полёт (управление вне модели мозга)";
                    ReasonFromBrain = false;
                }
                BehaviorName = Motor.LastTakeoffWasEscape && Mind.Current == Activity.Flee ? "Бегство в полёте" : "Полёт: " + FlyMind.Name(Mind.Current).ToLowerInvariant();
                if (d.Escape) cmd.Takeoff = true;
                return cmd;
            }

            switch (_mode)
            {
                case FlyMode.Flight:
                    cmd.Takeoff = true;
                    cmd.Mode = FlyMode.Walk;
                    Reason = $"Giant Fiber (DNp01) {gf:F0} Гц → взлёт";
                    BehaviorName = "Взлёт: бегство";
                    if (!Motor.InTakeoff && Motor.TakeoffCooldown <= 0) Mind.OnEscape(Senses.ThreatDirection);
                    break;
                case FlyMode.Backward:
                    cmd.Mode = FlyMode.Backward;
                    cmd.Forward = -14f * d.Backward;
                    cmd.Turn = turn;
                    Reason = $"MDN {mdn:F0} Гц → ходьба назад";
                    BehaviorName = "Пятится назад";
                    break;
                case FlyMode.Freeze:
                    cmd.Mode = FlyMode.Freeze;
                    Reason = $"DNp02/04/06/11 {alarm:F0} Гц → замирание, крылья вверх";
                    BehaviorName = "Тревога: замирание";
                    break;
                case FlyMode.Feed:
                    cmd.Mode = FlyMode.Feed;
                    Reason = $"MN9 {mn9:F0} Гц → хоботок выдвинут, глотание {IngestionMN.Rate:F0} Гц";
                    BehaviorName = "Ест";
                    break;
                case FlyMode.Groom:
                    cmd.Mode = FlyMode.Groom;
                    cmd.GroomKind = Senses.DustL + Senses.DustR > 0 ? GroomKind.Eyes : GroomKind.Antennae;
                    Reason = $"aDN1/aDN2 {aDN:F0} Гц → груминг {(cmd.GroomKind == GroomKind.Eyes ? "глаз" : "антенн")}";
                    BehaviorName = cmd.GroomKind == GroomKind.Eyes ? "Чистит глаза" : "Чистит антенны";
                    break;
                default:
                {
                    ApplyIntent(ref cmd, intent);
                    float brainTurn = turn;
                    cmd.Turn += cmd.Forward > 0.5f || cmd.Mode == FlyMode.Walk ? brainTurn : 0;
                    cmd.Forward += Mathf.Clamp(p9 * 0.12f, 0, 12f);
                    BehaviorName = Autonomy ? FlyMind.Name(Mind.Current) : "Покой";
                    bool turning = cmd.Mode == FlyMode.Walk && (cmd.Forward > 0.5f || Mathf.Abs(brainTurn) > 0.8f);
                    if (Mathf.Abs(brainTurn) > 0.3f && turning)
                    {
                        Reason = $"DNa02 L {Dna02L.Rate:F0} / R {Dna02R.Rate:F0} Гц → поворот {(brainTurn > 0 ? "вправо" : "влево")}";
                        if (cmd.Mode == FlyMode.Rest && Mathf.Abs(brainTurn) > 0.8f) cmd.Mode = FlyMode.Walk; // orienting turn in place
                    }
                    else if (p9 > 3f) Reason = $"P9 (DNp09) {p9:F0} Гц → ходьба вперёд";
                    else if (intent.Probe > 0.2f && d.Feed < 0.1f)
                    {
                        Reason = "пробует поверхность хоботком (вне модели мозга)";
                        ReasonFromBrain = false;
                    }
                    else
                    {
                        Reason = Autonomy ? (Mind.Detail.Length > 0 ? Mind.Detail + " (вне модели мозга)" : "мотивация и ритмы вне модели мозга") : "Покой";
                        ReasonFromBrain = false;
                    }
                    break;
                }
            }
            return cmd;
        }

        void ApplyIntent(ref MotorCommand cmd, Intent intent)
        {
            cmd.Mode = intent.Posture;
            cmd.Forward = intent.Forward;
            cmd.Turn = intent.Turn;
            cmd.FlyRequest = intent.Fly;
            cmd.GroomKind = intent.Groom;
            cmd.Oviposit = intent.Oviposit;
            cmd.Alarm = Mathf.Max(cmd.Alarm, intent.WingFlick);
            if (intent.Posture == FlyMode.Groom) cmd.Groom = Mathf.Max(cmd.Groom, 0.6f);
            if (intent.Posture == FlyMode.Drink)
            {
                // water GRNs do not reach MN9 in this model: drinking is a VNC-level proboscis program
                cmd.Proboscis = 1f;
                cmd.Pump = 0.8f;
                cmd.Labellum = 0.6f;
            }
            if (intent.Posture == FlyMode.Walk && intent.Forward < 0.3f && Mathf.Abs(intent.Turn) < 0.2f && !intent.Fly) cmd.Mode = FlyMode.Walk;
            if (!IsWired) cmd.Proboscis = intent.Probe;
        }
    }

    /// <summary>Recent behavior history for the HUD (behavior name segments over world time).</summary>
    public sealed class Ethogram
    {
        public struct Segment
        {
            public string Name;
            public Color Color;
            public float Start, End;
        }

        public readonly List<Segment> Segments = new List<Segment>();
        public float Window = 240f;

        public static Color ColorFor(FlyMode mode) => mode switch
        {
            FlyMode.Feed => new Color(1f, 0.72f, 0.2f),
            FlyMode.Drink => new Color(0.35f, 0.7f, 1f),
            FlyMode.Groom => new Color(0.75f, 0.5f, 1f),
            FlyMode.Freeze => new Color(1f, 0.3f, 0.3f),
            FlyMode.Backward => new Color(1f, 0.45f, 0.6f),
            FlyMode.Flight => new Color(0.4f, 1f, 0.9f),
            FlyMode.Rest => new Color(0.45f, 0.5f, 0.55f),
            FlyMode.Sleep => new Color(0.2f, 0.25f, 0.5f),
            _ => new Color(0.45f, 0.85f, 0.4f),
        };

        public void Record(float time, string name, Color color)
        {
            if (Segments.Count > 0)
            {
                var last = Segments[Segments.Count - 1];
                if (last.Name == name)
                {
                    last.End = time;
                    Segments[Segments.Count - 1] = last;
                    return;
                }
                last.End = time;
                Segments[Segments.Count - 1] = last;
            }
            Segments.Add(new Segment { Name = name, Color = color, Start = time, End = time });
            while (Segments.Count > 2 && Segments[0].End < time - Window) Segments.RemoveAt(0);
        }
    }
}
