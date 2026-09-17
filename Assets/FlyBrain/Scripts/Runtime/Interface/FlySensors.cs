using UnityEngine;

namespace FlyBrain
{
    /// <summary>What the virtual sense organs report this frame. Rates (Hz) are raw receptor drive before state-dependent gain.</summary>
    public struct SensoryState
    {
        public float Sugar, Water, Bitter;              // labellar gustatory receptor neurons
        public float JoL, JoR, JoFL, JoFR;              // Johnston's organ (antennal deflection): C/E and F classes
        public float EyeBristleL, EyeBristleR;          // eye bristle mechanosensory neurons (mean rate over the population)
        public int DustL, DustR;                        // dust grains touching bristles on each eye
        public float LoomL, LoomR;                      // LC4 / LPLC2 looming detectors per eye
        public float FrontalLoom;                       // LC16 (both eyes)
        public float ObjectL, ObjectR;                  // LC10a small moving object
        public bool LegsInFood, LabellumInFood, LegsInWater, LabellumInWater, OnWater;
        public FoodSource TouchedFood, TouchedWater;
        public Vector3 ThreatDirection;
        public float ThreatLoom;                        // strongest approaching creature 0..1
        public string ThreatKind;
        public float WindSpeed;
        public Vector3 WindAtHead;                      // air velocity relative to the ground (mm/s)
        public float OdorL, OdorR;                      // fermentation odor at each antenna (not fed to the connectome)
        public float HumidityL, HumidityR;
        public float Light;
        public float EgoLoom;                           // strongest expansion from approaching surfaces (1/s)
    }

    /// <summary>
    /// Converts the world around a fly body into sensory neuron firing rates. Receptor physiology is reduced to
    /// simple rate codes; everything downstream of the sensory neurons is the connectome model.
    /// </summary>
    public sealed class FlySensors
    {
        readonly FlyRig _rig;
        readonly FlyMotor _motor;
        readonly float[] _joAdapt = new float[2];
        readonly Vector3[] _eyeRays;
        float _dt, _dustTimer;

        public const float MaxGustatoryHz = 160f;
        public const float MaxJoHz = 150f;
        public const float MaxLoomHz = 160f;
        public const float EyeBristleHz = 120f;
        public const int EyeBristleChunks = 5;

        public FlySensors(FlyRig rig, FlyMotor motor)
        {
            _rig = rig;
            _motor = motor;
            // ommatidial sample directions (head space): a frontal-lateral field of view per eye, plus a few ventral rays
            var rays = new System.Collections.Generic.List<Vector3>();
            for (int side = -1; side <= 1; side += 2)
                foreach (float el in new[] { -55f, -25f, 5f, 35f })
                    foreach (float az in new[] { 5f, 40f, 80f, 120f })
                        rays.Add(Quaternion.Euler(-el, side * az, 0) * Vector3.forward);
            _eyeRays = rays.ToArray();
        }

        public SensoryState Update(float dt)
        {
            var s = new SensoryState();
            if (dt <= 0) dt = 1e-3f;
            _dt = dt;
            var env = FlyEnvironment.Current;
            s.Light = env != null ? env.LightLevel : 1f;
            TasteSense(ref s);
            Wind(ref s);
            Smell(ref s);
            Dust(ref s);
            Vision(ref s);
            return s;
        }

