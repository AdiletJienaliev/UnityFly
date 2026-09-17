using System;
using FlyBrain.Brain;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// A named set of model neurons (e.g. "left LC4", "MN9") with smoothed firing rates
    /// estimated from the spike counters of the simulation.
    /// </summary>
    public sealed unsafe class NeuronPopulation
    {
        public readonly string Key;
        public readonly string Label;
        public readonly int[] Indices;

        /// <summary>Mean firing rate per neuron (Hz), exponential smoothing with <see cref="TauMs"/>.</summary>
        public float Rate { get; private set; }
        /// <summary>Mean firing rate per neuron (Hz) with a short time constant (for command-like neurons).</summary>
        public float FastRate { get; private set; }
        /// <summary>Highest rate among individual neurons over the last sample interval (Hz, smoothed).</summary>
        public float PeakRate { get; private set; }

        public float TauMs = 120f;
        public float FastTauMs = 25f;

        long _lastTotal;
        double _lastMs = -1;
        readonly int[] _lastCounts;

        public NeuronPopulation(string key, string label, int[] indices)
        {
            Key = key;
            Label = label;
            Indices = indices ?? Array.Empty<int>();
            _lastCounts = new int[Indices.Length];
        }

        public bool IsEmpty => Indices.Length == 0;

        internal void Sample(LifBrain brain)
        {
            if (Indices.Length == 0) return;
            double now = brain.TimeMs;
            if (_lastMs >= 0 && now >= _lastMs && now - _lastMs < 0.05) return;
            int* counts = brain.SpikeCount;
            long total = 0;
            int peak = 0;
            for (int k = 0; k < Indices.Length; k++)
            {
                int c = counts[Indices[k]];
                total += c;
                int d = c - _lastCounts[k];
                if (d > peak) peak = d;
                _lastCounts[k] = c;
            }
            if (_lastMs < 0 || now < _lastMs || total < _lastTotal)
            {
                _lastMs = now;
                _lastTotal = total;
                return;
            }
            double dt = now - _lastMs;
            float inst = (float)((total - _lastTotal) * 1000.0 / (Indices.Length * dt));
            float instPeak = (float)(peak * 1000.0 / dt);
            float a = 1f - Mathf.Exp((float)(-dt / TauMs));
            float af = 1f - Mathf.Exp((float)(-dt / FastTauMs));
            Rate += a * (inst - Rate);
            FastRate += af * (inst - FastRate);
            PeakRate += a * (instPeak - PeakRate);
            _lastMs = now;
            _lastTotal = total;
        }

        internal void ResetRates()
        {
            Rate = FastRate = PeakRate = 0;
            _lastMs = -1;
        }
    }
}
