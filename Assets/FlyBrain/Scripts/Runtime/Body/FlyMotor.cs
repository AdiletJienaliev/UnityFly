using UnityEngine;

namespace FlyBrain
{
    public enum FlyMode { Walk, Feed, Groom, Freeze, Backward, Flight, Rest, Sleep, Drink }

    /// <summary>Which body part a grooming bout cleans (and therefore which legs move).</summary>
    public enum GroomKind { Antennae, Eyes, FrontLegs, HindLegs }

    /// <summary>Motor command decoded from descending / motor neuron activity and the virtual VNC (see FlyNervousSystem).</summary>
    public struct MotorCommand
    {
        public FlyMode Mode;
        public float Forward;     // mm/s, negative = backward
        public float Turn;        // rad/s, positive = clockwise seen from the fly's back (to the right)
        public float Proboscis;   // 0..1 extension (MN9)
        public float Labellum;    // 0..1 lobe spreading
        public float Pump;        // 0..1 ingestion pumping
        public float Groom;       // 0..1 grooming vigor
        public GroomKind GroomKind;
        public float Alarm;       // 0..1 crouch and wing raise
        public float HeadYaw;     // -1..1
        public float HeadPitch;   // -1..1 (positive = look down)
        public float AntennaL, AntennaR; // -1..1
        public float Sleep;       // 0..1 sleep posture
        public bool Takeoff;      // giant fiber escape
        public bool FlyRequest;   // voluntary takeoff
        public Vector3 ThreatDirection; // world direction towards the threat, zero if unknown

        // flight guidance (virtual central complex / VNC) and brain steering
        public Vector3 FlightDirection;  // desired world direction, zero = keep going
        public float FlightSpeed;        // mm/s
        public bool HasLandingTarget;
        public Vector3 LandingPoint, LandingNormal;
        public bool LandAnywhere;        // descend onto whatever is below / ahead
        public float SteerTurn;          // rad/s from the steering descending neurons
        public float LandingReflex;      // 0..1 leg extension from looming-sensitive descending neurons
        public float WingExtendL, WingExtendR; // courtship song: one wing held out and vibrated
        public float Oviposit;           // 0..1 abdomen bent down, ovipositor probing the substrate
    }

    /// <summary>
    /// The "virtual ventral nerve cord": turns motor commands into movement. There is no VNC in the brain
    /// connectome, so walking rhythm, leg coordination (tripod gait), grooming patterns, adhesion to any surface
    /// and flight are generated here; every pose is produced by IK targets.
    /// The fly walks on any collider of the surface layer (ground, fruit, leaves, bark, upside down) by keeping
    /// its body frame aligned with the surface normal, and flies freely between surfaces.
    /// </summary>
    public sealed class FlyMotor : MonoBehaviour
    {
        public FlyRig Rig;
        public MotorCommand Command;
        public FlyMode Mode { get; private set; }
        public Vector3 BoundsCenter = Vector3.zero;
        public float BoundsRadius = 55f;
        public float Speed => IsAirborne ? _flyVel.magnitude : _v;
        public float TurnRate => IsAirborne ? _yawRate * Mathf.Deg2Rad : _w;
        public bool IsAirborne => _flight != FlightPhase.None && _flight != FlightPhase.Settle;
        public bool IsSettling => _flight == FlightPhase.Settle;
        public bool InTakeoff => _flight == FlightPhase.Launch;
        public float ProboscisExtension => _proboscis;
        public float TakeoffCooldown { get; private set; }
        public int Takeoffs { get; private set; }
        public int Landings { get; private set; }
        public float LastImpactSpeed { get; private set; }
        public bool LastTakeoffWasEscape { get; private set; }
        public float FlightTime => IsAirborne ? _flightT : 0f;
        /// <summary>Normal of the surface the fly stands on (world up while flying).</summary>
        public Vector3 SurfaceUp => IsAirborne ? Vector3.up : _up;
        public Vector3 Velocity => IsAirborne ? _flyVel : Rig.Root.forward * _v;
        public float HeightAboveGround { get; private set; }
        public float LegExtension => _legExtend;
        /// <summary>Collider the fly stands on (null in flight).</summary>
        public Collider CurrentSurface { get; private set; }
        /// <summary>Seconds without surface contact while walking (edge of a leaf, etc.).</summary>
        public float LostContact => _lostContact;

        /// <summary>World-space displacement of each antenna tip caused by air flow (set by the sensors).</summary>
        public readonly Vector3[] AntennaWind = new Vector3[2];
        /// <summary>Liquid surface under the head (set by the sensors).</summary>
        public bool HasLiquidUnderHead;
        public Vector3 LiquidUnderHead;

        const int SurfaceMask = FlyLayers.SurfaceMask;
        const float SwingFraction = 0.45f;
        public const float CruiseSpeed = 380f;

        sealed class LegState
        {
            public Vector3 Foot, LiftFrom;
            public bool Swinging, Grooming, Tucked;
            public float SwingT, SwingDuration, PrevPhase, ReturnT;
        }

        enum FlightPhase { None, Launch, Airborne, Settle }

        LegState[] _legs;
        Vector3 _up = Vector3.up;
        float _v, _w, _height = FlyRig.StandHeight, _pitch, _roll, _bob, _phase, _lostContact;
        float _groomPhase, _pumpPhase, _flapPhase, _breathPhase, _proboscis, _labellum, _alarm, _wingRaise, _sleep, _twitch;
        FlightPhase _flight;
        float _flightT, _launchDuration, _settleDuration, _legExtend;
        Vector3 _flyPos, _flyVel, _takeoffUp, _takeoffAway;
        float _flyYaw, _yawRate, _squeeze, _ovi;
        bool _crash;
        GroomKind _groomKind;

        public void Init(FlyRig rig)
        {
            Rig = rig;
            _up = rig.Root.up;
            _legs = new LegState[6];
            for (int i = 0; i < 6; i++)
            {
                _legs[i] = new LegState { Foot = HomeWorld(i) };
                _legs[i].LiftFrom = _legs[i].Foot;
            }
            for (int k = 0; k < 3; k++)
            {
                UpdateAppendages(0.02f);
                SolveAll();
            }
        }

