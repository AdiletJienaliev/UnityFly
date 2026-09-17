using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FlyBrain.Brain;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FlyBrain
{
    /// <summary>
    /// Owns the whole-brain LIF simulation (FlyWire 783, 138 639 neurons) and runs it on a background
    /// thread in lock-step with world time: the world advances, the brain catches up. If the brain
    /// cannot keep up, world time is slowed down so that the closed loop body-brain stays consistent.
    /// </summary>
    public sealed unsafe class BrainService : MonoBehaviour
    {
        public const string DataFolder = "FlyBrain";

        public LifBrain Brain { get; private set; }
        public Connectome Connectome { get; private set; }
        public NeuronCatalog Catalog { get; private set; }
        public bool IsReady { get; private set; }
        public string Status { get; private set; } = "Загрузка коннектома…";
        public string Error { get; private set; }

        [Tooltip("Slow down world time when the brain cannot run in real time")]
        public bool AdaptiveTimeScale = true;
        [Tooltip("Desired world time scale (1 = real time)")]
        public float TargetTimeScale = 1f;
        public bool Paused;

        /// <summary>Simulated brain milliseconds per wall-clock millisecond while computing flat out.</summary>
        public float Capacity { get; private set; }
        public float SpikesPerSecond { get; private set; }
        public double BrainTimeMs => Brain?.TimeMs ?? 0;
        public double LagMs => _targetMs - BrainTimeMs;

        readonly List<NeuronPopulation> _populations = new List<NeuronPopulation>();
        float[] _sensorRate, _optoRate, _appliedRate;
        bool[] _optoSilenced, _appliedSilenced;
        readonly HashSet<int> _dirty = new HashSet<int>();

        Thread _thread;
        volatile bool _running;
        volatile bool _destroyed;
        double _targetMs;
        long _lastSpikeTotal;
        double _lastSpikeMs;

        public IReadOnlyList<NeuronPopulation> Populations => _populations;

        // ------------------------------------------------------------------ loading

        public void BeginLoad(int threads = 0)
        {
            if (threads <= 0) threads = Mathf.Clamp(SystemInfo.processorCount - 3, 2, 12);
            string dir = Path.Combine(Application.streamingAssetsPath, DataFolder);
            Task.Run(() =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    Status = "Загрузка коннектома FlyWire 783…";
                    var con = Connectome.Load(Path.Combine(dir, "connectome_783.bin"));
                    Status = "Загрузка аннотаций нейронов…";
                    var cat = NeuronCatalog.Load(Path.Combine(dir, "neurons_783.bin"), Path.Combine(dir, "groups_783.tsv"));
                    Status = "Создание модели LIF…";
                    var brain = new LifBrain(con, new LifParameters(), threads, (uint)Environment.TickCount);
                    Status = "Компиляция Burst…";
                    brain.RunSteps(brain.DelaySteps); // triggers Burst compilation of the kernels
                    if (_destroyed)
                    {
                        // play mode was stopped while loading: release the ~200 MB right away
                        brain.Dispose();
                        con.Dispose();
                        return;
                    }
                    Connectome = con;
                    Catalog = cat;
                    _sensorRate = new float[con.NeuronCount];
                    _optoRate = new float[con.NeuronCount];
                    _appliedRate = new float[con.NeuronCount];
                    _optoSilenced = new bool[con.NeuronCount];
                    _appliedSilenced = new bool[con.NeuronCount];
                    Brain = brain;
                    Debug.Log($"[FlyBrain] brain ready in {sw.ElapsedMilliseconds} ms: {con.NeuronCount} neurons, {con.ConnectionCount} connections, {threads} threads");
                    Status = "Мозг работает";
                    IsReady = true;
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    Status = "Ошибка загрузки мозга: " + e.Message;
                    Debug.LogException(e);
                }
            });
        }

        void Update()
        {
            if (!IsReady) return;
            if (_thread == null) StartThread();

            if (!Paused) _targetMs += Time.deltaTime * 1000.0;

            // keep the world in step with the brain
            double lag = LagMs;
            float desired = Paused ? Time.timeScale : TargetTimeScale;
            if (AdaptiveTimeScale && !Paused)
            {
                if (Capacity > 0) desired = Mathf.Min(desired, Mathf.Max(0.05f, Capacity * 0.9f));
                if (lag > 60) desired *= Mathf.Clamp01(1f - (float)(lag - 60) / 400f) * 0.9f + 0.1f;
            }
            if (!Paused) Time.timeScale = Mathf.Clamp(Mathf.MoveTowards(Time.timeScale, desired, Time.unscaledDeltaTime * 1.5f), 0.02f, 4f);
            if (lag > 500) _targetMs = BrainTimeMs + 500; // never let the brain fall arbitrarily far behind

            foreach (var p in _populations) p.Sample(Brain);

            double now = BrainTimeMs;
            if (now - _lastSpikeMs > 250)
            {
                long total = Brain.TotalSpikes;
                SpikesPerSecond = (float)((total - _lastSpikeTotal) * 1000.0 / Math.Max(1e-3, now - _lastSpikeMs));
                _lastSpikeTotal = total;
                _lastSpikeMs = now;
            }
        }

        void LateUpdate()
        {
            if (!IsReady) return;
            FlushRates();
        }

        void StartThread()
        {
            _targetMs = Brain.TimeMs;
            _running = true;
            _thread = new Thread(SimulationLoop) { IsBackground = true, Name = "FlyBrain simulation", Priority = System.Threading.ThreadPriority.AboveNormal };
            _thread.Start();
        }

        void SimulationLoop()
        {
            var sw = new Stopwatch();
            double window = Brain.Parameters.Dt * Brain.DelaySteps;
            while (_running)
            {
                double lag = _targetMs - Brain.TimeMs;
                if (lag < window)
                {
                    Thread.Sleep(1);
                    continue;
                }
                int windows = (int)Math.Min(lag / window, 10);
                double before = Brain.TimeMs;
                sw.Restart();
                try
                {
                    Brain.RunSteps(windows * Brain.DelaySteps);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _running = false;
                    break;
                }
                double wall = sw.Elapsed.TotalMilliseconds;
                float cap = (float)((Brain.TimeMs - before) / Math.Max(0.01, wall));
                Capacity = Capacity <= 0 ? cap : Capacity + 0.1f * (cap - Capacity);
            }
        }

        void OnDestroy()
        {
            _destroyed = true;
            _running = false;
            _thread?.Join(2000);
            Brain?.Dispose();
            Connectome?.Dispose();
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------ populations and inputs

        public NeuronPopulation AddPopulation(string key, string label, int[] indices)
        {
            var p = new NeuronPopulation(key, label, indices);
            _populations.Add(p);
            return p;
        }

        /// <summary>Poisson rate driven by the virtual senses of the body.</summary>
        public void SetSensorRate(NeuronPopulation population, float hz)
        {
            if (!IsReady || population == null) return;
            foreach (var i in population.Indices)
            {
                if (Mathf.Abs(_sensorRate[i] - hz) < 0.5f && !(hz == 0 && _sensorRate[i] != 0)) continue;
                _sensorRate[i] = hz;
                _dirty.Add(i);
            }
        }

        /// <summary>Optogenetic-style activation set from the UI (0 removes it).</summary>
        public void SetOptoRate(IEnumerable<int> neurons, float hz)
        {
            if (!IsReady) return;
            foreach (var i in neurons)
            {
                _optoRate[i] = hz;
                _dirty.Add(i);
            }
        }

        public void SetOptoSilenced(IEnumerable<int> neurons, bool silenced)
        {
            if (!IsReady) return;
            foreach (var i in neurons)
            {
                _optoSilenced[i] = silenced;
                _dirty.Add(i);
            }
        }

        public float SensorRate(int neuron) => _sensorRate != null ? _sensorRate[neuron] : 0;

        void FlushRates()
        {
            if (_dirty.Count == 0) return;
            foreach (var i in _dirty)
            {
                float r = Mathf.Max(_sensorRate[i], _optoRate[i]);
                if (r != _appliedRate[i])
                {
                    Brain.SetPoissonRate(i, r);
                    _appliedRate[i] = r;
                }
                if (_optoSilenced[i] != _appliedSilenced[i])
                {
                    Brain.SetSilenced(i, _optoSilenced[i]);
                    _appliedSilenced[i] = _optoSilenced[i];
                }
            }
            _dirty.Clear();
        }

        /// <summary>Returns every neuron to rest (inputs and manipulations stay in place).</summary>
        public void ResetActivity()
        {
            if (!IsReady) return;
            Brain.RequestReset();
            foreach (var p in _populations) p.ResetRates();
        }
    }
}
