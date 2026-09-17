using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyBrain
{
    /// <summary>
    /// The other inhabitants of the orchard floor: wild fruit flies (one of them a courting male), an ant trail,
    /// a zebra jumping spider that hunts flies and a blackbird that sometimes swoops over the fallen fruit.
    /// They run on simple behavior rules (only the main fly has the connectome brain), but they are real stimuli
    /// for it: moving flies and ants drive LC10a, a jumping spider or a diving bird drives LC4 / LPLC2.
    /// </summary>
    public sealed class Wildlife : MonoBehaviour
    {
        public readonly List<WildFly> Flies = new List<WildFly>();
        public JumpingSpider Spider { get; private set; }
        public Bird Bird { get; private set; }
        public readonly List<Ant> Ants = new List<Ant>();
        FlyBrainApp _app;
        static FlyBrainApp _main;
        float _birdTimer;

        public void Init(FlyBrainApp app)
        {
            _app = app;
            _main = app;
            var env = app.World as NatureEnvironment;
            var root = new GameObject("Wildlife").transform;
            root.SetParent(app.World.transform, false);

            // wild flies on the fruit
            int n = 0;
            foreach (var fruit in env != null ? env.Fruits : new List<Transform>())
            {
                if (n >= 5 || fruit.name.Contains("Свежее") || fruit.name.Contains("Вишня")) continue;
                var top = fruit.position + Vector3.up * 120f + Random.insideUnitSphere * 10f;
                if (!Physics.Raycast(top, Vector3.down, out var hit, 300f, FlyLayers.SurfaceMask)) continue;
                bool male = n == 0 || n == 3;
                var fly = WildFly.Create(root, hit.point, hit.normal, male, male && n == 0);
                Flies.Add(fly);
                n++;
            }

            // an ant trail from the tree to the pecked apple and back
            var trail = new List<Vector3>();
            Vector3 a = NatureEnvironment.TrunkPosition + new Vector3(-150, 0, 120);
            Vector3 b = new Vector3(-230, 0, -110);
            for (int k = 0; k <= 24; k++)
            {
                float t = k / 24f;
                var p = Vector3.Lerp(a, b, t) + new Vector3(Mathf.Sin(t * 9f) * 40f, 0, Mathf.Cos(t * 6f) * 30f);
                trail.Add(p);
            }
            for (int k = 0; k < 16; k++) Ants.Add(Ant.Create(root, trail, k / 16f + Random.Range(0f, 0.03f)));

            // a zebra jumping spider living on a stone near the fruit
            Vector3 home = new Vector3(-120, 0, -60);
            if (Physics.Raycast(home + Vector3.up * 300f, Vector3.down, out var h2, 600f, FlyLayers.SurfaceMask)) home = h2.point;
            Spider = JumpingSpider.Create(root, home);

            Bird = Bird.Create(root);
            _birdTimer = Random.Range(90f, 200f);
        }

        void Update()
        {
            var clock = _app.World.Clock;
            if (clock == null) return;
            if (clock.Daylight > 0.5f)
            {
                _birdTimer -= Time.deltaTime;
                if (_birdTimer <= 0)
                {
                    _birdTimer = Random.Range(160f, 360f);
                    SwoopBird();
                }
            }
        }

        public void SwoopBird()
        {
            if (Bird != null && !Bird.Active) Bird.Swoop(_app.Rig.Body.position);
        }

        public void SendSpider()
        {
            if (Spider != null) Spider.HuntNear(_app.Rig.Root);
        }

        /// <summary>All fly bodies that a predator might target (the brain fly first).</summary>
        public static IEnumerable<Transform> FlyBodies()
        {
            if (_main != null && _main.Rig != null) yield return _main.Rig.Root;
            foreach (var f in WildFly.All) yield return f.Rig.Root;
        }

        public static Material Mat(string key, Color c, float gloss) => FlyMaterials.Lit(key, c, gloss, new Color(0.2f, 0.2f, 0.2f));

        public static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            go.layer = layer;
            return go.transform;
        }
    }

    // ====================================================================== wild flies

    /// <summary>A fruit fly without a connectome brain: rule-based feeding, grooming, walking, short flights, escapes and courtship.</summary>
    public sealed class WildFly : MonoBehaviour
    {
        public static readonly List<WildFly> All = new List<WildFly>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public FlyRig Rig;
        public FlyMotor Motor;
        public bool Male, Courter;
        /// <summary>The fly this male is courting right now (null otherwise).</summary>
        public Transform CourtTarget => _state == State.Court ? _courtTarget : null;
        /// <summary>A courted female kicks or flicks her wings: the male gives up.</summary>
        public void Rejected()
        {
            if (_state == State.Court)
            {
                _state = State.Walk;
                _timer = Random.Range(3f, 6f);
            }
        }
        FlySensors _sensors;
        enum State { Walk, Rest, Feed, Groom, Fly, Court, Sleep }
        State _state = State.Rest;
        float _timer, _wiggle, _wiggleT;
        GroomKind _groom;
        Transform _courtTarget;
        AudioSource _buzz;
        bool _requestFly;

        public static WildFly Create(Transform parent, Vector3 point, Vector3 normal, bool male, bool courter)
        {
            var fwd = Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized;
            var rot = Quaternion.LookRotation(fwd, normal);
            var rig = FlyBuilder.Build(parent, point, 0f, male);
            rig.Root.rotation = rot;
            rig.Root.name = male ? "Wild fly (male)" : "Wild fly (female)";
            var motor = rig.Root.gameObject.AddComponent<FlyMotor>();
            motor.BoundsCenter = Vector3.zero;
            motor.BoundsRadius = 1300f;
            motor.Init(rig);
            motor.PlaceAt(point, rot);
            var w = rig.Root.gameObject.AddComponent<WildFly>();
            w.Rig = rig;
            w.Motor = motor;
            w.Male = male;
            w.Courter = courter;
            w._sensors = new FlySensors(rig, motor);
            var seen = SeenObject.Attach(rig.Body.gameObject, 1.1f, male ? "муха-самец" : "муха");
            seen.Owner = rig.Root;
            w._timer = Random.Range(1f, 6f);
            w._buzz = FlyAudio.AttachBuzz(rig.Body.gameObject, 0.35f);
            return w;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            var s = _sensors.Update(dt);
            var env = FlyEnvironment.Current;
            float light = env != null ? env.LightLevel : 1f;
            float hour = env != null && env.Clock != null ? env.Clock.Hour : 12f;
            var cmd = new MotorCommand { Mode = FlyMode.Walk };
            _timer -= dt;
            _wiggleT -= dt;
            if (_wiggleT <= 0)
            {
                _wiggleT = Random.Range(0.3f, 1.2f);
                _wiggle = Random.Range(-1.5f, 1.5f);
            }

            // escape: anything looming fast
            if (s.ThreatLoom > 0.35f && !Motor.IsAirborne && light > 0.05f)
            {
                cmd.Takeoff = true;
                cmd.ThreatDirection = s.ThreatDirection;
                _state = State.Fly;
                _timer = Random.Range(1.5f, 3f);
            }

            if (Motor.IsAirborne)
            {
                if (_state != State.Fly)
                {
                    _state = State.Fly;
                    _timer = 2f;
                }
                cmd.FlightSpeed = 300f;
                if (_timer <= 0 || light < 0.06f) cmd.LandAnywhere = true;
                else cmd.FlightDirection = (Rig.Root.forward + Vector3.up * 0.1f).normalized;
                if (!_hasTarget && _timer < 1.2f) PickLanding();
                if (_hasTarget)
                {
                    cmd.HasLandingTarget = true;
                    cmd.LandingPoint = _target;
                    cmd.LandingNormal = _targetNormal;
                }
            }
            else
            {
                _hasTarget = false;
                if (_state == State.Fly) Next(light, hour);
                if (_timer <= 0) Next(light, hour);
                switch (_state)
                {
                    case State.Walk:
                        cmd.Forward = 8f;
                        cmd.Turn = _wiggle;
                        if (s.LegsInFood && s.TouchedFood != null && s.TouchedFood.Taste == Taste.Sugar && Random.value < dt * 2f)
                        {
                            _state = State.Feed;
                            _timer = Random.Range(5f, 20f);
                        }
                        break;
                    case State.Feed:
                        cmd.Mode = FlyMode.Feed;
                        cmd.Proboscis = s.LegsInFood || s.LabellumInFood ? 1f : 0.4f;
                        cmd.Pump = s.LabellumInFood ? 0.8f : 0f;
                        cmd.Labellum = 0.7f;
                        break;
                    case State.Groom:
                        cmd.Mode = FlyMode.Groom;
                        cmd.Groom = 0.7f;
                        cmd.GroomKind = s.DustL + s.DustR > 0 ? GroomKind.Eyes : _groom;
                        break;
                    case State.Rest:
                        cmd.Mode = FlyMode.Rest;
                        break;
                    case State.Sleep:
                        cmd.Mode = FlyMode.Sleep;
                        cmd.Sleep = 0.8f;
                        if (light > 0.3f) Next(light, hour);
                        break;
                    case State.Court:
                        Court(ref cmd, dt);
                        break;
                }
                if (s.DustL + s.DustR > 1 && _state != State.Groom && _state != State.Sleep)
                {
                    _state = State.Groom;
                    _timer = 4f;
                }
            }
            if (Motor.IsAirborne) _requestFly = false;
            else if (_requestFly) cmd.FlyRequest = true;
            Motor.Command = cmd;
            if (_buzz != null) _buzz.volume = Mathf.MoveTowards(_buzz.volume, Motor.IsAirborne ? 0.35f : 0f, dt * 3f);
        }

        bool _hasTarget;
        Vector3 _target, _targetNormal;

        void PickLanding()
        {
            // flies aggregate on fermenting fruit
            float best = -1;
            for (int k = 0; k < 24; k++)
            {
                var dir = Random.onUnitSphere;
                dir.y = -Mathf.Abs(dir.y);
                if (!Physics.Raycast(Rig.Body.position, dir, out var hit, 700f, FlyLayers.SurfaceMask)) continue;
                if (hit.distance < 30f || hit.collider.name == "Puddle") continue;
                float score = OdorField.Concentration(hit.point + hit.normal * 3f) + Random.value * 0.2f;
                if (score > best)
                {
                    best = score;
                    _target = hit.point;
                    _targetNormal = hit.normal;
                    _hasTarget = true;
                }
            }
        }

        void Next(float light, float hour)
        {
            if (light < 0.08f)
            {
                _state = State.Sleep;
                _timer = Random.Range(20f, 60f);
                return;
            }
            float r = Random.value;
            if (Courter && r < 0.3f && FindCourtTarget())
            {
                _state = State.Court;
                _timer = Random.Range(8f, 20f);
                return;
            }
            if (r < 0.35f) { _state = State.Walk; _timer = Random.Range(1.5f, 6f); }
            else if (r < 0.62f) { _state = State.Rest; _timer = Random.Range(1f, 8f); }
            else if (r < 0.8f) { _state = State.Groom; _groom = Random.value < 0.5f ? GroomKind.HindLegs : GroomKind.FrontLegs; _timer = Random.Range(2f, 6f); }
            else if (r < 0.9f && Motor.TakeoffCooldown <= 0)
            {
                _state = State.Fly;
                _timer = Random.Range(1.5f, 3.5f);
                _requestFly = true;
                PickLanding();
            }
            else { _state = State.Walk; _timer = Random.Range(2f, 5f); }
        }

        bool FindCourtTarget()
        {
            _courtTarget = null;
            float best = 60f;
            foreach (var body in Wildlife.FlyBodies())
            {
                if (body == Rig.Root) continue;
                var w = body.GetComponent<WildFly>();
                if (w != null && w.Male) continue;
                float d = Vector3.Distance(body.position, Rig.Root.position);
                if (d < best)
                {
                    best = d;
                    _courtTarget = body;
                }
            }
            return _courtTarget != null;
        }

        void Court(ref MotorCommand cmd, float dt)
        {
            if (_courtTarget == null)
            {
                _timer = 0;
                return;
            }
            // follow the female, orient to her, extend the wing that is closer to her and sing
            Vector3 up = Motor.SurfaceUp;
            Vector3 to = Vector3.ProjectOnPlane(_courtTarget.position - Rig.Root.position, up);
            float dist = to.magnitude;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(Rig.Root.forward, up), to, up);
            cmd.Turn = Mathf.Clamp(angle / 30f, -3f, 3f);
            cmd.Forward = dist > 3.2f ? Mathf.Clamp((dist - 2.5f) * 4f, 0, 16f) : 0f;
            if (dist < 6f && Mathf.Abs(angle) < 50f)
            {
                float song = Mathf.Repeat(Time.time * 0.8f, 1f) < 0.6f ? 1f : 0f;
                if (angle < 0) cmd.WingExtendR = song; // extend the wing on the far side of the turn
                else cmd.WingExtendL = song;
            }
            if (dist > 90f || (_courtTarget.GetComponent<FlyMotor>()?.IsAirborne ?? false)) _timer = 0;
        }
    }

    // ====================================================================== ants

    /// <summary>A worker ant (Lasius) walking a pheromone trail.</summary>
    public sealed class Ant : MonoBehaviour
    {
        List<Vector3> _trail;
        float _s, _speed, _pause, _phase, _lane;
        Transform _body;
        readonly Transform[] _legs = new Transform[6];
        readonly Transform[] _antennae = new Transform[2];
        int _dir = 1;

        public static Ant Create(Transform parent, List<Vector3> trail, float s)
        {
            var go = new GameObject("Ant");
            go.transform.SetParent(parent, false);
            go.layer = FlyLayers.Creatures;
            var a = go.AddComponent<Ant>();
            a._trail = trail;
            a._s = s * (trail.Count - 1);
            a._speed = Random.Range(18f, 30f);
            a._lane = Random.Range(-2.5f, 2.5f);
            a._dir = Random.value < 0.5f ? 1 : -1;
            a.Build();
            SeenObject.Attach(go, 1.6f, "муравей");
            return a;
        }

        void Build()
        {
            int L = FlyLayers.Creatures;
            var black = Wildlife.Mat("antBody", new Color(0.09f, 0.06f, 0.05f), 0.75f);
            var legMat = Wildlife.Mat("antLeg", new Color(0.18f, 0.12f, 0.09f), 0.5f);
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _body.localPosition = new Vector3(0, 0.75f, 0);
            Wildlife.Part(_body, "Gaster", MeshFactory.Ellipsoid(new Vector3(0.62f, 0.55f, 0.85f), 16, 12), black, new Vector3(0, 0.1f, -1.4f), Quaternion.Euler(-12, 0, 0), L);
            Wildlife.Part(_body, "Petiole", MeshFactory.Ellipsoid(new Vector3(0.18f, 0.25f, 0.15f), 8, 6), black, new Vector3(0, 0.1f, -0.55f), Quaternion.identity, L);
            Wildlife.Part(_body, "Mesosoma", MeshFactory.Ellipsoid(new Vector3(0.3f, 0.3f, 0.7f), 12, 8), black, new Vector3(0, 0.12f, 0.15f), Quaternion.Euler(-8, 0, 0), L);
            Wildlife.Part(_body, "Head", MeshFactory.Ellipsoid(new Vector3(0.42f, 0.36f, 0.45f), 12, 8), black, new Vector3(0, 0.15f, 1.15f), Quaternion.identity, L);
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                _antennae[s] = Wildlife.Part(_body, "Antenna", MeshFactory.Segment(1.1f, 0.04f, 0.03f, 5), legMat, new Vector3(side * 0.15f, 0.35f, 1.45f), Quaternion.Euler(-30, side * 35, 0), L);
                for (int k = 0; k < 3; k++)
                {
                    var leg = Wildlife.Part(_body, "Leg", MeshFactory.Segment(1.5f, 0.06f, 0.03f, 5), legMat, new Vector3(side * 0.22f, 0f, 0.4f - k * 0.35f), Quaternion.identity, L);
                    _legs[s * 3 + k] = leg;
                }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_pause > 0)
            {
                _pause -= dt;
            }
            else
            {
                _s += _dir * _speed * dt / 45f;
                if (_s >= _trail.Count - 1) { _s = _trail.Count - 1.001f; _dir = -1; }
                if (_s <= 0) { _s = 0.001f; _dir = 1; }
                if (Random.value < dt * 0.12f) _pause = Random.Range(0.3f, 1.5f);
            }
            int i = Mathf.Clamp(Mathf.FloorToInt(_s), 0, _trail.Count - 2);
            float t = _s - i;
            Vector3 a = _trail[i], b = _trail[i + 1];
            Vector3 tangent = (b - a).normalized * _dir;
            Vector3 side = Vector3.Cross(Vector3.up, tangent);
            Vector3 p = Vector3.Lerp(a, b, t) + side * (_lane + Mathf.Sin(Time.time * 1.7f + _lane) * 1.5f);
            Vector3 up = Vector3.up;
            if (Physics.Raycast(p + Vector3.up * 80f, Vector3.down, out var hit, 200f, FlyLayers.SurfaceMask))
            {
                p = hit.point;
                up = Vector3.Slerp(Vector3.up, hit.normal, 0.7f);
            }
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.ProjectOnPlane(tangent, up), up));
            if (_pause <= 0) _phase += dt * _speed * 0.9f;
            for (int k = 0; k < 6; k++)
            {
                int sideSign = k < 3 ? -1 : 1;
                int pair = k % 3;
                float ph = _phase + ((pair % 2 == 0) == (sideSign < 0) ? 0 : Mathf.PI);
                float swing = Mathf.Sin(ph) * 22f;
                float lift = Mathf.Max(0, Mathf.Cos(ph)) * 15f;
                _legs[k].localRotation = Quaternion.Euler(35f - lift, sideSign * (90f - (pair - 1) * 35f + swing), 0);
            }
            for (int s = 0; s < 2; s++)
                _antennae[s].localRotation = Quaternion.Euler(-25 + Mathf.Sin(Time.time * 9f + s) * 12f, (s == 0 ? -1 : 1) * (30 + Mathf.Sin(Time.time * 7f + s * 2) * 15f), 0);
        }
    }

    // ====================================================================== jumping spider

    /// <summary>
    /// Zebra jumping spider (Salticus scenicus): watches moving flies, stalks them and jumps. The jump is fast
    /// enough to loom on a fly's eyes; a fly whose giant fiber fires in time gets away.
    /// </summary>
    public sealed class JumpingSpider : MonoBehaviour
    {
        enum State { Watch, Stalk, Crouch, Jump, Retreat }
        State _state = State.Watch;
        Vector3 _home, _jumpFrom, _jumpTo;
        Transform _prey;
        float _timer, _cooldown = 20f, _jumpT, _phase;
        Transform _body, _ceph;
        readonly Transform[] _legs = new Transform[8];
        public string StateName => _state.ToString();

        public static JumpingSpider Create(Transform parent, Vector3 home)
        {
            var go = new GameObject("Jumping spider");
            go.transform.SetParent(parent, false);
            go.transform.position = home;
            go.layer = FlyLayers.Creatures;
            var s = go.AddComponent<JumpingSpider>();
            s._home = home;
            s.Build();
            SeenObject.Attach(s._body.gameObject, 2.6f, "паук-скакун");
            return s;
        }

        void Build()
        {
            int L = FlyLayers.Creatures;
            var black = Wildlife.Mat("spiderBlack", new Color(0.06f, 0.05f, 0.05f), 0.45f);
            var white = Wildlife.Mat("spiderWhite", new Color(0.85f, 0.83f, 0.78f), 0.2f);
            var eye = Wildlife.Mat("spiderEye", new Color(0.02f, 0.02f, 0.02f), 0.95f);
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _body.localPosition = new Vector3(0, 1.5f, 0);
            _ceph = Wildlife.Part(_body, "Cephalothorax", MeshFactory.Ellipsoid(new Vector3(1.3f, 0.95f, 1.6f), 20, 14), black, new Vector3(0, 0.2f, 0.9f), Quaternion.identity, L);
            Wildlife.Part(_body, "Abdomen", MeshFactory.Ellipsoid(new Vector3(1.25f, 1.05f, 1.9f), 20, 14), black, new Vector3(0, 0.35f, -1.8f), Quaternion.Euler(-8, 0, 0), L);
            // zebra stripes: white chevrons on the abdomen
            for (int k = 0; k < 3; k++)
                Wildlife.Part(_body, "Stripe", MeshFactory.Ellipsoid(new Vector3(1.05f, 0.12f, 0.2f), 12, 6), white, new Vector3(0, 1.2f - k * 0.08f, -1.2f - k * 0.65f), Quaternion.Euler(-15, 0, 0), L);
            Wildlife.Part(_body, "Stripe", MeshFactory.Ellipsoid(new Vector3(0.9f, 0.1f, 0.18f), 12, 6), white, new Vector3(0, 1.05f, 0.4f), Quaternion.identity, L);
            // the large anterior median eyes
            for (int s = -1; s <= 1; s += 2)
            {
                Wildlife.Part(_ceph, "Eye", MeshFactory.Ellipsoid(Vector3.one * 0.36f, 12, 8), eye, new Vector3(s * 0.38f, 0.25f, 1.45f), Quaternion.identity, L);
                Wildlife.Part(_ceph, "Eye small", MeshFactory.Ellipsoid(Vector3.one * 0.18f, 8, 6), eye, new Vector3(s * 0.95f, 0.35f, 1.2f), Quaternion.identity, L);
                Wildlife.Part(_ceph, "Palp", MeshFactory.Segment(0.8f, 0.14f, 0.12f, 6), white, new Vector3(s * 0.3f, -0.45f, 1.45f), Quaternion.Euler(50, s * 10, 0), L);
            }
            for (int k = 0; k < 8; k++)
            {
                int side = k < 4 ? -1 : 1;
                int i = k % 4;
                var hip = new GameObject("Leg").transform;
                hip.SetParent(_body, false);
                hip.localPosition = new Vector3(side * 0.9f, -0.1f, 1.5f - i * 0.55f);
                float len = i == 0 ? 3.2f : 2.7f;
                var femur = Wildlife.Part(hip, "Femur", MeshFactory.Segment(len * 0.5f, i == 0 ? 0.28f : 0.18f, 0.14f, 6), i == 0 ? black : white, Vector3.zero, Quaternion.identity, L);
                Wildlife.Part(femur, "Tibia", MeshFactory.Segment(len * 0.55f, 0.13f, 0.07f, 6), black, new Vector3(0, 0, len * 0.5f), Quaternion.Euler(70, 0, 0), L);
                _legs[k] = hip;
            }
        }

        public void HuntNear(Transform prey)
        {
            if (prey == null) return;
            var p = prey.position + Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward * 45f;
            if (Physics.Raycast(p + Vector3.up * 200f, Vector3.down, out var hit, 400f, FlyLayers.SurfaceMask)) p = hit.point;
            transform.position = p;
            _home = p;
            _prey = prey;
            _cooldown = 0;
            _state = State.Stalk;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _cooldown -= dt;
            var env = FlyEnvironment.Current;
            float light = env != null ? env.LightLevel : 1f;
            Vector3 pos = transform.position;
            Vector3 face = transform.forward;
            float stride = 0;

            switch (_state)
            {
                case State.Watch:
                    if (_cooldown <= 0 && light > 0.35f)
                    {
                        _prey = NearestPrey(pos, 110f);
                        if (_prey != null) _state = State.Stalk;
                    }
                    if (_prey != null) face = _prey.position - pos;
                    break;

                case State.Stalk:
                {
                    if (_prey == null || PreyAirborne() || light < 0.25f)
                    {
                        _state = State.Retreat;
                        break;
                    }
                    Vector3 to = _prey.position - pos;
                    to.y = 0;
                    face = to;
                    float d = to.magnitude;
                    if (d > 160f)
                    {
                        _state = State.Retreat;
                        break;
                    }
                    // stop-and-go approach
                    bool go = Mathf.Repeat(Time.time * 0.7f, 1f) < 0.45f;
                    if (go && d > 16f)
                    {
                        float v = 9f * dt;
                        pos += to.normalized * v;
                        stride = v;
                    }
                    if (d <= 17f)
                    {
                        _state = State.Crouch;
                        _timer = 0.45f;
                    }
                    break;
                }

                case State.Crouch:
                    _timer -= dt;
                    if (_prey != null) face = _prey.position - pos;
                    _body.localPosition = new Vector3(0, 1.1f, 0);
                    if (_timer <= 0)
                    {
                        _state = State.Jump;
                        _jumpFrom = pos;
                        // jumping spiders are precise, but flies are faster: aim where the fly is now
                        _jumpTo = _prey != null ? _prey.position + Random.insideUnitSphere * 2.5f : pos + transform.forward * 15f;
                        _jumpT = 0;
                    }
                    break;

                case State.Jump:
                {
                    _jumpT += dt / 0.12f;
                    float t = Mathf.Clamp01(_jumpT);
                    pos = Vector3.Lerp(_jumpFrom, _jumpTo, t) + Vector3.up * (8f * t * (1 - t));
                    _body.localPosition = new Vector3(0, 1.6f, 0);
                    if (t >= 1)
                    {
                        _state = State.Retreat;
                        _cooldown = Random.Range(45f, 100f);
                    }
                    break;
                }

                case State.Retreat:
                {
                    Vector3 to = _home - pos;
                    to.y = 0;
                    if (to.magnitude < 3f)
                    {
                        _state = State.Watch;
                        break;
                    }
                    face = to;
                    float v = Mathf.Min(to.magnitude, 14f * dt);
                    pos += to.normalized * v;
                    stride = v;
                    break;
                }
            }

            if (_state != State.Jump && Physics.Raycast(pos + Vector3.up * 60f, Vector3.down, out var hit, 200f, FlyLayers.SurfaceMask))
                pos = hit.point;
            face.y = 0;
            var rot = face.sqrMagnitude > 1e-4f ? Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), 240f * dt) : transform.rotation;
            transform.SetPositionAndRotation(pos, rot);
            if (_state != State.Crouch && _state != State.Jump) _body.localPosition = Vector3.Lerp(_body.localPosition, new Vector3(0, 1.5f, 0), dt * 6);

            // legs: spread, stepping with the distance walked, tucked during the jump
            _phase += stride * 1.6f;
            for (int k = 0; k < 8; k++)
            {
                int side = k < 4 ? -1 : 1;
                int i = k % 4;
                float ph = _phase + (i % 2 == 0 == (side < 0) ? 0 : Mathf.PI);
                float swing = Mathf.Sin(ph) * 15f;
                float lift = Mathf.Max(0, Mathf.Cos(ph)) * 12f;
                float spread = side * (60f - i * 40f + swing);
                float tuck = _state == State.Jump ? 35f : 0;
                if (i == 0 && _state == State.Stalk) lift += 20f; // front legs raised when stalking
                _legs[k].localRotation = Quaternion.Euler(-20f - lift + tuck, spread + (i >= 2 ? side * 50f : 0), 0);
            }
        }

        bool PreyAirborne()
        {
            var m = _prey != null ? _prey.GetComponent<FlyMotor>() : null;
            return m != null && m.IsAirborne;
        }

        static Transform NearestPrey(Vector3 pos, float range)
        {
            Transform best = null;
            float bestD = range;
            foreach (var t in Wildlife.FlyBodies())
            {
                var m = t.GetComponent<FlyMotor>();
                if (m != null && m.IsAirborne) continue;
                float d = Vector3.Distance(t.position, pos);
                if (d < bestD && Mathf.Abs(t.position.y - pos.y) < 40f)
                {
                    bestD = d;
                    best = t;
                }
            }
            return best;
        }
    }

    // ====================================================================== bird

    /// <summary>A blackbird that dives over the fallen fruit and climbs away: a huge looming shadow for every fly below.</summary>
    public sealed class Bird : MonoBehaviour
    {
        public bool Active { get; private set; }
        Vector3 _a, _b, _c;
        float _t, _duration;
        Transform _wingL, _wingR;
        AudioSource _audio;

        public static Bird Create(Transform parent)
        {
            var go = new GameObject("Blackbird");
            go.transform.SetParent(parent, false);
            go.layer = FlyLayers.Creatures;
            var b = go.AddComponent<Bird>();
            b.Build();
            go.SetActive(false);
            return b;
        }

        void Build()
        {
            int L = FlyLayers.Creatures;
            var black = Wildlife.Mat("birdBlack", new Color(0.03f, 0.03f, 0.035f), 0.55f);
            var beak = Wildlife.Mat("birdBeak", new Color(0.95f, 0.6f, 0.05f), 0.5f);
            Wildlife.Part(transform, "Body", MeshFactory.Ellipsoid(new Vector3(45f, 42f, 110f), 20, 14), black, Vector3.zero, Quaternion.identity, L);
            Wildlife.Part(transform, "Head", MeshFactory.Ellipsoid(new Vector3(30f, 30f, 34f), 16, 12), black, new Vector3(0, 22f, 105f), Quaternion.identity, L);
            Wildlife.Part(transform, "Beak", MeshFactory.Segment(32f, 7f, 1.5f, 8), beak, new Vector3(0, 18f, 132f), Quaternion.Euler(8, 0, 0), L);
            Wildlife.Part(transform, "Tail", MeshFactory.Ellipsoid(new Vector3(30f, 5f, 70f), 12, 8), black, new Vector3(0, 8f, -150f), Quaternion.Euler(-10, 0, 0), L);
            _wingL = new GameObject("Wing L").transform;
            _wingL.SetParent(transform, false);
            _wingL.localPosition = new Vector3(-35f, 15f, 20f);
            Wildlife.Part(_wingL, "Feathers", MeshFactory.Ellipsoid(new Vector3(130f, 6f, 55f), 16, 8), black, new Vector3(-125f, 0, -10f), Quaternion.identity, L);
            _wingR = new GameObject("Wing R").transform;
            _wingR.SetParent(transform, false);
            _wingR.localPosition = new Vector3(35f, 15f, 20f);
            Wildlife.Part(_wingR, "Feathers", MeshFactory.Ellipsoid(new Vector3(130f, 6f, 55f), 16, 8), black, new Vector3(125f, 0, -10f), Quaternion.identity, L);
            SeenObject.Attach(gameObject, 90f, "птица");
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 0.6f;
            _audio.minDistance = 300f;
            _audio.maxDistance = 20000f;
            _audio.clip = FlyAudio.WingFlaps();
            _audio.loop = true;
            _audio.volume = 0.6f;
        }

        public void Swoop(Vector3 over)
        {
            float heading = Random.Range(0f, 360f);
            var dir = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            _a = over - dir * 3500f + Vector3.up * 1800f;
            _b = over + Vector3.up * Random.Range(110f, 220f) + Vector3.Cross(Vector3.up, dir) * Random.Range(-60f, 60f);
            _c = over + dir * 3500f + Vector3.up * 2200f;
            _t = 0;
            _duration = Random.Range(2.4f, 3.2f);
            Active = true;
            gameObject.SetActive(true);
            _audio.Play();
        }

        void Update()
        {
            if (!Active) return;
            _t += Time.deltaTime / _duration;
            float t = Mathf.Clamp01(_t);
            Vector3 p = Vector3.Lerp(Vector3.Lerp(_a, _b, t), Vector3.Lerp(_b, _c, t), t);
            Vector3 ahead = Vector3.Lerp(Vector3.Lerp(_a, _b, t + 0.01f), Vector3.Lerp(_b, _c, t + 0.01f), t + 0.01f);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(ahead - p));
            float flap = Mathf.Sin(Time.time * 2 * Mathf.PI * 7f) * 45f * (t > 0.35f && t < 0.6f ? 0.3f : 1f);
            _wingL.localRotation = Quaternion.Euler(0, 0, -flap);
            _wingR.localRotation = Quaternion.Euler(0, 0, flap);
            if (_t >= 1)
            {
                Active = false;
                _audio.Stop();
                gameObject.SetActive(false);
            }
        }
    }
}