        void TasteSense(ref SensoryState s)
        {
            Vector3 labellum = _rig.Proboscis.Tip;
            Vector3 head = _rig.HeadVisual.position;
            _motor.HasLiquidUnderHead = false;
            float bestSurface = float.NegativeInfinity;
            Vector3 up = _motor.SurfaceUp;
            foreach (var food in FoodSource.All)
            {
                if (food.IsEmpty) continue;
                float reach = food.CurrentRadius + 3f;
                if ((food.transform.position - head).sqrMagnitude > reach * reach) continue;
                float drive = 0;
                // tarsal taste: front and middle claws standing in the liquid. Tarsal GRNs reach the brain through
                // the VNC, which is not in the connectome, so they are proxied by the labellar GRN populations.
                bool legs = false;
                for (int i = 0; i < 6; i++)
                {
                    if (!_motor.IsPlanted(i)) continue;
                    if (food.Contains(_motor.ClawPosition(i), 0.15f))
                    {
                        if (_rig.Legs[i].Pair != 2) drive = Mathf.Max(drive, 0.65f);
                        legs = true;
                    }
                }
                bool lab = food.Contains(labellum, 0.1f);
                if (lab) drive = 1f;
                if (food.Taste == Taste.Water)
                {
                    if (legs) s.LegsInWater = true;
                    if (lab) s.LabellumInWater = true;
                    if (legs || lab) s.TouchedWater = food;
                    if (legs && food.CurrentRadius > 10f) s.OnWater = true;
                }
                else
                {
                    if (drive > 0 && legs) s.LegsInFood = true;
                    if (lab) s.LabellumInFood = true;
                }
                if (drive > 0)
                {
                    float r = MaxGustatoryHz * drive * food.Concentration;
                    switch (food.Taste)
                    {
                        case FlyBrain.Taste.Sugar: s.Sugar = Mathf.Max(s.Sugar, r); break;
                        case FlyBrain.Taste.Water: s.Water = Mathf.Max(s.Water, r); break;
                        default: s.Bitter = Mathf.Max(s.Bitter, r); break;
                    }
                    if (food.Taste != Taste.Water && (s.TouchedFood == null || drive >= 1)) s.TouchedFood = food;
                }
                if (food.SurfacePoint(head, out var surface))
                {
                    float h = Vector3.Dot(surface - head, up);
                    if (h > bestSurface)
                    {
                        bestSurface = h;
                        _motor.HasLiquidUnderHead = true;
                        _motor.LiquidUnderHead = surface;
                    }
                }
            }
        }

        float HeightAboveSurface(Vector3 p) => _motor.IsAirborne ? Mathf.Max(1f, _motor.HeightAboveGround) : 0.8f;

        void Wind(ref SensoryState s)
        {
            Vector3 self = _motor.Velocity;
            Vector3 headWind = WindField.At(_rig.HeadVisual.position, HeightAboveSurface(_rig.HeadVisual.position));
            s.WindAtHead = headWind;
            for (int side = 0; side < 2; side++)
            {
                Vector3 tip = _rig.Antennae[side].Tip;
                Vector3 w = WindField.At(tip, HeightAboveSurface(tip)) - self;
                float speed = w.magnitude;
                s.WindSpeed = Mathf.Max(s.WindSpeed, speed);
                // arista/funiculus rotation adapts to sustained air flow; changes (gusts, puffs) drive the JO neurons
                _joAdapt[side] += (speed - _joAdapt[side]) * (1 - Mathf.Exp(-_dt / 1.2f));
                float change = Mathf.Max(0f, speed - 0.8f * _joAdapt[side]);
                float gain = _motor.IsAirborne ? 0.35f : 1f; // antennae are held actively in flight
                float ce = MaxJoHz * Mathf.Clamp01((change - 40f) / 260f) * gain;
                float f = 90f * Mathf.Clamp01((change - 180f) / 320f) * gain;
                if (side == 0) { s.JoL = ce; s.JoFL = f; }
                else { s.JoR = ce; s.JoFR = f; }
                _motor.AntennaWind[side] = Vector3.ClampMagnitude(w * 0.0006f, 0.22f);
            }
        }

        void Smell(ref SensoryState s)
        {
            // effective separation includes active antennal sampling (a few mm), which makes plume edges detectable
            var head = _rig.HeadVisual;
            Vector3 l = head.position - head.right * 1.2f + head.forward * 0.4f;
            Vector3 r = head.position + head.right * 1.2f + head.forward * 0.4f;
            s.OdorL = OdorField.Concentration(l);
            s.OdorR = OdorField.Concentration(r);
            s.HumidityL = OdorField.Humidity(l);
            s.HumidityR = OdorField.Humidity(r);
        }

