using System.Runtime.InteropServices;
#if UNITY_2019_3_OR_NEWER
using Unity.Burst;
using Unity.Mathematics;
#endif

namespace FlyBrain.Brain
{
    /// <summary>Per-neuron state packed into 16 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NeuronState
    {
        public float V;          // membrane potential (mV)
        public float G;          // synaptic conductance term (mV)
        public float PoissonP;   // per-step probability of a Poisson kick (0 = not stimulated)
        public byte Refr;        // remaining refractory steps
        public byte RefrLen;     // refractory steps after a spike (0 for Poisson-driven neurons, as in model.poi)
        public byte Silenced;    // outgoing synapses disabled (model.silence)
        public byte Reserved;
    }

    /// <summary>One partition of neurons [WordLo*64, WordHi*64) plus its pending synaptic events.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NeuronStepArgs
    {
        public NeuronState* State;
        public ulong* ActiveBits;   // bit i set: neuron i is away from rest or has pending input
        public int* LocalOf;        // global neuron -> index in this partition's local buffer (valid for gathered neurons)
        public int* SpikeCount;
        public int* LastSpikeStep;

        // synaptic events addressed to this partition: target neuron, absolute arrival step, weight (mV)
        public int* EvTarget;
        public int* EvStep;
        public float* EvWeight;
        public int EvCount, EvCapacity;

        // scratch buffers owned by the partition (per gathered neuron)
        public NeuronState* Local;
        public int* LocalIndex;
        public int* SpikedAt;
        public byte* Flags;          // bit0: keep active (future events), bit1: simulated step by step
        public float* PositiveInput; // sum of excitatory input arriving this window
        public int* NeuronEvStart;   // per-neuron event lists for analytically advanced neurons
        public int* Stepwise;        // local indices of neurons simulated step by step
        // scratch buffers per event
        public int* BucketTarget;
        public float* BucketWeight;
        public byte* NeuronEvStep;
        public float* NeuronEvWeight;
        public int* BucketStart;     // NSteps + 1 entries

        // closed-form propagators for k = 0..D steps
        public float* DecayVPow, DecayGPow, CouplingPow;
        public float PeakCoupling;   // max over t of the voltage response to a unit step in g

        public int* OutSpikes;       // spiking neuron indices
        public byte* OutSpikeStep;   // step offset inside the window for each recorded spike
        public int OutCount;
        public int ActiveCount, StepwiseCount;
        public int WordLo, WordHi;
        public long StepStart;
        public int NSteps;
        public float V0, VReset, VTh, DecayV, DecayG, CouplingGV, Kick;
        public float Snap;           // |v-v0| and |g| below this snap to exact rest (0 disables)
        public uint Rng;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ScatterArgs
    {
        public int* RowPtr;
        public int* Post;
        public short* Weight;
        public NeuronState* State;
        public ulong* ActiveBits;
        public byte* PartOfWord;
        public NeuronStepArgs* Parts;
        public int* Spikes;
        public byte* SpikeStep;
        public int SpikeCount;
        public long StepStart;
        public int Delay;
        public float WSyn;
        // resume point when a partition's event buffer is full
        public int ResumeSpike, ResumeSynapse, FullPartition;
    }

    /// <summary>
    /// Inner loops of the LIF brain. Compiled by Burst inside Unity, plain C# elsewhere.
    ///
    /// The per-step update order mirrors Brian2's schedule: state update, threshold, synapses (+PoissonInput), reset.
    /// Only active neurons are touched: they are gathered into a dense local buffer, synaptic input arrives as a
    /// sparse event list, and results are written back. Within a window, a neuron that provably cannot reach
    /// threshold (bound from superposition of the linear responses to its current state and to all excitatory
    /// input still to arrive) is advanced analytically from event to event, which gives the same trajectory as
    /// stepping it; every other neuron is simulated step by step.
    /// </summary>
#if UNITY_2019_3_OR_NEWER
    [BurstCompile]
#endif
    public static unsafe class LifKernels
    {
        const byte FlagKeep = 1, FlagStepwise = 2;

#if UNITY_2019_3_OR_NEWER
        static int Tzcnt(ulong x) => math.tzcnt(x);
#else
        static int Tzcnt(ulong x) => System.Numerics.BitOperations.TrailingZeroCount(x);
#endif

#if UNITY_2019_3_OR_NEWER
        [BurstCompile(CompileSynchronously = true)]
#endif
        public static void StepNeurons(NeuronStepArgs* a)
        {
            NeuronState* st = a->State, loc = a->Local;
            ulong* bits = a->ActiveBits;
            int* localOf = a->LocalOf, localIndex = a->LocalIndex, spikedAt = a->SpikedAt, stepwise = a->Stepwise;
            byte* flags = a->Flags;
            float* posInput = a->PositiveInput;
            int* spikeCount = a->SpikeCount, lastSpike = a->LastSpikeStep, outSpikes = a->OutSpikes;
            byte* outStep = a->OutSpikeStep;
            float v0 = a->V0, vReset = a->VReset, vTh = a->VTh, dv = a->DecayV, dg = a->DecayG, cgv = a->CouplingGV, kick = a->Kick, snap = a->Snap;
            float* dvPow = a->DecayVPow, dgPow = a->DecayGPow, cPow = a->CouplingPow;
            int wLo = a->WordLo, wHi = a->WordHi, nSteps = a->NSteps;
            long stepStart = a->StepStart;
            uint rng = a->Rng;
            int count = 0;

            // 1. gather active neurons into the dense local buffer
            int m = 0;
            for (int w = wLo; w < wHi; w++)
            {
                ulong word = bits[w];
                while (word != 0)
                {
                    int i = (w << 6) | Tzcnt(word);
                    word &= word - 1;
                    localIndex[m] = i;
                    loc[m] = st[i];
                    localOf[i] = m;
                    spikedAt[m] = -1;
                    flags[m] = 0;
                    posInput[m] = 0f;
                    m++;
                }
            }

            // 2. events of this window: bucket by step (later events stay queued) and sum excitatory input per neuron
            int* bucketStart = a->BucketStart;
            for (int s = 0; s <= nSteps; s++) bucketStart[s] = 0;
            int kept = 0;
            for (int e = 0; e < a->EvCount; e++)
            {
                long off = a->EvStep[e] - stepStart;
                if (off < 0) off = 0;
                if (off < nSteps) bucketStart[off + 1]++;
            }
            for (int s = 0; s < nSteps; s++) bucketStart[s + 1] += bucketStart[s];
            for (int e = 0; e < a->EvCount; e++)
            {
                int target = a->EvTarget[e];
                int j = localOf[target];
                long off = a->EvStep[e] - stepStart;
                if (off < 0) off = 0;
                if (off < nSteps)
                {
                    int pos = bucketStart[off]++;
                    a->BucketTarget[pos] = j;
                    a->BucketWeight[pos] = a->EvWeight[e];
                    if (a->EvWeight[e] > 0f) posInput[j] += a->EvWeight[e];
                }
                else
                {
                    flags[j] |= FlagKeep;
                    a->EvTarget[kept] = target;
                    a->EvStep[kept] = a->EvStep[e];
                    a->EvWeight[kept] = a->EvWeight[e];
                    kept++;
                }
            }
            for (int s = nSteps; s > 0; s--) bucketStart[s] = bucketStart[s - 1];
            bucketStart[0] = 0;
            int windowEvents = bucketStart[nSteps];
            a->EvCount = kept;

            // 3. classify: neurons that might spike (or are stimulated / refractory) are stepped
            int nStep = 0;
            float peak = a->PeakCoupling;
            const float margin = 0.01f;
            for (int j = 0; j < m; j++)
            {
                NeuronState* x = loc + j;
                float u = x->V - v0;
                float bound = (u > 0f ? u : 0f) + ((x->G > 0f ? x->G : 0f) + posInput[j]) * peak;
                if (x->PoissonP > 0f || x->Refr != 0 || v0 + bound > vTh - margin)
                {
                    flags[j] |= FlagStepwise;
                    stepwise[nStep++] = j;
                }
            }

            // per-neuron event lists (step order) for the analytic neurons
            int* evStart = a->NeuronEvStart;
            for (int j = 0; j <= m; j++) evStart[j] = 0;
            for (int e = 0; e < windowEvents; e++)
            {
                int j = a->BucketTarget[e];
                if ((flags[j] & FlagStepwise) == 0) evStart[j + 1]++;
            }
            for (int j = 0; j < m; j++) evStart[j + 1] += evStart[j];
            for (int s = 0; s < nSteps; s++)
            {
                int end = bucketStart[s + 1];
                for (int e = bucketStart[s]; e < end; e++)
                {
                    int j = a->BucketTarget[e];
                    if ((flags[j] & FlagStepwise) != 0) continue;
                    int pos = evStart[j]++;
                    a->NeuronEvStep[pos] = (byte)s;
                    a->NeuronEvWeight[pos] = a->BucketWeight[e];
                }
            }
            for (int j = m; j > 0; j--) evStart[j] = evStart[j - 1];
            evStart[0] = 0;

            // 4a. analytic neurons: jump from event to event with the exact propagator
            for (int j = 0; j < m; j++)
            {
                if ((flags[j] & FlagStepwise) != 0) continue;
                NeuronState* x = loc + j;
                float u = x->V - v0, g = x->G;
                int t = 0; // steps already integrated
                int end = evStart[j + 1];
                for (int e = evStart[j]; e < end; e++)
                {
                    int k = a->NeuronEvStep[e] + 1 - t; // integrate up to and including the arrival step
                    if (k > 0)
                    {
                        float nu = u * dvPow[k] + g * cPow[k];
                        g *= dgPow[k];
                        u = nu;
                        t += k;
                    }
                    g += a->NeuronEvWeight[e];
                }
                int rest = nSteps - t;
                if (rest > 0)
                {
                    float nu = u * dvPow[rest] + g * cPow[rest];
                    g *= dgPow[rest];
                    u = nu;
                }
                if (g < snap && g > -snap && u < snap && u > -snap)
                {
                    g = 0f;
                    u = 0f;
                }
                x->V = v0 + u;
                x->G = g;
            }

            // 4b. stepwise neurons
            for (int s = 0; s < nSteps; s++)
            {
                int stepInt = (int)(stepStart + s);
                for (int q = 0; q < nStep; q++)
                {
                    int j = stepwise[q];
                    NeuronState* x = loc + j;
                    float gi = x->G;
                    float vi = x->V;
                    int r = x->Refr;

                    bool spike = false;
                    if (r > 0)
                    {
                        r--;
                        x->Refr = (byte)r;
                    }
                    if (r == 0)
                    {
                        // state update (exact linear integration), frozen while refractory, then threshold
                        float nv = v0 + (vi - v0) * dv + gi * cgv;
                        gi *= dg;
                        vi = nv;
                        spike = vi > vTh;

                        // PoissonInput (v += w_syn * f_poi); like every write to v/g except the reset it is
                        // dropped when the neuron is refractory or has just spiked ("unless refractory" in Brian2)
                        float p = x->PoissonP;
                        if (p > 0f && !spike)
                        {
                            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                            if ((rng >> 8) * (1f / 16777216f) < p) vi += kick;
                        }
                    }

                    if (spike)
                    {
                        vi = vReset;
                        gi = 0f;
                        x->Refr = x->RefrLen;
                        spikedAt[j] = s;
                        int i = localIndex[j];
                        spikeCount[i]++;
                        lastSpike[i] = stepInt;
                        outSpikes[count] = i;
                        outStep[count] = (byte)s;
                        count++;
                    }
                    else if (gi < snap && gi > -snap && vi - v0 < snap && vi - v0 > -snap)
                    {
                        gi = 0f;
                        vi = v0;
                    }
                    x->V = vi;
                    x->G = gi;
                }

                // synaptic input arriving this step (on_pre: g += w), dropped for refractory / just-spiked neurons
                int end = bucketStart[s + 1];
                for (int e = bucketStart[s]; e < end; e++)
                {
                    int j = a->BucketTarget[e];
                    if ((flags[j] & FlagStepwise) == 0) continue;
                    NeuronState* x = loc + j;
                    if (x->Refr == 0 && spikedAt[j] != s) x->G += a->BucketWeight[e];
                }
            }

            // 5. write back; neurons at rest without queued events leave the active set
            int active = 0;
            for (int j = 0; j < m; j++)
            {
                int i = localIndex[j];
                NeuronState* x = loc + j;
                st[i] = *x;
                if ((flags[j] & FlagKeep) == 0 && x->Refr == 0 && x->G == 0f && x->V == v0 && x->PoissonP <= 0f)
                    bits[i >> 6] &= ~(1UL << (i & 63));
                else
                    active++;
            }

            a->ActiveCount = active;
            a->StepwiseCount = nStep;
            a->OutCount = count;
            a->Rng = rng;
        }

        /// <summary>
        /// Turns spikes into synaptic events for the postsynaptic partitions and marks the targets active.
        /// Returns false if a partition's event buffer is full (see FullPartition / Resume*).
        /// </summary>
#if UNITY_2019_3_OR_NEWER
        [BurstCompile(CompileSynchronously = true)]
#endif
        public static bool ScatterSpikes(ScatterArgs* a)
        {
            int* rowPtr = a->RowPtr, post = a->Post, spikes = a->Spikes;
            short* w = a->Weight;
            byte* spikeStep = a->SpikeStep, partOfWord = a->PartOfWord;
            ulong* bits = a->ActiveBits;
            NeuronState* st = a->State;
            NeuronStepArgs* parts = a->Parts;
            float wsyn = a->WSyn;

            for (int j = a->ResumeSpike; j < a->SpikeCount; j++)
            {
                int pre = spikes[j];
                if (st[pre].Silenced != 0)
                {
                    a->ResumeSynapse = -1;
                    continue;
                }
                int arrival = (int)(a->StepStart + spikeStep[j] + a->Delay);
                int end = rowPtr[pre + 1];
                int k = a->ResumeSynapse >= 0 ? a->ResumeSynapse : rowPtr[pre];
                a->ResumeSynapse = -1;
                for (; k < end; k++)
                {
                    int target = post[k];
                    int p = partOfWord[target >> 6];
                    NeuronStepArgs* part = parts + p;
                    if (part->EvCount == part->EvCapacity)
                    {
                        a->ResumeSpike = j;
                        a->ResumeSynapse = k;
                        a->FullPartition = p;
                        return false;
                    }
                    int e = part->EvCount++;
                    part->EvTarget[e] = target;
                    part->EvStep[e] = arrival;
                    part->EvWeight[e] = w[k] * wsyn;
                    bits[target >> 6] |= 1UL << (target & 63);
                }
            }
            a->ResumeSpike = a->SpikeCount;
            return true;
        }
    }
}