        public bool IsPlanted(int leg) => !IsAirborne && !_legs[leg].Swinging && !_legs[leg].Grooming && _legs[leg].ReturnT <= 0;
        public Vector3 ClawPosition(int leg) => Rig.Legs[leg].Tarsus.Tip;
        public bool IsGroomingLeg(int leg) => _legs[leg].Grooming;
        public GroomKind CurrentGroomKind => _groomKind;

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt > 0)
            {
                TakeoffCooldown = Mathf.Max(0, TakeoffCooldown - dt);
                if (!IsAirborne && !IsSettling && TakeoffCooldown <= 0)
                {
                    if (Command.Takeoff) BeginTakeoff(true, Command.ThreatDirection);
                    else if (Command.FlyRequest) BeginTakeoff(false, Vector3.zero);
                }
                if (_flight != FlightPhase.None) UpdateFlight(dt);
                else UpdateGround(dt);
                UpdateAppendages(dt);
            }
            SolveAll();
        }

        // ------------------------------------------------------------------ surface frame

        static bool Probe(Vector3 from, Vector3 dir, float dist, out RaycastHit hit) =>
            Physics.Raycast(from, dir, out hit, dist, SurfaceMask, QueryTriggerInteraction.Ignore);

        /// <summary>Surface under a point along -up, averaged over a small footprint for a smooth normal.</summary>
        bool ProbeSurface(Vector3 pos, Vector3 up, Vector3 fwd, out Vector3 point, out Vector3 normal)
        {
            point = pos;
            normal = up;
            if (!Probe(pos + up * 1.0f, -up, 2.4f, out var center)) return false;
            point = center.point;
            CurrentSurface = center.collider;
            Vector3 right = Vector3.Cross(up, fwd);
            Vector3 n = center.normal * 2f;
            foreach (var off in new[] { fwd * 0.8f, -fwd * 0.8f, right * 0.6f, -right * 0.6f })
                if (Probe(pos + off + up * 1.0f, -up, 2.4f, out var h)) n += h.normal;
            normal = n.normalized;
            return true;
        }

        /// <summary>Foot position on the surface near a desired point (wraps around edges when needed).</summary>
        Vector3 SurfaceAt(Vector3 desired, Vector3 up)
        {
            if (Probe(desired + up * 1.1f, -up, 2.6f, out var hit)) return hit.point;
            // over an edge: reach down and back towards the body
            Vector3 toBody = Rig.Root.position - desired;
            Vector3 dir = (-up * 1.2f + Vector3.ProjectOnPlane(toBody, up).normalized).normalized;
            if (Probe(desired + up * 0.3f, dir, 2.0f, out hit)) return hit.point;
            return desired - up * 0.25f;
        }

        void Reorient(Vector3 newUp)
        {
            // parallel transport of the heading
            var q = Quaternion.FromToRotation(_up, newUp);
            _up = newUp.normalized;
            var fwd = Vector3.ProjectOnPlane(q * Rig.Root.forward, _up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Rig.Root.up, _up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, _up);
            Rig.Root.rotation = Quaternion.LookRotation(fwd.normalized, _up);
        }

        // ------------------------------------------------------------------ walking

        void UpdateGround(float dt)
        {
            var c = Command;
            Mode = c.Mode == FlyMode.Flight ? FlyMode.Walk : c.Mode;
            float targetV = c.Forward, targetW = c.Turn;
            bool stationary = Mode == FlyMode.Feed || Mode == FlyMode.Groom || Mode == FlyMode.Freeze || Mode == FlyMode.Rest || Mode == FlyMode.Sleep || Mode == FlyMode.Drink;
            if (stationary)
            {
                targetV = 0;
                targetW = 0;
            }

            // edge-of-range reflex
            Vector3 fromCenter = Rig.Root.position - BoundsCenter;
            fromCenter.y = 0;
            float r = fromCenter.magnitude;
            float margin = Mathf.Min(6f, BoundsRadius * 0.1f);
            if (r > BoundsRadius - margin && r > 1e-3f && Mathf.Abs(targetV) > 0.1f)
            {
                Vector3 fwdFlat = Vector3.ProjectOnPlane(Rig.Root.forward, Vector3.up) * Mathf.Sign(targetV);
                float outward = Vector3.Dot(fwdFlat.normalized, fromCenter / r);
                if (outward > -0.3f)
                {
                    float toRight = Vector3.Dot(Vector3.Cross(Rig.Root.forward, -fromCenter), _up) >= 0 ? 1 : -1;
                    targetW += toRight * 3f * Mathf.Clamp01((r - (BoundsRadius - margin)) / Mathf.Max(1f, margin * 0.7f)) * Mathf.Sign(targetV);
                }
                if (r > BoundsRadius && outward > 0) targetV *= 0.1f;
            }

            // backing out of a crevice (the body does not fit between two surfaces)
            if (_squeeze > 0)
            {
                _squeeze -= dt;
                targetV = -4f;
                targetW = 3.5f;
            }

            _v += (targetV - _v) * (1 - Mathf.Exp(-dt * 5f));
            _w += (targetW - _w) * (1 - Mathf.Exp(-dt * 8f));

            var root = Rig.Root;
            Vector3 oldPos = root.position, oldUp = _up;
            Quaternion oldRot = root.rotation;
            Vector3 fwd = Vector3.ProjectOnPlane(root.forward, _up).normalized;
            fwd = Quaternion.AngleAxis(_w * Mathf.Rad2Deg * dt, _up) * fwd;
            root.rotation = Quaternion.LookRotation(fwd, _up);
            Vector3 pos = root.position + fwd * (_v * dt);

            // a surface ahead (fruit, stone, wall): walk onto it
            float sign = _v >= 0 ? 1f : -1f;
            bool climbing = false;
            if (Mathf.Abs(_v) > 0.05f && Probe(pos + _up * 0.35f, fwd * sign, 0.9f + Mathf.Abs(_v) * dt, out var wall))
            {
                float angle = Vector3.Angle(wall.normal, _up);
                if (angle > 30f)
                {
                    climbing = true;
                    float closeness = 1f - wall.distance / (0.9f + Mathf.Abs(_v) * dt);
                    Reorient(Vector3.Slerp(_up, wall.normal, Mathf.Clamp01(closeness * dt * 14f + 0.02f)));
                    fwd = root.forward;
                }
            }

            if (ProbeSurface(pos, _up, fwd, out var point, out var normal))
            {
                _lostContact = 0;
                // follow the surface: tangential position exact, height smoothed
                float off = Vector3.Dot(point - pos, _up);
                pos += _up * off * (1 - Mathf.Exp(-dt * 30f)) + Vector3.ProjectOnPlane(point - pos, _up);
                Reorient(Vector3.Slerp(_up, normal, 1 - Mathf.Exp(-dt * 12f)));
            }
            else
            {
                // convex edge: wrap around it (top of a leaf to its underside, over the rim of a stone)
                Vector3 move = root.forward * sign;
                if (Probe(pos - _up * 0.7f + move * 0.2f, -move, 1.6f, out var under) ||
                    Probe(pos + move * 0.4f, -_up - move, 2.2f, out under))
                {
                    pos = under.point;
                    Reorient(Vector3.Slerp(_up, under.normal, 0.5f));
                    _lostContact = 0;
                }
                else
                {
                    _lostContact += dt;
                    if (_lostContact > 0.06f)
                    {
                        BeginFall();
                        return;
                    }
                }
            }
            // no room for the body: stay out of the gap and turn away
            if (!climbing && Probe(pos + _up * 0.3f, _up, 1.1f, out _) && !Probe(oldPos + oldUp * 0.3f, oldUp, 1.1f, out _))
            {
                pos = oldPos;
                _up = oldUp;
                root.rotation = oldRot;
                if (_squeeze <= 0) _squeeze = 0.6f;
            }
            root.position = pos;

            _alarm = Mathf.Lerp(_alarm, c.Alarm, 1 - Mathf.Exp(-dt * 6));
            _sleep = Mathf.Lerp(_sleep, Mode == FlyMode.Sleep ? Mathf.Max(0.6f, c.Sleep) : Mode == FlyMode.Rest ? 0.25f : 0f, 1 - Mathf.Exp(-dt * 1.5f));
            bool hindGroom = Mode == FlyMode.Groom && c.GroomKind == GroomKind.HindLegs;
            float crouch = _alarm * 0.16f + (Mode == FlyMode.Feed || Mode == FlyMode.Drink ? 0.12f : 0) + (Mode == FlyMode.Groom ? 0.03f : 0) + _sleep * 0.14f;
            float pitch = (Mode == FlyMode.Feed || Mode == FlyMode.Drink ? 9f : 0) + (Mode == FlyMode.Groom && !hindGroom ? -8f : 0) + (hindGroom ? 10f : 0) + _sleep * 4f;
            _height = Mathf.Lerp(_height, FlyRig.StandHeight - crouch + _bob, 1 - Mathf.Exp(-dt * 10));
            _pitch = Mathf.Lerp(_pitch, pitch, 1 - Mathf.Exp(-dt * 6));
            _roll = Mathf.Lerp(_roll, 0, 1 - Mathf.Exp(-dt * 8));
            Rig.Body.localPosition = new Vector3(0, _height, 0);
            Rig.Body.localRotation = Quaternion.Euler(_pitch, 0, _roll);
            HeightAboveGround = 0;

            Gait(dt);
            GroomingLegs(dt);
        }

        void Gait(float dt)
        {
            float gaitSpeed = Mathf.Abs(_v) + Mathf.Abs(_w) * 1.3f;
            bool moving = gaitSpeed > 0.3f;
            bool unsettled = false;
            for (int i = 0; i < 6; i++)
            {
                var l = _legs[i];
                if (l.Grooming) continue;
                if (l.Swinging || l.ReturnT > 0 || Vector3.ProjectOnPlane(l.Foot - HomeWorld(i), _up).magnitude > 0.3f) unsettled = true;
            }
            float stride = Mathf.Lerp(0.9f, 1.7f, Mathf.Clamp01(Mathf.Abs(_v) / 16f));
            float freq = moving ? Mathf.Clamp(gaitSpeed / stride, 3f, 11f) : (unsettled ? 5f : 0f);
            _phase = Mathf.Repeat(_phase + freq * dt, 1f);
            _bob = moving ? 0.03f * Mathf.Sin(_phase * Mathf.PI * 4) : 0;

            for (int i = 0; i < 6; i++)
            {
                var l = _legs[i];
                var leg = Rig.Legs[i];
                if (l.Grooming) continue;
                Vector3 home = HomeWorld(i);

                if (l.ReturnT > 0)
                {
                    // put a leg back on the surface after grooming or flight
                    l.ReturnT -= dt / 0.2f;
                    float t = 1 - Mathf.Clamp01(l.ReturnT);
                    l.Foot = Vector3.Lerp(l.LiftFrom, home, Smooth(t)) + _up * (0.25f * Mathf.Sin(t * Mathf.PI));
                    if (l.ReturnT <= 0) l.Foot = home;
                    l.PrevPhase = Mathf.Repeat(_phase + Group(leg) * 0.5f, 1f);
                    continue;
                }

                float lp = Mathf.Repeat(_phase + Group(leg) * 0.5f, 1f);
                bool entered = freq > 0 && lp < SwingFraction && (l.PrevPhase >= SwingFraction || lp < l.PrevPhase);
                l.PrevPhase = lp;

                if (!l.Swinging)
                {
                    float stretch = (l.Foot - home).magnitude;
                    bool start = entered && (moving || stretch > 0.25f);
                    if (stretch > 1.4f) start = true; // stumble step: foot left too far behind
                    if (start)
                    {
                        l.Swinging = true;
                        l.LiftFrom = l.Foot;
                        l.SwingT = 0;
                        l.SwingDuration = freq > 0 ? SwingFraction / freq : 0.1f;
                    }
                }

                if (l.Swinging)
                {
                    l.SwingT += dt / Mathf.Max(0.02f, l.SwingDuration);
                    float s = Mathf.Clamp01(l.SwingT);
                    float remain = (1 - s) * l.SwingDuration;
                    float stanceHalf = moving && freq > 0 ? 0.5f * (1 - SwingFraction) / freq : 0;
                    Vector3 land = PredictHome(i, remain + stanceHalf);
                    Vector3 p = Vector3.Lerp(l.LiftFrom, land, Smooth(s));
                    p += _up * (0.26f * Mathf.Sin(s * Mathf.PI));
                    l.Foot = p;
                    if (s >= 1)
                    {
                        l.Swinging = false;
                        l.Foot = land;
                    }
                }
            }
        }

        void GroomingLegs(float dt)
        {
            bool groom = Mode == FlyMode.Groom;
            if (groom && Command.GroomKind != _groomKind)
            {
                // switch the grooming program: release all grooming legs first
                for (int i = 0; i < 6; i++) ReleaseGroomingLeg(i);
                _groomKind = Command.GroomKind;
            }
            if (groom) _groomPhase += dt * (3f + 2.5f * Command.Groom);
            int pair = _groomKind == GroomKind.HindLegs ? 2 : 0;
            for (int i = 0; i < 6; i++)
            {
                var leg = Rig.Legs[i];
                var l = _legs[i];
                if (leg.Pair != pair)
                {
                    if (l.Grooming) ReleaseGroomingLeg(i);
                    continue;
                }
                if (groom && !l.Grooming && !l.Swinging)
                {
                    l.Grooming = true;
                    l.ReturnT = 0;
                }
                else if (!groom && l.Grooming) ReleaseGroomingLeg(i);
                if (!l.Grooming) continue;

                float a = _groomPhase * Mathf.PI * 2;
                int side = leg.Side;
                Vector3 target;
                switch (_groomKind)
                {
                    case GroomKind.HindLegs:
                    {
                        // hind legs sweep the abdomen and wings, then rub each other behind the body
                        float cycle = Mathf.Repeat(_groomPhase * 0.35f, 1f);
                        Vector3 local = cycle < 0.6f
                            ? new Vector3(side * (0.34f - 0.08f * Mathf.Sin(a)), 0.18f + 0.12f * Mathf.Cos(a), -0.8f - 0.45f * (0.5f + 0.5f * Mathf.Sin(a)))
                            : new Vector3(side * (0.03f + 0.05f * Mathf.Abs(Mathf.Sin(a * 2))), -0.25f, -1.75f + 0.08f * Mathf.Sin(a * 2));
                        target = Rig.Body.TransformPoint(local);
                        break;
                    }
                    case GroomKind.FrontLegs:
                    {
                        // front legs rub against each other under the head (cleaning tarsi and proboscis)
                        float slide = Mathf.Sin(a * 1.5f);
                        Vector3 local = new Vector3(side * (0.02f + 0.03f * Mathf.Abs(Mathf.Cos(a * 1.5f))), -0.38f + 0.04f * slide * side, 0.42f + 0.1f * slide * side);
                        target = Rig.HeadVisual.TransformPoint(local);
                        break;
                    }
                    default:
                    {
                        // antennal / eye grooming bout: loops over the antenna, strokes down over the compound eye, then the legs rub together
                        float cycle = Mathf.Repeat(_groomPhase * 0.3f, 1f);
                        Vector3 local;
                        bool eyes = _groomKind == GroomKind.Eyes;
                        if (cycle < (eyes ? 0.25f : 0.45f))
                            local = new Vector3(side * (0.12f + 0.05f * Mathf.Cos(a)), 0.02f + 0.16f * Mathf.Sin(a), 0.56f + 0.09f * Mathf.Cos(a));
                        else if (cycle < 0.78f)
                            local = new Vector3(side * (0.3f + 0.05f * Mathf.Sin(a)), 0.12f - 0.2f * Mathf.Cos(a), 0.22f + 0.14f * Mathf.Sin(a * 0.5f));
                        else
                            local = new Vector3(side * (0.02f + 0.06f * Mathf.Abs(Mathf.Sin(a * 2))), -0.32f, 0.46f + 0.04f * Mathf.Sin(a * 2));
                        target = Rig.HeadVisual.TransformPoint(local);
                        break;
                    }
                }
                l.Foot = Vector3.Lerp(l.Foot, target, 1 - Mathf.Exp(-dt * 25));
            }
        }

        void ReleaseGroomingLeg(int i)
        {
            var l = _legs[i];
            if (!l.Grooming) return;
            l.Grooming = false;
            l.LiftFrom = l.Foot;
            l.ReturnT = 1;
        }

        static int Group(LegRig leg) => (leg.Side < 0) == (leg.Pair != 1) ? 0 : 1; // tripods: L1 R2 L3 | R1 L2 R3

        Vector3 HomeWorld(int i) => SurfaceAt(Rig.Root.TransformPoint(Rig.Legs[i].HomeLocal), _up);

        Vector3 PredictHome(int i, float tau)
        {
            var root = Rig.Root;
            var yawF = Quaternion.AngleAxis(_w * Mathf.Rad2Deg * tau, _up);
            var yawMid = Quaternion.AngleAxis(_w * Mathf.Rad2Deg * tau * 0.5f, _up);
            Vector3 posF = root.position + (yawMid * root.forward) * (_v * tau);
            Vector3 p = posF + (yawF * root.rotation) * Rig.Legs[i].HomeLocal;
            return SurfaceAt(p, _up);
        }

        // ------------------------------------------------------------------ flight

        void BeginTakeoff(bool escape, Vector3 threatDirection)
        {
            CurrentSurface = null;
            LastTakeoffWasEscape = escape;
            _takeoffUp = _up;
            Vector3 away = escape ? -threatDirection : Rig.Root.forward;
            away = Vector3.ProjectOnPlane(away, _up);
            if (away.sqrMagnitude < 1e-4f) away = Rig.Root.forward;
            _takeoffAway = Quaternion.AngleAxis(Random.Range(-30f, 30f), _up) * away.normalized;
            _flyPos = Rig.Body.position;
            _flyVel = Vector3.zero;
            Vector3 flat = Vector3.ProjectOnPlane(_takeoffAway + _takeoffUp * 0.2f, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(Rig.Root.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            _flyYaw = Quaternion.LookRotation(flat).eulerAngles.y;
            _yawRate = 0;
            _flightT = 0;
            _launchDuration = escape ? 0.05f : 0.3f;
            _flight = FlightPhase.Launch;
            Mode = FlyMode.Flight;
            Takeoffs++;
            for (int i = 0; i < 6; i++)
            {
                _legs[i].Grooming = false;
                _legs[i].Swinging = false;
                _legs[i].ReturnT = 0;
            }
        }

        void BeginFall()
        {
            // lost the surface (walked off an edge): flies fall into flight within a few milliseconds
            BeginTakeoff(false, Vector3.zero);
            _flight = FlightPhase.Airborne;
            _flyVel = Rig.Root.forward * _v + Vector3.down * 40f;
            Takeoffs--; // not a takeoff
        }

        void UpdateFlight(float dt)
        {
            Mode = FlyMode.Flight;
            _flightT += dt;
            var root = Rig.Root;
            var body = Rig.Body;

            if (_flight == FlightPhase.Launch)
            {
                float t = Mathf.Clamp01(_flightT / _launchDuration);
                // voluntary: wings raise first, then the middle legs push; escape: the jump comes first
                float wingT = LastTakeoffWasEscape ? t : Mathf.Clamp01(t / 0.55f);
                float jumpT = LastTakeoffWasEscape ? t : Mathf.Clamp01((t - 0.5f) / 0.5f);
                _wingRaise = wingT;
                _height = FlyRig.StandHeight + 0.9f * Smooth(jumpT);
                _pitch = Mathf.Lerp(_pitch, LastTakeoffWasEscape ? -25f : -12f, t);
                body.localPosition = new Vector3(0, _height, 0);
                body.localRotation = Quaternion.Euler(_pitch, 0, 0);
                if (_flightT >= _launchDuration)
                {
                    _flight = FlightPhase.Airborne;
                    _flyPos = body.position;
                    float up = LastTakeoffWasEscape ? 420f : 220f;
                    float out_ = LastTakeoffWasEscape ? 320f : 140f;
                    _flyVel = _takeoffUp * up + _takeoffAway * out_ + Vector3.up * 60f;
                    for (int i = 0; i < 6; i++) _legs[i].Tucked = true;
                    root.rotation = Quaternion.Euler(0, _flyYaw, 0);
                    _up = Vector3.up;
                }
                return;
            }

            if (_flight == FlightPhase.Airborne)
            {
                Airborne(dt);
                return;
            }

            // settle after landing
            float s = Mathf.Clamp01(_flightT / _settleDuration);
            _wingRaise = 1 - Smooth(s);
            _legExtend = Mathf.Lerp(_legExtend, 0, s);
            float wobble = _crash ? Mathf.Sin(_flightT * 40f) * 18f * (1 - s) : 0f;
            _pitch = Mathf.Lerp(_pitch, 0, s);
            _roll = wobble;
            _height = Mathf.Lerp(_height, FlyRig.StandHeight, s);
            body.localPosition = new Vector3(0, _height, 0);
            body.localRotation = Quaternion.Euler(_pitch, 0, _roll);
            for (int i = 0; i < 6; i++) _legs[i].Foot = Vector3.Lerp(_legs[i].Foot, HomeWorld(i), 1 - Mathf.Exp(-dt * 20));
            if (s >= 1)
            {
                _flight = FlightPhase.None;
                _v = 0;
                _w = 0;
                _crash = false;
                TakeoffCooldown = LastTakeoffWasEscape ? 1.5f : 0.8f;
            }
        }

        void Airborne(float dt)
        {
            var c = Command;
            var root = Rig.Root;
            var body = Rig.Body;

            // ground clearance
            HeightAboveGround = Probe(_flyPos, Vector3.down, 3000f, out var below) ? below.distance : 3000f;

            // guidance; without any (reflex-only fly) the flight ends on the nearest surface
            bool unguided = !c.HasLandingTarget && !c.LandAnywhere && c.FlightDirection.sqrMagnitude < 1e-4f;
            if (unguided && _flightT > 1.2f) c.LandAnywhere = true;
            Vector3 fromBounds = _flyPos - BoundsCenter;
            fromBounds.y = 0;
            bool outside = fromBounds.magnitude > BoundsRadius;
            if (outside && !c.HasLandingTarget)
            {
                c.LandAnywhere = false;
                c.FlightDirection = (-fromBounds.normalized + Vector3.down * 0.2f).normalized;
                if (unguided) c.FlightSpeed = 250f;
            }
            Vector3 goal;
            float speed;
            float landingProximity = 0;
            if (c.HasLandingTarget)
            {
                Vector3 aim = c.LandingPoint + c.LandingNormal * 1.0f;
                Vector3 to = aim - _flyPos;
                float dist = to.magnitude;
                // approach from the outside of the surface
                if (Vector3.Dot(-to, c.LandingNormal) < 0.3f * dist && dist > 12f) to += c.LandingNormal * Mathf.Min(dist * 0.4f, 40f);
                goal = to.normalized;
                speed = Mathf.Clamp(dist * 4f, 70f, c.FlightSpeed > 0 ? c.FlightSpeed : CruiseSpeed);
                landingProximity = Mathf.Clamp01(1f - dist / 60f);
            }
            else if (c.LandAnywhere)
            {
                Vector3 heading = Quaternion.Euler(0, _flyYaw, 0) * Vector3.forward;
                goal = (heading * 0.55f + Vector3.down * 0.85f).normalized;
                speed = Mathf.Clamp(HeightAboveGround * 3f, 80f, 260f);
                landingProximity = Mathf.Clamp01(1f - HeightAboveGround / 40f);
            }
            else if (c.FlightDirection.sqrMagnitude > 1e-4f)
            {
                goal = c.FlightDirection.normalized;
                speed = c.FlightSpeed > 0 ? c.FlightSpeed : CruiseSpeed;
            }
            else
            {
                goal = Quaternion.Euler(0, _flyYaw, 0) * Vector3.forward;
                speed = c.FlightSpeed > 0 ? c.FlightSpeed : CruiseSpeed * 0.7f;
            }

            // collision avoidance: pull up over the ground, veer around obstacles that are not the landing site
            if (!c.LandAnywhere && Physics.SphereCast(_flyPos, 1.2f, _flyVel.sqrMagnitude > 1 ? _flyVel.normalized : goal, out var ahead,
                    Mathf.Max(20f, _flyVel.magnitude * 0.35f), SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                bool isTarget = c.HasLandingTarget && Vector3.Distance(ahead.point, c.LandingPoint) < 25f;
                if (!isTarget)
                {
                    float urgency = 1f - ahead.distance / Mathf.Max(20f, _flyVel.magnitude * 0.35f);
                    Vector3 avoid = Vector3.ProjectOnPlane(ahead.normal, Vector3.up) + Vector3.up * (ahead.normal.y > 0.5f ? 1.5f : 0.6f);
                    goal = (goal + avoid.normalized * (1.5f * urgency + 0.3f)).normalized;
                }
            }
            if (!c.HasLandingTarget && !c.LandAnywhere && HeightAboveGround < 15f) goal = (goal + Vector3.up * (15f - HeightAboveGround) / 10f).normalized;

            // heading control with saccade-like turns
            Vector3 goalFlat = Vector3.ProjectOnPlane(goal, Vector3.up);
            float desiredYaw = goalFlat.sqrMagnitude > 1e-4f ? Mathf.Atan2(goalFlat.x, goalFlat.z) * Mathf.Rad2Deg : _flyYaw;
            float err = Mathf.DeltaAngle(_flyYaw, desiredYaw);
            float steer = c.SteerTurn * Mathf.Rad2Deg;
            float maxRate = Mathf.Abs(err) > 60f || Mathf.Abs(steer) > 200f ? 1400f : 450f;
            float targetRate = Mathf.Clamp(err * 7f + steer, -maxRate, maxRate);
            _yawRate = Mathf.Lerp(_yawRate, targetRate, 1 - Mathf.Exp(-dt * 18f));
            _flyYaw += _yawRate * dt;

            Vector3 heading3 = Quaternion.Euler(0, _flyYaw, 0) * Vector3.forward;
            float align = Mathf.Clamp01(Mathf.Cos(err * Mathf.Deg2Rad));
            float horizontal = speed * Mathf.Sqrt(Mathf.Max(0, 1 - goal.y * goal.y)) * (0.35f + 0.65f * align);
            float vertical = Mathf.Clamp(goal.y * speed, -300f, 300f);
            Vector3 air = heading3 * horizontal + Vector3.up * vertical;
            Vector3 wind = WindField.At(_flyPos, HeightAboveGround);
            float drift = c.HasLandingTarget ? 0.35f : 0.8f; // flies compensate drift when approaching a target
            Vector3 targetVel = air + Vector3.ClampMagnitude(wind, 900f) * drift;
            _flyVel = Vector3.Lerp(_flyVel, targetVel, 1 - Mathf.Exp(-dt * (c.HasLandingTarget ? 6f : 4f)));

            // touchdown on any surface in the path
            Vector3 move = _flyVel * dt;
            float moveLen = move.magnitude;
            if (moveLen > 1e-5f && Physics.SphereCast(_flyPos, 0.6f, move / moveLen, out var hit, moveLen + 0.7f, SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                Touchdown(hit.point, hit.normal);
                return;
            }
            if (HeightAboveGround < 0.9f && below.collider != null)
            {
                Touchdown(below.point, below.normal);
                return;
            }
            _flyPos += move;

            // body attitude: pitched up when slow, banked into turns
            float spd = _flyVel.magnitude;
            float pitch = Mathf.Lerp(-38f, -8f, Mathf.Clamp01(spd / 450f));
            _pitch = Mathf.Lerp(_pitch, pitch, 1 - Mathf.Exp(-dt * 6));
            _roll = Mathf.Lerp(_roll, Mathf.Clamp(-_yawRate * 0.06f, -40f, 40f), 1 - Mathf.Exp(-dt * 10));
            _height = FlyRig.StandHeight;
            root.SetPositionAndRotation(_flyPos - Vector3.up * _height, Quaternion.Euler(0, _flyYaw, 0));
            body.localPosition = new Vector3(0, _height, 0);
            body.localRotation = Quaternion.Euler(_pitch, 0, _roll);
            _flapPhase += dt * Mathf.PI * 2 * 218f;
            _wingRaise = 1;

            // legs: tucked, extended forward and down for landing (brain looming reflex or close to the target)
            float extend = Mathf.Max(c.LandingReflex, landingProximity);
            _legExtend = Mathf.Lerp(_legExtend, extend, 1 - Mathf.Exp(-dt * 14f));
            for (int i = 0; i < 6; i++)
            {
                var home = Rig.Legs[i].HomeLocal;
                Vector3 tucked = home * 0.4f + new Vector3(0, -0.75f, 0);
                Vector3 extended = new Vector3(home.x * 0.8f, -1.15f, home.z * 0.75f + 0.35f);
                _legs[i].Foot = body.TransformPoint(Vector3.Lerp(tucked, extended, _legExtend));
                _legs[i].Tucked = _legExtend < 0.5f;
            }
        }

        void Touchdown(Vector3 point, Vector3 normal)
        {
            LastImpactSpeed = _flyVel.magnitude;
            _crash = _legExtend < 0.35f && LastImpactSpeed > 180f;
            var root = Rig.Root;
            Vector3 heading = Quaternion.Euler(0, _flyYaw, 0) * Vector3.forward;
            _up = normal.normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(heading, _up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.up, _up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, _up);
            root.SetPositionAndRotation(point, Quaternion.LookRotation(fwd.normalized, _up));
            _height = FlyRig.StandHeight + 0.25f;
            _flight = FlightPhase.Settle;
            _flightT = 0;
            _settleDuration = _crash ? 0.55f : 0.3f;
            _flyVel = Vector3.zero;
            _yawRate = 0;
            _v = 0;
            _w = 0;
            _lostContact = 0;
            Landings++;
            for (int i = 0; i < 6; i++)
            {
                _legs[i].Tucked = false;
                _legs[i].Swinging = false;
                _legs[i].Grooming = false;
                _legs[i].ReturnT = 0;
                _legs[i].LiftFrom = _legs[i].Foot;
            }
        }

        // ------------------------------------------------------------------ head, antennae, proboscis, wings, abdomen

        void UpdateAppendages(float dt)
        {
            var c = Command;
            var body = Rig.Body;
            bool flying = IsAirborne;

            // head
            _twitch -= dt;
            float headPitch = (Mode == FlyMode.Feed || Mode == FlyMode.Drink ? 22f : 0) + (Mode == FlyMode.Groom && _groomKind != GroomKind.HindLegs ? 28f : 0) + (flying ? -10f : 0)
                              + _sleep * 14f + Mathf.Clamp(c.HeadPitch, -1, 1) * 15f;
            float headYaw = Mathf.Clamp(c.HeadYaw, -1, 1) * 25f;
            var neck = Rig.Head.Bones[0];
            Rig.Head.Target = neck.position + body.rotation * Quaternion.Euler(headPitch, headYaw, 0) * Vector3.forward * 0.3f;
            Rig.Head.RollUp = body.up;
            Rig.Head.Solve();
            var head = Rig.HeadVisual;

            // antennae: wind deflection, active movements, lowered in sleep
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                float drive = s == 0 ? c.AntennaL : c.AntennaR;
                Vector3 target = head.TransformPoint(new Vector3(side * 0.13f, 0.0f, 0.66f))
                                 + head.TransformDirection(new Vector3(0, 0.05f, 0.03f)) * Mathf.Clamp(drive, -1, 1)
                                 + head.TransformDirection(new Vector3(0, -0.08f, -0.03f)) * _sleep
                                 + Vector3.ClampMagnitude(AntennaWind[s], 0.25f);
                if (flying) target += head.TransformDirection(new Vector3(side * 0.03f, 0.03f, -0.02f));
                if (Mode == FlyMode.Groom && _groomKind <= GroomKind.Eyes) target += head.TransformDirection(new Vector3(-side * 0.02f, -0.12f, -0.04f));
                Rig.Antennae[s].Target = target;
                Rig.Antennae[s].SetPole(1, head.up + head.forward * 0.5f);
            }

            // proboscis extension (MN9 or drinking) reaches for the substrate or the surface of a liquid
            float ext = flying ? 0 : Mathf.Clamp01(c.Proboscis);
            if (Mode == FlyMode.Groom && _groomKind == GroomKind.FrontLegs) ext = Mathf.Max(ext, 0.35f); // proboscis is wiped
            _proboscis = Mathf.Lerp(_proboscis, ext, 1 - Mathf.Exp(-dt * 8));
            _labellum = Mathf.Lerp(_labellum, flying ? 0 : c.Labellum, 1 - Mathf.Exp(-dt * 6));
            _pumpPhase += dt * Mathf.PI * 2 * 4.5f;
            Vector3 retracted = head.TransformPoint(new Vector3(0, -0.36f, 0.1f));
            Vector3 up = flying ? Vector3.up : _up;
            Vector3 reachStart = head.position + body.forward * 0.3f;
            Vector3 reach = reachStart - up * 0.85f;
            if (Probe(reachStart + up * 0.2f, -up, 1.3f, out var sub)) reach = sub.point + up * 0.02f;
            if (HasLiquidUnderHead && Vector3.Dot(LiquidUnderHead - reach, up) > 0) reach = LiquidUnderHead + up * 0.02f;
            if (Vector3.Distance(reach, head.position) > 0.9f) reach = head.position + (reach - head.position).normalized * 0.9f;
            Vector3 pTarget = Vector3.Lerp(retracted, reach, Smooth(_proboscis));
            pTarget += body.up * (0.04f * Mathf.Sin(_pumpPhase) * c.Pump * _proboscis);
            Rig.Proboscis.Target = pTarget;
            Rig.Proboscis.SetPole(1, head.forward - head.up * 0.2f);
            Rig.Proboscis.SetPole(2, -head.forward);

            // wings: folded over the abdomen, raised when alarmed, beating at ~220 Hz in flight (strobed), blur fan
            float raise = Mathf.Max(_alarm * 0.6f, _wingRaise);
            bool beating = _flight == FlightPhase.Airborne;
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                var wing = Rig.Wings[s];
                if (beating)
                {
                    float phi = 70f * Mathf.Cos(_flapPhase);
                    float elev = 12f * Mathf.Sin(_flapPhase * 2) + 8f;
                    Vector3 dir = body.rotation * (Quaternion.Euler(0, side * -phi, side * elev) * new Vector3(side, 0, 0));
                    wing.Target = wing.Root + dir * 2f;
                    float pron = Mathf.Sign(-Mathf.Sin(_flapPhase)) * side * 40f;
                    wing.RollUp = Quaternion.AngleAxis(pron, dir) * body.up;
                }
                else
                {
                    Vector3 folded = new Vector3(side * 0.2f, -0.05f, -1f);
                    Vector3 upPose = new Vector3(side * 0.7f, 0.9f, -0.35f);
                    Vector3 dir = body.TransformDirection(Vector3.Slerp(folded.normalized, upPose.normalized, raise));
                    float extend = s == 0 ? c.WingExtendL : c.WingExtendR;
                    if (extend > 0.01f)
                    {
                        float buzz = Mathf.Sin(Time.time * 2 * Mathf.PI * 170f) * 4f * extend;
                        var outPose = Quaternion.Euler(buzz, 0, 0) * new Vector3(side * 0.95f, 0.05f, -0.3f).normalized;
                        dir = body.TransformDirection(Vector3.Slerp(folded.normalized, outPose, extend));
                    }
                    // hind-leg grooming lifts and spreads the wings a little
                    if (Mode == FlyMode.Groom && _groomKind == GroomKind.HindLegs) dir = body.TransformDirection(Vector3.Slerp(folded.normalized, new Vector3(side * 0.6f, 0.3f, -0.8f).normalized, 0.35f));
                    wing.Target = wing.Root + dir * 2f;
                    wing.RollUp = body.up;
                }
                if (Rig.WingBlur[s] != null)
                {
                    Rig.WingBlur[s].gameObject.SetActive(beating);
                }

                var haltere = Rig.Halteres[s];
                float osc = beating ? Mathf.Sin(_flapPhase + Mathf.PI) * 0.8f : 0;
                haltere.Target = haltere.Root + body.TransformDirection(new Vector3(side * 0.8f, -0.2f + osc, -0.6f)) * 0.22f;
                haltere.RollUp = body.up;
            }

            // abdomen: breathing (slower in sleep), pumping while feeding, lifted in flight
            _breathPhase += dt * Mathf.PI * 2 * Mathf.Lerp(0.8f, 0.35f, _sleep);
            float abdLift = 0.03f * Mathf.Sin(_breathPhase) + (Mode == FlyMode.Feed || Mode == FlyMode.Drink ? -0.05f * Mathf.Sin(_pumpPhase) * c.Pump : 0) + (flying ? 0.25f : 0) - _sleep * 0.08f;
            if (Mode == FlyMode.Groom && _groomKind == GroomKind.HindLegs) abdLift -= 0.12f;
            float ovi = flying ? 0f : Mathf.Clamp01(c.Oviposit);
            _ovi = Mathf.Lerp(_ovi, ovi, 1 - Mathf.Exp(-dt * 5f));
            abdLift -= _ovi * (0.5f + 0.08f * Mathf.Sin(Time.time * 9f));
            Rig.Abdomen.Target = body.TransformPoint(new Vector3(0, -0.2f + abdLift, -1.42f + _ovi * 0.3f));
            Rig.Abdomen.SetPole(1, body.up);
        }

        void SolveAll()
        {
            if (Rig == null) return;
            var body = Rig.Body;
            // head first: antennae and proboscis hang from it
            Rig.Head.Solve();
            foreach (var a in Rig.Antennae) a.Solve();
            Rig.Proboscis.Solve();
            var lab = Rig.Proboscis.Bones[2];
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                var lobe = Rig.LabellumLobes[s];
                lobe.Target = lobe.Root + (lab.forward * 0.12f + lab.right * side * (0.01f + 0.08f * _labellum));
                lobe.RollUp = lab.up;
                lobe.Solve();
            }

            if (_legs != null)
                for (int i = 0; i < 6; i++)
                {
                    var leg = Rig.Legs[i];
                    var l = _legs[i];
                    Vector3 tip = l.Foot;
                    Vector3 root = leg.Chain.Root;
                    Vector3 ankle;
                    if (l.Grooming)
                    {
                        Vector3 reachDir = (tip - root).normalized;
                        ankle = tip - reachDir * leg.TarsusLength * 0.8f - body.up * 0.15f;
                    }
                    else
                    {
                        Vector3 outward = Vector3.ProjectOnPlane(tip - root, body.up);
                        if (outward.sqrMagnitude < 1e-6f) outward = body.right * leg.Side;
                        outward.Normalize();
                        float angle = (l.Tucked ? 60f : 18f) * Mathf.Deg2Rad;
                        ankle = tip - outward * leg.TarsusLength * Mathf.Cos(angle) + body.up * leg.TarsusLength * Mathf.Sin(angle);
                    }
                    float tilt = leg.Pair == 0 ? -0.5f : leg.Pair == 2 ? 0.6f : 0f;
                    leg.Chain.Target = ankle;
                    leg.Chain.SetPole(1, body.TransformDirection(new Vector3(leg.Side * 0.35f, -1f, 0)));
                    leg.Chain.SetPole(2, body.TransformDirection(new Vector3(leg.Side * 1f, 0.45f, tilt)));
                    leg.Chain.Solve();
                    leg.Tarsus.Target = tip;
                    leg.Tarsus.RollUp = body.up;
                    leg.Tarsus.Solve();
                }

            foreach (var w in Rig.Wings) w.Solve();
            foreach (var h in Rig.Halteres) h.Solve();
            Rig.Abdomen.Solve();
        }

        // ------------------------------------------------------------------ utilities

        /// <summary>Height of the surface below a point (world down), or 0.</summary>
        public static float GroundHeight(Vector3 p)
        {
            return Physics.Raycast(new Vector3(p.x, p.y + 40f, p.z), Vector3.down, out var hit, 400f, SurfaceMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : 0f;
        }

        /// <summary>Place the fly on a surface with a given pose (used at spawn).</summary>
        public void PlaceAt(Vector3 point, Quaternion rotation)
        {
            _flight = FlightPhase.None;
            Rig.Root.SetPositionAndRotation(point, rotation);
            _up = rotation * Vector3.up;
            for (int i = 0; i < 6; i++)
            {
                _legs[i].Foot = HomeWorld(i);
                _legs[i].Swinging = _legs[i].Grooming = _legs[i].Tucked = false;
                _legs[i].ReturnT = 0;
            }
        }

        static float Smooth(float t) => t * t * (3 - 2 * t);
    }
}