        void Dust(ref SensoryState s)
        {
            var head = _rig.HeadVisual;
            // grooming legs knock dust off the head
            for (int i = DustParticle.All.Count - 1; i >= 0; i--)
            {
                var d = DustParticle.All[i];
                if (d.Head != head) continue;
                bool removed = false;
                for (int leg = 0; leg < 6 && !removed; leg++)
                {
                    if (_rig.Legs[leg].Pair != 0 || !_motor.IsGroomingLeg(leg)) continue;
                    var tarsus = _rig.Legs[leg].Tarsus;
                    Vector3 a = tarsus.Root, b = tarsus.Tip;
                    Vector3 p = d.transform.position;
                    Vector3 closest = a + Vector3.Project(p - a, b - a);
                    if (Vector3.Dot(closest - a, b - a) < 0) closest = a;
                    if ((closest - a).sqrMagnitude > (b - a).sqrMagnitude) closest = b;
                    // a grain under a sweeping leg comes off after ~0.5 s of contact on average
                    if ((closest - p).sqrMagnitude < 0.25f * 0.25f && Random.value < 1f - Mathf.Exp(-2f * _dt)) removed = true;
                }
                if (!removed && _motor.IsAirborne && Random.value < 1f - Mathf.Exp(-0.3f * _dt)) removed = true;
                if (removed) d.Detach();
            }

            // natural soiling: soil crumbs while walking on the ground, pollen and spores on fruit and in breezes
            var env = FlyEnvironment.Current;
            if (env != null && env.IsNature)
            {
                _dustTimer -= _dt;
                // grains per second
                float rate = 1f / 400f;
                if (!_motor.IsAirborne && Mathf.Abs(_motor.Speed) > 2f) rate += 1f / 120f;
                if (s.WindSpeed > 500f) rate += 1f / 150f;
                if (s.TouchedFood != null && s.TouchedFood.Taste == Taste.Bitter) rate += 1f / 8f; // mold spores
                if (_dustTimer <= 0 && Random.value < 1f - Mathf.Exp(-rate * _dt))
                {
                    _dustTimer = 2f;
                    int side = Random.value < 0.5f ? -1 : 1;
                    Vector3 dir = Random.onUnitSphere;
                    dir.x = Mathf.Abs(dir.x) * side;
                    Vector3 local = new Vector3(side * 0.22f, 0.03f, 0.19f) + Vector3.Scale(dir, new Vector3(0.17f, 0.26f, 0.22f));
                    var color = Random.value < 0.3f ? new Color(0.95f, 0.8f, 0.25f) : new Color(0.45f, 0.36f, 0.26f);
                    DustParticle.Create(head, local, side, true, color);
                }
            }

            s.DustL = DustParticle.CountOn(head, -1);
            s.DustR = DustParticle.CountOn(head, 1);
            // every grain bends a patch of bristles: one fifth of the eye bristle neurons of that eye per grain
            s.EyeBristleL = EyeBristleHz * Mathf.Min(EyeBristleChunks, s.DustL) / EyeBristleChunks;
            s.EyeBristleR = EyeBristleHz * Mathf.Min(EyeBristleChunks, s.DustR) / EyeBristleChunks;
        }

