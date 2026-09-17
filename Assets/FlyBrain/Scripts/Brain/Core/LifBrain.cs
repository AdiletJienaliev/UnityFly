using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlyBrain.Brain
{
    /// <summary>
    /// C# port of the Brian2 LIF model from philshiu/Drosophila_brain_model (model.py).
    ///
    /// Every neuron obeys  dv/dt = (v0 - v + g)/t_mbr,  dg/dt = -g/tau  (unless refractory),
    /// spikes when v > v_th, resets v = v_rst, g = 0 and adds w_syn * (signed synapse count) to g
    /// of every postsynaptic partner after a 1.8 ms delay. "Activation" is Poisson input that kicks v
    /// by w_syn * f_poi (and removes the refractory period, as in model.poi); "silencing" zeroes all
    /// outgoing synapses (model.silence).
    ///
    /// Validated against the Brian2 results in the original repository (Pearson r = 0.9997 over all
    /// responding neurons, see BrainModel/BrainSim).
    ///
    /// Because every synapse has the same 18-step delay, the network is advanced in windows of up to
    /// 18 steps during which neuron partitions are independent, so they are integrated in parallel.
    /// </summary>
    public sealed unsafe class LifBrain : IDisposable
    {
        public readonly Connectome Connectome;
        public readonly LifParameters Parameters;
        public readonly int N;
        public readonly int DelaySteps;

        public NeuronState* State { get; private set; }
        public int* SpikeCount { get; private set; }
        public int* LastSpikeStep { get; private set; }

        /// <summary>Number of simulated 0.1 ms steps.</summary>
        public long Step { get; private set; }
        public double TimeMs => Step * Parameters.Dt;
        public long TotalSpikes { get; private set; }
        public int SpikesLastWindow { get; private set; }
        public int ActiveNeurons { get; private set; }
        /// <summary>Active neurons that had to be simulated step by step in the last window (the rest were advanced analytically).</summary>
        public int SteppedNeurons { get; private set; }
        /// <summary>Accumulated wall-clock time spent integrating neurons / propagating spikes (ms).</summary>
        public double NeuronMs, ScatterMs;

        /// <summary>Tolerance for snapping near-rest neurons to rest so they can be skipped (mV). 0 = exact.</summary>
        public float SnapTolerance = 1e-3f;

        readonly int _words;
        ulong* _activeBits;
        int* _localOf;
        byte* _partOfWord;
        readonly NeuronStepArgs[] _args;
        readonly WorkerPool _pool;
        readonly Action<int> _partBody;
        int* _allSpikes;
        byte* _allSpikeSteps;
        int _allCapacity;
        readonly List<IntPtr> _allocations = new List<IntPtr>();

        readonly object _inputLock = new object();
        readonly Dictionary<int, float> _pendingRates = new Dictionary<int, float>();
        readonly Dictionary<int, bool> _pendingSilence = new Dictionary<int, bool>();
        readonly float[] _rateHz;
        bool _pendingReset;

        const int InitialEventCapacity = 1 << 14;

        public LifBrain(Connectome connectome, LifParameters parameters = null, int threads = 0, uint seed = 12345)
        {
            Connectome = connectome;
            Parameters = parameters ?? new LifParameters();
            N = connectome.NeuronCount;
            DelaySteps = Parameters.DelaySteps;
            if (DelaySteps < 1 || DelaySteps > 255) throw new ArgumentException("delay must be 1..255 steps");

            _words = (N + 63) / 64;
            State = Alloc<NeuronState>(_words * 64L);
            _activeBits = Alloc<ulong>(_words);
            _localOf = Alloc<int>(_words * 64L);
            _partOfWord = Alloc<byte>(_words);
            SpikeCount = Alloc<int>(N);
            LastSpikeStep = Alloc<int>(N);
            _rateHz = new float[N];

            if (threads <= 0) threads = Math.Max(1, Environment.ProcessorCount - 1);
            _pool = new WorkerPool(threads);
            int parts = Math.Min(255, threads == 1 ? 1 : threads * 2);
            int wordsPerPart = (_words + parts - 1) / parts;
            parts = (_words + wordsPerPart - 1) / wordsPerPart;
            _args = new NeuronStepArgs[parts];

            var p = Parameters;
            // closed-form propagators over k steps: u_k = u*dv^k + g*C_k, g_k = g*dg^k
            var dvPow = Alloc<float>(DelaySteps + 1);
            var dgPow = Alloc<float>(DelaySteps + 1);
            var cPow = Alloc<float>(DelaySteps + 1);
            double ratio = p.TauSynapse / (p.TauMembrane - p.TauSynapse);
            for (int k = 0; k <= DelaySteps; k++)
            {
                double ev = Math.Exp(-k * p.Dt / p.TauMembrane), eg = Math.Exp(-k * p.Dt / p.TauSynapse);
                dvPow[k] = (float)ev;
                dgPow[k] = (float)eg;
                cPow[k] = (float)(ratio * (ev - eg));
            }
            double tPeak = Math.Log(p.TauMembrane / p.TauSynapse) * p.TauMembrane * p.TauSynapse / (p.TauMembrane - p.TauSynapse);
            float peak = (float)(ratio * (Math.Exp(-tPeak / p.TauMembrane) - Math.Exp(-tPeak / p.TauSynapse)) * 1.001);

            var rng = seed == 0 ? 1u : seed;
            for (int i = 0; i < parts; i++)
            {
                int wLo = i * wordsPerPart, wHi = Math.Min(_words, wLo + wordsPerPart);
                int neurons = (wHi - wLo) * 64;
                for (int w = wLo; w < wHi; w++) _partOfWord[w] = (byte)i;
                rng = rng * 747796405u + 2891336453u;
                _args[i] = new NeuronStepArgs
                {
                    WordLo = wLo, WordHi = wHi,
                    V0 = (float)p.V0, VReset = (float)p.VReset, VTh = (float)p.VThreshold,
                    DecayV = p.DecayV, DecayG = p.DecayG, CouplingGV = p.CouplingGV, Kick = p.PoissonKick,
                    Local = Alloc<NeuronState>(neurons),
                    LocalIndex = Alloc<int>(neurons),
                    SpikedAt = Alloc<int>(neurons),
                    Flags = Alloc<byte>(neurons),
                    PositiveInput = Alloc<float>(neurons),
                    NeuronEvStart = Alloc<int>(neurons + 1),
                    Stepwise = Alloc<int>(neurons),
                    BucketStart = Alloc<int>(256 + 1),
                    DecayVPow = dvPow, DecayGPow = dgPow, CouplingPow = cPow, PeakCoupling = peak,
                    OutSpikes = Alloc<int>((long)neurons * DelaySteps),
                    OutSpikeStep = Alloc<byte>((long)neurons * DelaySteps),
                    Rng = rng | 1u,
                };
                AllocateEvents(ref _args[i], InitialEventCapacity);
            }
            _partBody = RunPartition;
            ResetState();
            ClearStatistics();
        }

        T* Alloc<T>(long count) where T : unmanaged
        {
            long bytes = Math.Max(1, count) * sizeof(T);
            var ptr = (T*)Marshal.AllocHGlobal((IntPtr)bytes);
            for (long off = 0; off < bytes; off += int.MaxValue)
                new Span<byte>((byte*)ptr + off, (int)Math.Min(int.MaxValue, bytes - off)).Clear();
            _allocations.Add((IntPtr)ptr);
            return ptr;
        }

        void Free(void* ptr)
        {
            if (ptr == null) return;
            _allocations.Remove((IntPtr)ptr);
            Marshal.FreeHGlobal((IntPtr)ptr);
        }

        void AllocateEvents(ref NeuronStepArgs a, int capacity)
        {
            var target = Alloc<int>(capacity);
            var step = Alloc<int>(capacity);
            var weight = Alloc<float>(capacity);
            if (a.EvCount > 0)
            {
                Buffer.MemoryCopy(a.EvTarget, target, (long)capacity * 4, (long)a.EvCount * 4);
                Buffer.MemoryCopy(a.EvStep, step, (long)capacity * 4, (long)a.EvCount * 4);
                Buffer.MemoryCopy(a.EvWeight, weight, (long)capacity * 4, (long)a.EvCount * 4);
            }
            Free(a.EvTarget);
            Free(a.EvStep);
            Free(a.EvWeight);
            Free(a.BucketTarget);
            Free(a.BucketWeight);
            Free(a.NeuronEvStep);
            Free(a.NeuronEvWeight);
            a.EvTarget = target;
            a.EvStep = step;
            a.EvWeight = weight;
            a.BucketTarget = Alloc<int>(capacity);
            a.BucketWeight = Alloc<float>(capacity);
            a.NeuronEvStep = Alloc<byte>(capacity);
            a.NeuronEvWeight = Alloc<float>(capacity);
            a.EvCapacity = capacity;
        }

        // ------------------------------------------------------------------ inputs (thread-safe)

        /// <summary>Poisson activation of a neuron at the given rate (Hz). 0 removes the activation.</summary>
        public void SetPoissonRate(int neuron, float hz)
        {
            lock (_inputLock) _pendingRates[neuron] = Math.Max(0f, hz);
        }

        /// <summary>Silence a neuron: its spikes no longer reach postsynaptic partners.</summary>
        public void SetSilenced(int neuron, bool silenced)
        {
            lock (_inputLock) _pendingSilence[neuron] = silenced;
        }

        public float GetPoissonRate(int neuron) => _rateHz[neuron];
        public bool IsSilenced(int neuron) => State[neuron].Silenced != 0;
        public float MembranePotential(int neuron) => State[neuron].V;

        /// <summary>Requests all neurons to return to rest (applied before the next window).</summary>
        public void RequestReset()
        {
            lock (_inputLock) _pendingReset = true;
        }

        void ApplyInputs()
        {
            lock (_inputLock)
            {
                if (_pendingReset)
                {
                    ResetState();
                    _pendingReset = false;
                }
                if (_pendingRates.Count > 0)
                {
                    foreach (var kv in _pendingRates)
                    {
                        int i = kv.Key;
                        _rateHz[i] = kv.Value;
                        State[i].PoissonP = (float)(kv.Value * Parameters.Dt * 1e-3);
                        // model.poi sets rfc = 0 for Poisson targets
                        State[i].RefrLen = kv.Value > 0 ? (byte)0 : (byte)Parameters.RefractorySteps;
                        if (kv.Value > 0) Wake(i);
                    }
                    _pendingRates.Clear();
                }
                if (_pendingSilence.Count > 0)
                {
                    foreach (var kv in _pendingSilence) State[kv.Key].Silenced = kv.Value ? (byte)1 : (byte)0;
                    _pendingSilence.Clear();
                }
            }
        }

        void Wake(int i) => _activeBits[i >> 6] |= 1UL << (i & 63);

        void ResetState()
        {
            float v0 = (float)Parameters.V0;
            for (int i = 0; i < N; i++)
            {
                ref var x = ref State[i];
                x.V = v0;
                x.G = 0;
                x.Refr = 0;
                x.RefrLen = x.PoissonP > 0 ? (byte)0 : (byte)Parameters.RefractorySteps;
            }
            new Span<ulong>(_activeBits, _words).Clear();
            for (int p = 0; p < _args.Length; p++) _args[p].EvCount = 0;
            for (int i = 0; i < N; i++)
                if (State[i].PoissonP > 0) Wake(i);
        }

        /// <summary>Clears spike statistics (counts, last spike) without touching the dynamics.</summary>
        public void ClearStatistics()
        {
            new Span<int>(SpikeCount, N).Clear();
            for (int i = 0; i < N; i++) LastSpikeStep[i] = int.MinValue;
            TotalSpikes = 0;
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>Advances the network by the given number of 0.1 ms steps.</summary>
        public void RunSteps(long steps)
        {
            while (steps > 0)
            {
                int w = (int)Math.Min(steps, DelaySteps);
                RunWindow(w);
                steps -= w;
            }
        }

        public void RunMs(double ms) => RunSteps((long)Math.Round(ms / Parameters.Dt));

        void RunWindow(int nSteps)
        {
            ApplyInputs();
            for (int i = 0; i < _args.Length; i++)
            {
                ref var a = ref _args[i];
                a.State = State; a.ActiveBits = _activeBits; a.LocalOf = _localOf;
                a.SpikeCount = SpikeCount; a.LastSpikeStep = LastSpikeStep;
                a.StepStart = Step; a.NSteps = nSteps; a.OutCount = 0; a.Snap = SnapTolerance;
            }

            long t0 = Stopwatch.GetTimestamp();
            _pool.Run(_args.Length, _partBody);
            long t1 = Stopwatch.GetTimestamp();
            NeuronMs += (t1 - t0) * 1000.0 / Stopwatch.Frequency;

            int total = 0, active = 0, stepped = 0;
            for (int i = 0; i < _args.Length; i++)
            {
                total += _args[i].OutCount;
                active += _args[i].ActiveCount;
                stepped += _args[i].StepwiseCount;
            }
            ActiveNeurons = active;
            SteppedNeurons = stepped;

            if (total > 0)
            {
                if (total > _allCapacity)
                {
                    Free(_allSpikes);
                    Free(_allSpikeSteps);
                    _allCapacity = Math.Max(total, _allCapacity * 2);
                    _allSpikes = Alloc<int>(_allCapacity);
                    _allSpikeSteps = Alloc<byte>(_allCapacity);
                }
                int k = 0;
                for (int i = 0; i < _args.Length; i++)
                {
                    ref var a = ref _args[i];
                    Buffer.MemoryCopy(a.OutSpikes, _allSpikes + k, (long)a.OutCount * sizeof(int), (long)a.OutCount * sizeof(int));
                    Buffer.MemoryCopy(a.OutSpikeStep, _allSpikeSteps + k, a.OutCount, a.OutCount);
                    k += a.OutCount;
                }
                fixed (NeuronStepArgs* parts = _args)
                {
                    var s = new ScatterArgs
                    {
                        RowPtr = Connectome.RowPtr, Post = Connectome.Post, Weight = Connectome.Weight, State = State,
                        ActiveBits = _activeBits, PartOfWord = _partOfWord, Parts = parts,
                        Spikes = _allSpikes, SpikeStep = _allSpikeSteps, SpikeCount = total,
                        StepStart = Step, Delay = DelaySteps, WSyn = (float)Parameters.WSyn,
                        ResumeSpike = 0, ResumeSynapse = -1, FullPartition = -1,
                    };
                    while (!LifKernels.ScatterSpikes(&s))
                        AllocateEvents(ref parts[s.FullPartition], parts[s.FullPartition].EvCapacity * 2);
                }
                TotalSpikes += total;
                ScatterMs += (Stopwatch.GetTimestamp() - t1) * 1000.0 / Stopwatch.Frequency;
            }
            SpikesLastWindow = total;
            Step += nSteps;
        }

        void RunPartition(int index)
        {
            fixed (NeuronStepArgs* a = &_args[index])
                LifKernels.StepNeurons(a);
        }

        public void Dispose()
        {
            _pool.Dispose();
            foreach (var ptr in _allocations) Marshal.FreeHGlobal(ptr);
            _allocations.Clear();
            State = null;
        }
    }
}
