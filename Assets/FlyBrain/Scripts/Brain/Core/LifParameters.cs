using System;

namespace FlyBrain.Brain
{
    /// <summary>
    /// Constants of the leaky integrate-and-fire model, identical to <c>default_params</c> in
    /// philshiu/Drosophila_brain_model/model.py. Units: mV, ms, Hz.
    /// </summary>
    [Serializable]
    public class LifParameters
    {
        public double Dt = 0.1;          // Brian2 default clock (ms)
        public double V0 = -52.0;        // resting potential
        public double VReset = -52.0;    // reset potential after spike
        public double VThreshold = -45.0;
        public double TauMembrane = 20.0;
        public double TauSynapse = 5.0;  // 'tau' (decay of g)
        public double Refractory = 2.2;
        public double Delay = 1.8;       // synaptic delay
        public double WSyn = 0.275;      // mV per synapse
        public double FPoisson = 250.0;  // Poisson kick = WSyn * FPoisson

        public int DelaySteps => (int)Math.Round(Delay / Dt);
        public int RefractorySteps => (int)Math.Round(Refractory / Dt);
        public float PoissonKick => (float)(WSyn * FPoisson);

        // Exact integration of dv/dt = (v0 - v + g)/tm, dg/dt = -g/tau over one step (Brian2 method='linear')
        public float DecayV => (float)Math.Exp(-Dt / TauMembrane);
        public float DecayG => (float)Math.Exp(-Dt / TauSynapse);
        public float CouplingGV => (float)(TauSynapse / (TauMembrane - TauSynapse) * (Math.Exp(-Dt / TauMembrane) - Math.Exp(-Dt / TauSynapse)));
    }
}
