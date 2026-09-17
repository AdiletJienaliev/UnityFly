using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FlyBrain
{
    public enum Activity { Rest, Explore, Forage, LocalSearch, SeekWater, Drink, Groom, Sleep, Flee, Relocate, Roost, ReturnToRange, LayEggs, Reject }

    /// <summary>What the connectome is doing, decoded from descending / motor neuron rates (see FlyNervousSystem).</summary>
    public struct BrainDrive
    {
        public bool Ready;
        public bool Escape;       // giant fiber
        public float Feed;        // MN9
        public float Groom;       // aDN1 / aDN2
        public float Backward;    // MDN
        public float Freeze;      // DNp02/04/06/11
        public float TurnHz;      // DNa02 / DNa01 asymmetry (+ right)
        public bool Engaged => Escape || Feed > 0.1f || Groom > 0.12f || Backward > 0.12f || Freeze > 0.2f;
    }

    /// <summary>Output of the mind: what the body should do when the brain is not driving a reflex.</summary>
    public struct Intent
    {
        public FlyMode Posture;          // Walk, Rest, Sleep, Drink, Groom
        public GroomKind Groom;
        public float Forward, Turn;
        public float HeadYaw, HeadPitch, AntennaL, AntennaR;
        public float Probe;              // brief proboscis extension (tasting the surface)
        public float Oviposit;           // ovipositor probing
        public float WingFlick;          // brief wing raise (rejecting a male)
        public float SleepDepth;
        public bool Fly;
        public Vector3 FlightDirection;
        public float FlightSpeed;
        public bool HasLandingTarget, LandAnywhere;
        public Vector3 LandingPoint, LandingNormal;
    }

    /// <summary>
    /// Motivation and navigation outside the connectome ("virtual VNC and central complex"): chooses what to do from
    /// hunger, thirst, sleep pressure, the circadian clock and fear; tracks odor plumes (upwind surge, crosswind
    /// casting), humidity gradients and remembered places; walks, rests, sleeps, grooms and decides to fly and where
    /// to land. Reflexes decoded from the brain (feeding, grooming, escape, backing up, freezing, turning towards
    /// moving objects) always take precedence, and brain steering is added on top of the navigation.
    /// </summary>
    public sealed class FlyMind
    {
        public Activity Current { get; private set; } = Activity.Explore;
        public float ActivityTime { get; private set; }
        public string Detail { get; private set; } = "";
        public Vector3? FoodMemory { get; private set; }
        public Vector3? WaterMemory { get; private set; }
        /// <summary>Where the smell of fermentation was strongest recently.</summary>
        public Vector3? OdorMemory { get; private set; }
        float _odorMemoryStrength, _odorMemoryAge;
        public int MealsRemembered { get; private set; }

        readonly FlyRig _rig;
        readonly FlyMotor _motor;
        readonly FlyPhysiology _phys;

        Intent _intent;
        float _decideTimer, _wiggle, _wiggleTarget, _wiggleTimer, _boutTimer, _speed;
        bool _walking = true;
        float _headTimer, _headYaw, _headPitch, _antTimer, _antL, _antR, _probeTimer, _probe;
        float _groomUrge = 0.2f, _flightUrge;
        float _odorAvgPrev, _odorLost, _castTimer, _castSign = 1, _castPeriod = 0.5f;
        float _landingAge, _flightDecisionTimer;
        Vector3 _threatFrom;
        bool _wasFeeding;
        float _feedingTime, _afterMealTimer;
        float _stuckTimer, _nearBest, _nearStall;
        Vector3 _lastPos;

        public FlyMind(FlyRig rig, FlyMotor motor, FlyPhysiology phys)
        {
            _rig = rig;
            _motor = motor;
            _phys = phys;
            _speed = Random.Range(7f, 12f);
            _boutTimer = Random.Range(2f, 5f);
            _lastPos = rig.Root.position;
        }

        public static string Name(Activity a) => a switch
        {
            Activity.Rest => "Отдых",
            Activity.Explore => "Исследует",
            Activity.Forage => "Ищет еду по запаху",
            Activity.LocalSearch => "Локальный поиск у еды",
            Activity.SeekWater => "Ищет воду",
            Activity.Drink => "Пьёт",
            Activity.Groom => "Чистится",
            Activity.Sleep => "Спит",
            Activity.Flee => "Бегство",
            Activity.Relocate => "Перелёт",
            Activity.Roost => "Ищет укрытие на ночь",
            Activity.ReturnToRange => "Возвращается",
            Activity.LayEggs => "Откладывает яйца",
            Activity.Reject => "Отвергает ухаживание самца",
            _ => a.ToString(),
        };

        void Switch(Activity a, string detail = "")
        {
            if (a != Current)
            {
                _nearBest = 0;
                _nearStall = 0;
                Current = a;
                ActivityTime = 0;
                _landingAge = 0;
                _intent.HasLandingTarget = false;
            }
            Detail = detail;
        }

        /// <summary>Called by the nervous system when the giant fiber launched an escape.</summary>
        public void OnEscape(Vector3 threatDirection)
        {
            _threatFrom = _rig.Body.position + threatDirection * 50f;
            _phys.Startle(1f);
            _phys.Asleep = false;
            Switch(Activity.Flee, "после команды Giant Fiber");
        }

        public void Startle(float amount, Vector3 direction)
        {
            _phys.Startle(amount);
            if (amount > 0.3f && _phys.Asleep)
            {
                _phys.Asleep = false;
                Switch(Activity.Rest, "разбужена");
            }
            if (direction.sqrMagnitude > 0.01f) _threatFrom = _rig.Body.position + direction.normalized * 50f;
        }

        public Intent Update(float dt, in SensoryState s, in BrainDrive brain, FlyEnvironment env)
        {
            ActivityTime += dt;
            float hour = env != null && env.Clock != null ? env.Clock.Hour : 12f;
            float light = env != null ? env.LightLevel : 1f;
            IdleMovements(dt);

            // remember what the brain decided
            bool feeding = brain.Ready && brain.Feed > 0.1f && !_motor.IsAirborne;
            if (feeding)
            {
                _feedingTime += dt;
                if (s.TouchedFood != null && s.TouchedFood.Taste == Taste.Sugar && _feedingTime > 1f)
                {
                    FoodMemory = s.TouchedFood.transform.position;
                    MealsRemembered++;
                }
                _phys.Asleep = false;
            }
            else if (_wasFeeding && _feedingTime > 2f && Current != Activity.Flee && !_motor.IsAirborne && !_motor.InTakeoff)
            {
                _afterMealTimer = Random.Range(8f, 18f);
                if (Random.value < 0.55f)
                {
                    Switch(Activity.Groom, "после еды: чистит хоботок и лапки");
                    _intent.Groom = GroomKind.FrontLegs;
                }
                else Switch(Activity.LocalSearch, "после еды");
                _groomUrge = 0.1f;
            }
            if (!feeding) _feedingTime = 0;
            _wasFeeding = feeding;
            if (s.LabellumInWater || s.LegsInWater) WaterMemory = _rig.Root.position;
            float odorNow = OdorField.NearField(_rig.HeadVisual.position);
            _odorMemoryAge += dt;
            if (odorNow > 0.25f && (odorNow > _odorMemoryStrength * 0.9f || _odorMemoryAge > 120f))
            {
                OdorMemory = _rig.HeadVisual.position;
                _odorMemoryStrength = odorNow;
                _odorMemoryAge = 0;
            }

            _intent.Probe = _probe;
            _intent.Oviposit = 0;
            _intent.WingFlick = 0;
            _intent.HeadYaw = _headYaw;
            _intent.HeadPitch = _headPitch;
            _intent.AntennaL = _antL;
            _intent.AntennaR = _antR;
            _intent.Fly = false;

            if (_motor.IsAirborne || _motor.InTakeoff)
            {
                Fly(dt, s, light, env);
                _intent.Posture = FlyMode.Walk;
                return _intent;
            }
            _intent.HasLandingTarget = false;
            _intent.LandAnywhere = false;
            if (Current == Activity.Flee && ActivityTime > 0.8f && !_motor.IsSettling) Switch(Activity.Rest, "замерла после приземления");

            if (brain.Ready && brain.Engaged && !brain.Escape)
            {
                // a reflex runs the body; the mind only keeps its bookkeeping
                if (Current == Activity.Sleep) Switch(Activity.Rest, "разбужена стимулом");
                _phys.Asleep = false;
                _intent.Posture = FlyMode.Walk;
                _intent.Forward = 0;
                _intent.Turn = 0;
                return _intent;
            }

            Courtship(dt);
            Decide(dt, s, hour, light, env);
            Ground(dt, s, hour, light, env);
            return _intent;
        }

        // ------------------------------------------------------------------ action selection

        void Decide(float dt, in SensoryState s, float hour, float light, FlyEnvironment env)
        {
            float hunger = _phys.Hunger, thirst = _phys.Thirst, fear = _phys.Fear;
            float sleepDrive = _phys.SleepDrive(hour);
            float odor = (s.OdorL + s.OdorR) * 0.5f;
            _groomUrge = Mathf.Min(0.75f, _groomUrge + dt * 0.012f);
            _afterMealTimer -= dt;

            // sleep
            if (Current == Activity.Sleep)
            {
                _phys.Asleep = true;
                bool wake = sleepDrive < 0.3f || fear > 0.35f || hunger > 0.9f || thirst > 0.9f;
                if (wake)
                {
                    _phys.Asleep = false;
                    Switch(Activity.Groom, "проснулась");
                    _intent.Groom = Random.value < 0.5f ? GroomKind.HindLegs : GroomKind.FrontLegs;
                }
                return;
            }
            _phys.Asleep = false;

            // out of range: head back
            if (env != null)
            {
                Vector3 d = _rig.Root.position - env.Center;
                d.y = 0;
                if (d.magnitude > env.Radius && Current != Activity.ReturnToRange) Switch(Activity.ReturnToRange, "край мира");
                if (Current == Activity.ReturnToRange && d.magnitude < env.Radius * 0.8f) Switch(Activity.Explore);
            }

            _decideTimer -= dt;
            if (_decideTimer > 0) return;
            _decideTimer = 0.5f;

            bool minBout = ActivityTime < 3f;
            if (Current == Activity.Drink && (thirst < 0.05f || ActivityTime > 14f || !(s.LabellumInWater || s.LegsInWater)))
            {
                Switch(Activity.Groom, "напилась");
                _intent.Groom = GroomKind.FrontLegs;
                return;
            }
            if (Current == Activity.LocalSearch && _afterMealTimer > 0 && hunger > 0.05f) return;
            if (Current == Activity.Groom && ActivityTime < 5f) return;
            if (Current == Activity.Groom) _groomUrge = 0;
            if (Current == Activity.Roost && ActivityTime < 25f) return;
            if (Current == Activity.ReturnToRange) return;
            if (Current == Activity.Flee) return;
            if (Current == Activity.LayEggs || Current == Activity.Reject) return;

            float dusk = hour > 19.3f && hour < 22f ? 1f : 0f;
            float uForage = hunger > 0.22f ? hunger * (odor > 0.02f ? 1.25f : 0.9f) * (1f - 0.7f * fear) : 0f;
            float uWater = thirst > 0.28f ? thirst * 1.15f * (1f - 0.6f * fear) : 0f;
            float uRest = 0.18f + 0.65f * sleepDrive + (light < 0.1f ? 0.4f : 0f) - 0.3f * fear;
            float uExplore = 0.3f + 0.3f * FlyPhysiology.ActivityPeak(hour) + 0.25f * fear - (light < 0.1f ? 0.25f : 0f);
            float uGroom = _groomUrge + (s.DustL + s.DustR > 0 ? 0.1f + 0.02f * ActivityTime : 0f);
            float uRoost = dusk * 0.9f * (1f - fear);
            if (Current == Activity.Roost) uRoost = 0;

            var best = Activity.Explore;
            float bestU = uExplore + (Current == Activity.Explore ? 0.12f : 0);
            void Consider(Activity a, float u)
            {
                if (Current == a) u += minBout ? 0.5f : 0.12f;
                if (u > bestU)
                {
                    bestU = u;
                    best = a;
                }
            }
            Consider(Activity.Forage, uForage);
            Consider(Activity.SeekWater, uWater);
            Consider(Activity.Rest, uRest);
            Consider(Activity.Groom, uGroom);
            Consider(Activity.Roost, uRoost);
            // females lay eggs on fermenting fruit, mostly in the afternoon and evening
            bool onFruit = OnFermentingFruit();
            if (_phys.Eggs > 0.5f && onFruit && fear < 0.2f && light > 0.05f)
                Consider(Activity.LayEggs, 0.55f + _phys.Eggs * 0.4f + (hour > 15f && hour < 21f ? 0.2f : 0f));

            bool wet = s.LegsInWater || s.OnWater;
            if (wet && (best == Activity.Rest || best == Activity.Roost) && Current != Activity.SeekWater)
            {
                best = Activity.Explore;
                bestU = 10f;
            }
            // resting at night turns into sleep
            if (Current == Activity.Rest && !wet && ActivityTime > 4f && sleepDrive > 0.58f && fear < 0.1f && hunger < 0.75f && thirst < 0.75f && !_motor.IsSettling)
            {
                Switch(Activity.Sleep, hour > 19 || hour < 6 ? "ночной сон" : "дневной сон (сиеста)");
                _phys.Asleep = true;
                return;
            }
            if (best != Current)
            {
                string detail = best switch
                {
                    Activity.Forage => $"голод {hunger:P0}",
                    Activity.SeekWater => $"жажда {thirst:P0}",
                    Activity.Rest => light < 0.1f ? "темно" : sleepDrive > 0.4f ? "сонливость" : "",
                    Activity.Groom => "",
                    Activity.Roost => "сумерки",
                    _ => "",
                };
                Switch(best, detail);
                if (best == Activity.Groom)
                    _intent.Groom = s.DustL + s.DustR > 0 ? GroomKind.Eyes : Random.value < 0.5f ? GroomKind.HindLegs : GroomKind.FrontLegs;
            }
        }

        // ------------------------------------------------------------------ walking behaviors

        void Ground(float dt, in SensoryState s, float hour, float light, FlyEnvironment env)
        {
            var root = _rig.Root;
            Vector3 up = _motor.SurfaceUp;
            Vector3 fwd = root.forward;
            _intent.Posture = FlyMode.Walk;
            _intent.SleepDepth = 0;
            float forward = 0, turn = 0;
            WanderNoise(dt);

            // being stuck (pushing against something) makes flies turn
            float moved = Vector3.Distance(root.position, _lastPos);
            _lastPos = root.position;
            if (_intent.Forward > 3f && moved < _intent.Forward * dt * 0.2f) _stuckTimer += dt;
            else _stuckTimer = Mathf.Max(0, _stuckTimer - dt);

            switch (Current)
            {
                case Activity.Sleep:
                    _intent.Posture = FlyMode.Sleep;
                    _intent.SleepDepth = Mathf.Clamp01(0.5f + _phys.SleepDepth);
                    _intent.HeadYaw = _intent.AntennaL = _intent.AntennaR = 0;
                    _intent.Probe = 0;
                    break;

                case Activity.Rest:
                    _intent.Posture = FlyMode.Rest;
                    if (_phys.Fear > 0.4f) _intent.Probe = 0;
                    break;

                case Activity.Groom:
                    _intent.Posture = FlyMode.Groom;
                    _intent.Probe = 0;
                    break;

                case Activity.Drink:
                    _intent.Posture = FlyMode.Drink;
                    break;

                case Activity.LayEggs:
                {
                    _intent.Posture = FlyMode.Rest;
                    _intent.Probe = 0;
                    _intent.Oviposit = ActivityTime > 0.6f ? 1f : ActivityTime / 0.6f;
                    Detail = "ищет место яйцекладки на бродящем фрукте";
                    if (ActivityTime > 5.5f)
                    {
                        FlyEgg.Lay(_rig, _motor);
                        _phys.Eggs = Mathf.Max(0, _phys.Eggs - 0.14f);
                        _phys.EggsLaid++;
                        Switch(Activity.Groom, "отложила яйцо");
                        _intent.Groom = GroomKind.HindLegs;
                    }
                    break;
                }

                case Activity.Reject:
                {
                    // wing flicks and walking away from a courting male
                    float k = Mathf.Repeat(ActivityTime * 2.2f, 1f);
                    _intent.WingFlick = k < 0.25f ? 0.9f : 0f;
                    forward = ActivityTime > 0.8f ? 15f : 0f;
                    turn = _wiggle * 0.8f;
                    if (ActivityTime > 3.5f) Switch(Activity.Explore);
                    break;
                }

                case Activity.LocalSearch:
                {
                    // "fly dance": slow loops with frequent reversals that keep the fly near the food it found
                    forward = 4f + 2f * Mathf.Sin(ActivityTime * 1.3f);
                    turn = 2.4f * Mathf.Sign(Mathf.Sin(ActivityTime * 0.7f + 0.5f)) + _wiggle * 0.5f;
                    if (FoodMemory.HasValue) turn += TurnTowards(FoodMemory.Value, 1.2f, 6f);
                    if (_afterMealTimer <= 0) Switch(Activity.Explore);
                    break;
                }

                case Activity.Forage:
                {
                    float odor = (s.OdorL + s.OdorR) * 0.5f;
                    Vector3 headPos = _rig.HeadVisual.position;
                    float near = OdorField.NearField(headPos);
                    if (near > 0.18f)
                    {
                        // close to the source: follow the smooth near-field gradient (sampled by head and antennal movements)
                        Vector3 right = Vector3.Cross(up, fwd);
                        const float e = 3f;
                        Vector3 grad = fwd * (OdorField.NearField(headPos + fwd * e) - OdorField.NearField(headPos - fwd * e))
                                     + right * (OdorField.NearField(headPos + right * e) - OdorField.NearField(headPos - right * e));
                        if (grad.sqrMagnitude > 1e-10f)
                            turn = Mathf.Clamp(Vector3.SignedAngle(fwd, grad, up) / 35f, -2.8f, 2.8f) + _wiggle * 0.25f;
                        forward = Mathf.Lerp(10f, 4f, Mathf.Clamp01((near - 0.18f) * 1.5f));
                        _odorLost = 0;
                        Detail = "запах брожения совсем рядом — идёт к источнику";
                        // no progress towards the source for a while: hop onto it
                        if (near > _nearBest + 0.02f)
                        {
                            _nearBest = near;
                            _nearStall = 0;
                        }
                        else _nearStall += dt;
                        if (_nearStall > 6f) WantFlight(dt, 0.6f);
                        if (s.LegsInFood && s.TouchedFood != null && s.TouchedFood.Taste == Taste.Sugar)
                        {
                            forward = 1f;
                            _probeTimer = Mathf.Min(_probeTimer, 0.05f); // taste with the proboscis
                        }
                    }
                    else if (odor > 0.012f)
                    {
                        // osmotropotaxis (left/right difference), anemotaxis (upwind) and klinokinesis (turn more when it gets worse)
                        float trop = Mathf.Clamp((s.OdorR - s.OdorL) / (odor + 0.02f) * 5f, -2.5f, 2.5f);
                        float upwind = UpwindTurn(s.WindAtHead, 1.4f) * Mathf.Clamp01(1f - odor * 1.5f);
                        float worse = odor < _odorAvgPrev * 0.97f ? 1f : 0f;
                        _odorAvgPrev = Mathf.Lerp(_odorAvgPrev, odor, 1 - Mathf.Exp(-dt * 2f));
                        forward = Mathf.Lerp(12f, 5f, Mathf.Clamp01(odor * 1.6f));
                        turn = trop + upwind + worse * _wiggle * 1.5f + _wiggle * 0.3f;
                        _odorLost = 0;
                        Detail = odor > 0.4f ? "сильный запах брожения рядом" : "идёт против ветра по запаху";
                        if (s.LegsInFood && s.TouchedFood != null && s.TouchedFood.Taste == Taste.Sugar)
                        {
                            forward = 1.5f;
                            _probeTimer = Mathf.Min(_probeTimer, 0.05f); // taste with the proboscis
                        }
                    }
                    else
                    {
                        _odorLost += dt;
                        forward = _walking ? _speed : 0;
                        turn = _wiggle;
                        var remembered = FoodMemory ?? OdorMemory;
                        if (remembered.HasValue) turn += TurnTowards(remembered.Value, 0.8f, 30f);
                        Detail = FoodMemory.HasValue ? "вспоминает, где ела" : OdorMemory.HasValue ? "вспоминает, где пахло брожением" : "запаха нет";
                        if (_odorLost > 5f) WantFlight(dt, 0.25f);
                    }
                    break;
                }

                case Activity.SeekWater:
                {
                    float hum = (s.HumidityL + s.HumidityR) * 0.5f;
                    if (s.LabellumInWater || s.LegsInWater)
                    {
                        Switch(Activity.Drink, s.TouchedWater != null && !string.IsNullOrEmpty(s.TouchedWater.Label) ? s.TouchedWater.Label : "");
                        break;
                    }
                    if (hum > 0.01f)
                    {
                        // hygrosensation: walk up the humidity gradient
                        Vector3 headPos = _rig.HeadVisual.position;
                        Vector3 right = Vector3.Cross(up, fwd);
                        const float e = 2.5f;
                        Vector3 grad = fwd * (OdorField.Humidity(headPos + fwd * e) - OdorField.Humidity(headPos - fwd * e))
                                     + right * (OdorField.Humidity(headPos + right * e) - OdorField.Humidity(headPos - right * e));
                        if (grad.sqrMagnitude > 1e-12f) turn = Mathf.Clamp(Vector3.SignedAngle(fwd, grad, up) / 35f, -2.8f, 2.8f) + _wiggle * 0.2f;
                        forward = Mathf.Lerp(12f, 4f, hum);
                        Detail = hum > 0.3f ? "влажно — вода рядом" : "чувствует влажность — идёт к воде";
                        if (hum < 0.3f && ActivityTime > 6f) WantFlight(dt, 0.25f);
                        else if (ActivityTime > 25f) WantFlight(dt, 0.3f);
                    }
                    else
                    {
                        forward = _walking ? _speed : 0;
                        turn = _wiggle;
                        if (WaterMemory.HasValue) turn += TurnTowards(WaterMemory.Value, 0.8f, 30f);
                        Detail = WaterMemory.HasValue ? "вспоминает, где пила" : "сухо — ищет воду";
                        if (ActivityTime > 2.5f) WantFlight(dt, 0.7f);
                    }
                    break;
                }

                case Activity.Roost:
                {
                    forward = _walking ? _speed * 0.7f : 0;
                    turn = _wiggle;
                    bool sheltered = Physics.Raycast(root.position + up * 1f, Vector3.up, 250f, FlyLayers.SurfaceMask) || up.y < -0.2f;
                    if (sheltered && ActivityTime > 2f) Switch(Activity.Rest, "укрытие найдено");
                    else if (ActivityTime > 1.5f && light > 0.04f) WantFlight(dt, 1.5f);
                    break;
                }

                case Activity.ReturnToRange:
                    forward = 11f;
                    if (env != null) turn = TurnTowards(env.Center, 2f, 0f);
                    WantFlight(dt, 0.4f);
                    break;

                case Activity.Flee:
                    _intent.Posture = FlyMode.Rest;
                    break;

                default: // Explore
                {
                    forward = _walking ? _speed : 0;
                    turn = forward > 0.5f ? _wiggle : 0;
                    // negative geotaxis on steep surfaces
                    if (up.y < 0.8f)
                    {
                        Vector3 uphill = Vector3.ProjectOnPlane(Vector3.up, up);
                        if (uphill.sqrMagnitude > 1e-3f) turn += Mathf.Clamp(Vector3.SignedAngle(fwd, uphill, up) / 90f, -1f, 1f) * 0.7f;
                    }
                    if (!_walking) _intent.Posture = FlyMode.Rest;
                    WantFlight(dt, 0.02f + 0.03f * FlyPhysiology.ActivityPeak(hour) + (s.OnWater ? 0.8f : 0));
                    break;
                }
            }

            if (_stuckTimer > 1.2f)
            {
                turn += 2.5f * Mathf.Sign(_wiggle + 0.01f);
                if (_stuckTimer > 3f) WantFlight(dt, 3f);
            }

            _intent.Forward = forward * (_phys.Weak ? 0.4f : 1f);
            _intent.Turn = turn;
            if (_phys.Weak || light < 0.06f) _intent.Fly = false;
        }

        bool OnFermentingFruit()
        {
            var surface = _motor.CurrentSurface;
            if (surface == null) return false;
            bool fruit = IsFruit(surface);
            return fruit && OdorField.NearField(_rig.Root.position) > 0.35f;
        }

        float _courtCheck;

        void Courtship(float dt)
        {
            _courtCheck -= dt;
            if (_courtCheck > 0 || Current == Activity.Reject || Current == Activity.Sleep) return;
            _courtCheck = 0.4f;
            foreach (var w in WildFly.All)
            {
                if (w.CourtTarget != _rig.Root) continue;
                if (Vector3.Distance(w.Rig.Root.position, _rig.Root.position) > 6f) continue;
                // an unreceptive female: flicks her wings and leaves; sometimes she tolerates him for a while
                if (Current == Activity.LayEggs || Random.value < 0.35f) continue;
                Switch(Activity.Reject, "самец поёт крылом рядом");
                w.Rejected();
                return;
            }
        }

        void WanderNoise(float dt)
        {
            _boutTimer -= dt;
            if (_boutTimer <= 0)
            {
                _walking = !_walking;
                bool urgent = Current == Activity.SeekWater || Current == Activity.Forage || Current == Activity.ReturnToRange;
                _boutTimer = _walking ? Random.Range(1.5f, 7f) * (urgent ? 2f : 1f) : Random.Range(0.4f, 2.8f) * (urgent ? 0.4f : 1f);
                _speed = Random.Range(5f, 14f) * (1f + _phys.Fear * 0.6f);
            }
            _wiggleTimer -= dt;
            if (_wiggleTimer <= 0)
            {
                _wiggleTarget = Random.Range(-1.3f, 1.3f);
                if (Random.value < 0.08f) _wiggleTarget = Random.Range(-4f, 4f); // occasional sharp turn
                _wiggleTimer = Random.Range(0.3f, 1.4f);
            }
            _wiggle += (_wiggleTarget - _wiggle) * (1 - Mathf.Exp(-dt * 2.5f));
        }

        float TurnTowards(Vector3 target, float gain, float deadZone)
        {
            var root = _rig.Root;
            Vector3 up = _motor.SurfaceUp;
            Vector3 to = Vector3.ProjectOnPlane(target - root.position, up);
            if (to.magnitude < deadZone) return 0;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(root.forward, up), to, up);
            return Mathf.Clamp(angle / 45f, -1.5f, 1.5f) * gain;
        }

        float UpwindTurn(Vector3 wind, float gain)
        {
            var root = _rig.Root;
            Vector3 up = _motor.SurfaceUp;
            Vector3 against = Vector3.ProjectOnPlane(-wind, up);
            if (against.magnitude < 5f) return 0;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(root.forward, up), against, up);
            return Mathf.Clamp(angle / 60f, -1.2f, 1.2f) * gain;
        }

        float _lookTimer;

        void IdleMovements(float dt)
        {
            _headTimer -= dt;
            _lookTimer -= dt;
            if (_lookTimer <= 0)
            {
                // the head follows small moving animals nearby (other flies, ants)
                _lookTimer = 0.25f;
                var head = _rig.HeadVisual;
                float best = 45f;
                foreach (var o in SeenObject.All)
                {
                    if (o.Owner == _rig.Root || o.Velocity.sqrMagnitude < 4f) continue;
                    Vector3 rel = o.transform.position - head.position;
                    float d = rel.magnitude;
                    if (d > best) continue;
                    Vector3 local = head.parent != null ? head.parent.InverseTransformDirection(rel) : rel;
                    float az = Mathf.Atan2(local.x, local.z);
                    if (Mathf.Abs(az) > 1.5f) continue;
                    best = d;
                    _headYaw = Mathf.Clamp(az / 0.9f, -0.7f, 0.7f);
                    _headPitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) / 0.9f, -0.5f, 0.6f);
                    _headTimer = 0.8f;
                }
            }
            if (_headTimer <= 0)
            {
                _headTimer = Random.Range(0.6f, 3.5f);
                _headYaw = Random.value < 0.4f ? 0 : Random.Range(-0.45f, 0.45f);
                _headPitch = Random.Range(-0.2f, 0.3f);
            }
            _antTimer -= dt;
            if (_antTimer <= 0)
            {
                _antTimer = Random.Range(0.15f, 1.8f);
                _antL = Random.Range(-0.4f, 0.8f);
                _antR = Random.value < 0.6f ? _antL + Random.Range(-0.2f, 0.2f) : Random.Range(-0.4f, 0.8f);
            }
            _probeTimer -= dt;
            if (_probeTimer <= 0)
            {
                _probeTimer = Random.Range(4f, 14f);
                _probe = 0.55f;
            }
            _probe = Mathf.Max(0, _probe - dt * 1.8f);
        }

        // ------------------------------------------------------------------ flight

        void WantFlight(float dt, float ratePerSecond)
        {
            if (_motor.TakeoffCooldown > 0 || _phys.Weak) return;
            var env = FlyEnvironment.Current;
            if (env != null && !env.AllowVoluntaryFlight) return;
            if (env != null && env.LightLevel < 0.08f) return; // no flying in the dark
            if (WindField.Instance != null && WindField.Instance.Strength * WindField.Instance.Gust > 2600f) return; // too windy
            _flightUrge += dt * ratePerSecond;
            if (Random.value < 1f - Mathf.Exp(-dt * ratePerSecond))
            {
                _flightUrge = 0;
                _intent.Fly = true;
                _landingAge = 0;
                _odorLost = 0;
                _castTimer = 0;
                if (Current == Activity.Explore) Switch(Activity.Relocate, "перелетает на новое место");
                PlanLanding(env);
            }
        }

        void PlanLanding(FlyEnvironment env)
        {
            Vector3 origin = _rig.Body.position + Vector3.up * 3f;
            _intent.HasLandingTarget = false;
            switch (Current)
            {
                case Activity.Forage:
                    var target = FoodMemory ?? OdorMemory;
                    if (ScanLanding(origin, target.HasValue ? target.Value - origin : Vector3.zero, 40f, 900f, ScoreFood, out var p, out var n))
                        SetLanding(p, n);
                    break;
                case Activity.SeekWater:
                case Activity.Drink:
                    if (ScanLanding(origin, WaterMemory.HasValue ? WaterMemory.Value - origin : Vector3.zero, 40f, 900f, ScoreWater, out p, out n))
                        SetLanding(p, n);
                    break;
                case Activity.Roost:
                    if (ScanLanding(origin, Vector3.zero, 30f, 700f, ScoreShelter, out p, out n)) SetLanding(p, n);
                    break;
                case Activity.ReturnToRange:
                    if (env != null && ScanLanding(origin, env.Center - origin, 100f, 1200f, ScoreExplore, out p, out n)) SetLanding(p, n);
                    break;
                case Activity.Flee:
                    if (ScanLanding(origin, origin - _threatFrom, 100f, 600f, ScoreFlee, out p, out n)) SetLanding(p, n);
                    break;
                default:
                    if (ScanLanding(origin, Vector3.zero, 80f, 800f, ScoreExplore, out p, out n)) SetLanding(p, n);
                    break;
            }
        }

        void SetLanding(Vector3 p, Vector3 n)
        {
            _intent.HasLandingTarget = true;
            _intent.LandingPoint = p;
            _intent.LandingNormal = n;
            _landingAge = 0;
        }

        float ScoreFood(RaycastHit h)
        {
            float odor = OdorField.NearField(h.point + h.normal * 2f);
            var remembered = FoodMemory ?? OdorMemory;
            float mem = remembered.HasValue ? Mathf.Exp(-Vector3.Distance(h.point, remembered.Value) / 150f) * 2f : 0;
            return odor * 4f + mem + (h.normal.y > -0.2f ? 0.3f : -1f) - (IsWaterSurface(h) ? 5f : 0) + (IsFruit(h.collider) ? 1.2f : 0f) + HomeBias(h.point) + Random.value * 0.3f;
        }

        float ScoreWater(RaycastHit h)
        {
            float hum = OdorField.Humidity(h.point + h.normal * 2f);
            float mem = WaterMemory.HasValue ? Mathf.Exp(-Vector3.Distance(h.point, WaterMemory.Value) / 100f) * 2f : 0;
            return hum * 6f + mem + (h.normal.y > 0.3f ? 0.3f : -0.5f) + HomeBias(h.point) * 0.5f + Random.value * 0.3f;
        }

        float ScoreShelter(RaycastHit h)
        {
            float s = h.normal.y < -0.3f ? 2.5f : 0f;
            if (Physics.Raycast(h.point + h.normal * 2f, Vector3.up, 300f, FlyLayers.SurfaceMask)) s += 1.5f;
            if (h.collider != null && h.collider.name.Contains("trunk")) s += 1f;
            return s - (IsWaterSurface(h) ? 5f : 0) + Random.value * 0.3f;
        }

        float ScoreExplore(RaycastHit h)
        {
            float s = 1f + Mathf.Clamp01(h.distance / 500f) * 0.5f + HomeBias(h.point) * 0.7f;
            if (h.collider != null && h.collider.name.Contains("trunk")) s += 0.4f;
            if (IsFruit(h.collider)) s += 0.6f;
            s += OdorField.Concentration(h.point) * 2f * _phys.Hunger;
            return s - (IsWaterSurface(h) ? 5f : 0) + Random.value;
        }

        float ScoreFlee(RaycastHit h)
        {
            float away = Vector3.Distance(h.point, _threatFrom);
            // far enough to be safe, close enough to come back to the fruit
            return Mathf.Clamp01(away / 200f) * 2f - Mathf.Clamp01((away - 450f) / 300f) * 2f - (IsWaterSurface(h) ? 5f : 0) + HomeBias(h.point) * 0.5f + Random.value * 0.5f;
        }

        /// <summary>Flies stay in the home range of their resource patch: a mild preference for places near its center.</summary>
        static float HomeBias(Vector3 p)
        {
            var env = FlyEnvironment.Current;
            if (env == null) return 0f;
            Vector3 d = p - env.Center;
            d.y = 0;
            return -Mathf.Clamp01(d.magnitude / env.Radius) * 1.5f;
        }

        static bool IsWaterSurface(RaycastHit h) => h.collider != null && h.collider.name == "Puddle";

        public static bool IsFruit(Collider c)
        {
            if (c == null) return false;
            string n = c.name.ToLowerInvariant();
            return n.Contains("яблоко") || n.Contains("груша") || n.Contains("слива") || n.Contains("вишня") || n.Contains("apple");
        }

        static bool ScanLanding(Vector3 origin, Vector3 preferred, float minDist, float maxDist, Func<RaycastHit, float> score, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            float best = float.NegativeInfinity;
            Vector3 pref = preferred.sqrMagnitude > 1f ? preferred.normalized : Vector3.zero;
            for (int k = 0; k < 56; k++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = -Mathf.Abs(dir.y) * 0.9f - 0.05f;
                if (pref != Vector3.zero && k % 2 == 0) dir = (pref + Random.insideUnitSphere * 0.35f).normalized;
                if (!Physics.Raycast(origin, dir.normalized, out var hit, maxDist, FlyLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.distance < minDist) continue;
                var env = FlyEnvironment.Current;
                if (env != null)
                {
                    Vector3 fromCenter = hit.point - env.Center;
                    fromCenter.y = 0;
                    if (fromCenter.magnitude > env.Radius) continue;
                }
                float s = score(hit);
                if (pref != Vector3.zero) s += Vector3.Dot(dir.normalized, pref) * 0.8f;
                if (s > best)
                {
                    best = s;
                    point = hit.point;
                    normal = hit.normal;
                }
            }
            return best > float.NegativeInfinity;
        }

        void Fly(float dt, in SensoryState s, float light, FlyEnvironment env)
        {
            _landingAge += dt;
            float t = _motor.FlightTime;
            Vector3 pos = _rig.Body.position;
            _intent.LandAnywhere = false;
            _intent.FlightSpeed = FlyMotor.CruiseSpeed;
            float heightAbove = _motor.HeightAboveGround;

            if (light < 0.06f || _phys.Weak)
            {
                _intent.HasLandingTarget = false;
                _intent.LandAnywhere = true;
                Detail = "темно — садится";
                return;
            }

            // reached the neighbourhood of the target but no touchdown (target moved / occluded): pick again
            if (_intent.HasLandingTarget && (_landingAge > 5f || Vector3.Distance(pos, _intent.LandingPoint) < 2.5f))
            {
                _intent.HasLandingTarget = false;
                if (t > 9f) _intent.LandAnywhere = true;
            }

            switch (Current)
            {
                case Activity.Flee:
                    if (t < 0.45f)
                    {
                        Vector3 away = pos - _threatFrom;
                        away.y = 0;
                        _intent.FlightDirection = (away.normalized + Vector3.up * 0.9f).normalized;
                        _intent.FlightSpeed = 520f;
                        _intent.HasLandingTarget = false;
                    }
                    else if (!_intent.HasLandingTarget && (_flightDecisionTimer -= dt) <= 0)
                    {
                        _flightDecisionTimer = 0.3f;
                        PlanLanding(env);
                    }
                    Detail = "уходит от угрозы";
                    break;

                case Activity.Forage:
                {
                    float odor = (s.OdorL + s.OdorR) * 0.5f;
                    Vector3 wind = s.WindAtHead - _motor.Velocity;
                    Vector3 windWorld = WindField.Instance != null ? WindField.Instance.Direction : Vector3.forward;
                    Vector3 upwind = -Vector3.ProjectOnPlane(windWorld, Vector3.up).normalized;
                    if (odor > 0.02f)
                    {
                        _odorLost = 0;
                        _castPeriod = 0.45f;
                        float alt = Mathf.Clamp((35f - heightAbove) / 50f, -0.5f, 0.5f);
                        _intent.FlightDirection = (upwind + Vector3.up * alt).normalized;
                        _intent.FlightSpeed = 330f;
                        Detail = $"полёт против ветра по шлейфу ({odor:F2})";
                        if (odor > 0.28f && !_intent.HasLandingTarget && (_flightDecisionTimer -= dt) <= 0)
                        {
                            _flightDecisionTimer = 0.3f;
                            PlanLanding(env);
                        }
                    }
                    else
                    {
                        _odorLost += dt;
                        _castTimer -= dt;
                        if (_castTimer <= 0)
                        {
                            _castSign = -_castSign;
                            _castPeriod = Mathf.Min(1.6f, _castPeriod * 1.25f);
                            _castTimer = _castPeriod;
                        }
                        Vector3 cross = Vector3.Cross(Vector3.up, upwind) * _castSign;
                        float alt = Mathf.Clamp((45f - heightAbove) / 50f, -0.5f, 0.5f);
                        if (!_intent.HasLandingTarget)
                            _intent.FlightDirection = (cross + upwind * 0.25f + Vector3.up * alt).normalized;
                        Detail = "потеряла запах — галсы поперёк ветра";
                        if (_odorLost > 3.5f && !_intent.HasLandingTarget && (_flightDecisionTimer -= dt) <= 0)
                        {
                            _flightDecisionTimer = 0.3f;
                            PlanLanding(env);
                        }
                    }
                    _ = wind;
                    break;
                }

                default:
                    _flightDecisionTimer -= dt;
                    if (!_intent.HasLandingTarget && !_intent.LandAnywhere && _flightDecisionTimer <= 0)
                    {
                        _flightDecisionTimer = 0.3f;
                        PlanLanding(env);
                        if (!_intent.HasLandingTarget)
                        {
                            float alt = Mathf.Clamp((60f - heightAbove) / 60f, -0.5f, 0.5f);
                            _intent.FlightDirection = (_rig.Root.forward + Vector3.up * alt).normalized;
                        }
                    }
                    if (Current == Activity.Relocate) Detail = "перелетает";
                    break;
            }

            if (env != null && Current != Activity.Flee)
            {
                Vector3 d = pos - env.Center;
                d.y = 0;
                if (d.magnitude > env.Radius * 1.05f)
                {
                    _intent.HasLandingTarget = false;
                    _intent.FlightDirection = (-d.normalized + Vector3.up * 0.1f).normalized;
                    Detail = "край мира — разворот";
                }
            }
            if (t > 14f && !_intent.HasLandingTarget) _intent.LandAnywhere = true;
        }

        /// <summary>After touchdown: flights end in a short pause and usually a grooming bout.</summary>
        public void OnLanded()
        {
            if (Current == Activity.Relocate) Switch(Random.value < 0.4f ? Activity.Groom : Activity.Explore, "после приземления");
            if (Current == Activity.Groom) _intent.Groom = Random.value < 0.6f ? GroomKind.HindLegs : GroomKind.FrontLegs;
        }
    }
}