        void Vision(ref SensoryState s)
        {
            var head = _rig.HeadVisual;
            Vector3 eye = head.TransformPoint(new Vector3(0, 0.03f, 0.2f));
            Vector3 self = _motor.Velocity;
            float light = Mathf.Clamp01(s.Light * 1.6f);
            float bestLoom = 0;

            // 1) other animals and approaching objects
            foreach (var obj in SeenObject.All)
            {
                if (obj.Owner == _rig.Root) continue;
                Vector3 rel = obj.transform.position - eye;
                float d = Mathf.Max(rel.magnitude, obj.Radius * 1.01f);
                if (d > obj.Radius * 250f || d > 3500f) continue;
                Vector3 relVel = obj.Velocity - self;
                float closing = -Vector3.Dot(relVel, rel / d);
                float theta = 2f * Mathf.Asin(Mathf.Clamp01(obj.Radius / d));
                Vector3 local = head.InverseTransformDirection(rel);
                float azimuth = Mathf.Atan2(local.x, local.z);
                bool looming = closing > 0 && theta > 0.05f;
                bool small = theta < 0.35f;
                if (!looming && !small) continue;
                // occluded by leaves, fruit, the ground?
                if (Physics.Raycast(eye, rel / d, d - obj.Radius, FlyLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;

                if (looming)
                {
                    // angular expansion rate (rad/s) of a sphere approaching at "closing" speed
                    float thetaRate = 2f * obj.Radius * closing / (d * d * Mathf.Sqrt(Mathf.Max(1e-4f, 1f - obj.Radius * obj.Radius / (d * d))));
                    float loom = Mathf.Clamp01((thetaRate - 0.25f) / 3f) * Mathf.Clamp01(theta / 0.3f) * light;
                    if (loom > 0)
                    {
                        float wl = azimuth < 0.35f ? 1f : 0f;
                        float wr = azimuth > -0.35f ? 1f : 0f;
                        if (Mathf.Abs(azimuth) > 2.8f) { wl *= 0.4f; wr *= 0.4f; } // behind the head
                        s.LoomL = Mathf.Max(s.LoomL, MaxLoomHz * loom * wl);
                        s.LoomR = Mathf.Max(s.LoomR, MaxLoomHz * loom * wr);
                        if (Mathf.Abs(azimuth) < 0.6f) s.FrontalLoom = Mathf.Max(s.FrontalLoom, MaxLoomHz * loom);
                        if (loom > bestLoom)
                        {
                            bestLoom = loom;
                            s.ThreatDirection = rel / d;
                            s.ThreatKind = obj.Kind;
                        }
                    }
                }
                if (small && Mathf.Abs(azimuth) < 1.9f && d < 60f)
                {
                    // LC10a: small moving objects in the frontal-lateral field
                    float angularSpeed = Vector3.ProjectOnPlane(relVel, rel / d).magnitude / d;
                    float drive = Mathf.Clamp01(angularSpeed / 2f) * Mathf.Clamp01(theta / 0.04f) * light;
                    float hz = 110f * drive;
                    if (azimuth < 0) s.ObjectL = Mathf.Max(s.ObjectL, hz);
                    else s.ObjectR = Mathf.Max(s.ObjectR, hz);
                }
            }
            s.ThreatLoom = bestLoom;

            // 2) surfaces rushing towards the eyes (flight approach, landing, collisions): expansion = approach speed / distance
            float speed = self.magnitude;
            if (speed > 20f)
            {
                float egoL = 0, egoR = 0, frontal = 0;
                for (int k = 0; k < _eyeRays.Length; k++)
                {
                    Vector3 dir = head.TransformDirection(_eyeRays[k]);
                    float approach = Vector3.Dot(self, dir);
                    if (approach < 10f) continue;
                    if (!Physics.Raycast(eye, dir, out var hit, 400f, FlyLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                    float e = approach / Mathf.Max(hit.distance, 1f);
                    if (_eyeRays[k].x < 0.2f) egoL = Mathf.Max(egoL, e);
                    if (_eyeRays[k].x > -0.2f) egoR = Mathf.Max(egoR, e);
                    if (Mathf.Abs(_eyeRays[k].x) < 0.25f) frontal = Mathf.Max(frontal, e);
                }
                s.EgoLoom = Mathf.Max(egoL, egoR);
                // walking flies suppress responses to self-generated expansion; in flight the threshold is low
                float threshold = _motor.IsAirborne ? 5f : 14f;
                float range = _motor.IsAirborne ? 22f : 30f;
                s.LoomL = Mathf.Max(s.LoomL, MaxLoomHz * Mathf.Clamp01((egoL - threshold) / range) * light);
                s.LoomR = Mathf.Max(s.LoomR, MaxLoomHz * Mathf.Clamp01((egoR - threshold) / range) * light);
                if (!_motor.IsAirborne) s.FrontalLoom = Mathf.Max(s.FrontalLoom, MaxLoomHz * Mathf.Clamp01((frontal - threshold) / range) * light);
            }
        }
    }
}
