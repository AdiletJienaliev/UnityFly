using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyBrain
{
    public enum Taste { Sugar, Water, Bitter }

    /// <summary>
    /// A liquid on a surface: a drop of sugar water, fruit juice, dew or a bitter film. The drop is a spherical cap
    /// standing on its local XZ plane, so it can sit on any surface (transform.up = surface normal).
    /// Tasted by the legs and the labellum, consumed while feeding; natural sources refill or evaporate.
    /// </summary>
    public sealed class FoodSource : MonoBehaviour
    {
        public static readonly List<FoodSource> All = new List<FoodSource>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public Taste Taste;
        public float Concentration = 1f;
        public float Radius = 2f;
        public float Height = 1f;
        public float Volume = 1f;
        public float MaxVolume = 1f;
        /// <summary>Volume per world second added back (juice seeping from fruit flesh).</summary>
        public float Refill;
        /// <summary>Volume per world second lost (dew in the sun).</summary>
        public float Evaporation;
        public bool DestroyWhenEmpty = true;
        /// <summary>No drop mesh (the liquid is drawn by something else, e.g. the puddle surface).</summary>
        public bool Hidden
        {
            get => _hidden;
            set
            {
                _hidden = value;
                if (_mr != null) UpdateMesh();
            }
        }
        bool _hidden;
        public string Label;

        MeshFilter _mf;
        MeshRenderer _mr;
        float _shownVolume = -1;

        public static FoodSource Create(Transform parent, Taste taste, Vector3 position, Vector3 normal, float radius, float concentration = 1f,
                                        float heightRatio = 0.5f, Material material = null)
        {
            var go = new GameObject(taste + " drop");
            go.transform.SetParent(parent, false);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            go.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, normal.normalized) * Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            var f = go.AddComponent<FoodSource>();
            f.Taste = taste;
            f.Radius = radius;
            f.Height = radius * heightRatio;
            f.Concentration = concentration;
            f._mf = go.AddComponent<MeshFilter>();
            f._mr = go.AddComponent<MeshRenderer>();
            f._mr.shadowCastingMode = ShadowCastingMode.Off;
            f._mr.sharedMaterial = material != null ? material : DefaultMaterial(taste);
            go.layer = FlyLayers.Creatures;
            f.UpdateMesh();
            return f;
        }

        public static Material DefaultMaterial(Taste taste) => taste switch
        {
            Taste.Sugar => FlyMaterials.Transparent("sugarDrop", new Color(1f, 0.82f, 0.35f, 0.45f), new Color(1f, 0.95f, 0.8f, 0.55f)),
            Taste.Water => FlyMaterials.Transparent("waterDrop", new Color(0.55f, 0.75f, 1f, 0.22f), new Color(0.85f, 0.95f, 1f, 0.65f)),
            _ => FlyMaterials.Transparent("bitterDrop", new Color(0.55f, 0.25f, 0.75f, 0.5f), new Color(0.85f, 0.7f, 1f, 0.5f)),
        };

        float Scale => Mathf.Pow(Mathf.Max(Volume, 0.02f), 1f / 3f);
        public float CurrentRadius => Radius * Scale;
        public float CurrentHeight => Height * Scale;
        public bool IsEmpty => Volume <= 0.02f;

        /// <summary>Liquid surface point above p along the drop's normal, if p lies within the drop's footprint.</summary>
        public bool SurfacePoint(Vector3 p, out Vector3 surface)
        {
            surface = default;
            if (IsEmpty) return false;
            float r = CurrentRadius, h = CurrentHeight;
            Vector3 lp = transform.InverseTransformPoint(p);
            float x = Mathf.Sqrt(lp.x * lp.x + lp.z * lp.z);
            if (x >= r || lp.y < -r) return false;
            float R = (r * r + h * h) / (2 * h);
            float y = h - R + Mathf.Sqrt(Mathf.Max(0, R * R - x * x));
            surface = transform.TransformPoint(new Vector3(lp.x, y, lp.z));
            return true;
        }

        public bool Contains(Vector3 p, float tolerance = 0.05f)
        {
            if (!SurfacePoint(p, out var s)) return false;
            float above = Vector3.Dot(p - s, transform.up);
            float below = Vector3.Dot(p - transform.position, transform.up);
            return above <= tolerance && below >= -0.5f - tolerance;
        }

        public void Consume(float amount)
        {
            Volume = Mathf.Max(0, Volume - amount);
            if (IsEmpty && DestroyWhenEmpty) Destroy(gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Refill > 0 && Volume < MaxVolume) Volume = Mathf.Min(MaxVolume, Volume + Refill * dt);
            if (Evaporation > 0 && Volume > 0)
            {
                Volume = Mathf.Max(0, Volume - Evaporation * dt);
                if (IsEmpty && DestroyWhenEmpty) Destroy(gameObject);
            }
            if (Mathf.Abs(_shownVolume - Volume) > 0.01f) UpdateMesh();
        }

        void UpdateMesh()
        {
            _shownVolume = Volume;
            _mr.enabled = !IsEmpty && !Hidden;
            if (IsEmpty || Hidden) return;
            float r = CurrentRadius, h = CurrentHeight;
            float q = r > 6f ? 10f : 50f;
            _mf.sharedMesh = MeshFactory.Drop(Mathf.Round(r * q) / q, Mathf.Max(0.05f, Mathf.Round(h * 50) / 50));
        }
    }

    /// <summary>
    /// Anything the fly sees as a distinct object against the background: small moving things for LC10a
    /// (other flies, ants), and approaching bodies for the looming detectors LC4 / LPLC2 (spider jump, bird, threats).
    /// </summary>
    public sealed class SeenObject : MonoBehaviour
    {
        public static readonly List<SeenObject> All = new List<SeenObject>();
        void OnEnable()
        {
            All.Add(this);
            _last = transform.position;
        }
        void OnDisable() => All.Remove(this);

        /// <summary>Visual radius (mm).</summary>
        public float Radius = 1f;
        public string Kind = "object";
        /// <summary>Optional transform to exclude (the object's own fly body when it is one of the flies).</summary>
        public Transform Owner;
        public Vector3 Velocity { get; private set; }
        Vector3 _last;

        public static SeenObject Attach(GameObject go, float radius, string kind)
        {
            var s = go.AddComponent<SeenObject>();
            s.Radius = radius;
            s.Kind = kind;
            return s;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            var p = transform.position;
            Velocity = Vector3.Lerp(Velocity, (p - _last) / dt, 1 - Mathf.Exp(-dt * 20f));
            _last = p;
        }
    }

    /// <summary>A short puff of air (mm/s) that deflects the antennae and moves the Johnston's organ.</summary>
    public sealed class WindPuff : MonoBehaviour
    {
        public static readonly List<WindPuff> All = new List<WindPuff>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public Vector3 Direction;
        public float Speed = 400f;
        public float Duration = 0.8f;
        public float Width = 6f;
        public float Range = 60f;
        float _age;
        Transform[] _streaks;
        Vector3[] _offsets;

        public static WindPuff Create(Transform parent, Vector3 origin, Vector3 direction, float speed)
        {
            var go = new GameObject("Air puff");
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            var w = go.AddComponent<WindPuff>();
            w.Direction = direction.normalized;
            w.Speed = speed;
            w.BuildStreaks();
            return w;
        }

        public float Strength => _age < Duration ? Mathf.Sin(Mathf.Clamp01(_age / Duration) * Mathf.PI) : 0f;

        public Vector3 WindAt(Vector3 p)
        {
            Vector3 rel = p - transform.position;
            float along = Vector3.Dot(rel, Direction);
            if (along < 0 || along > Range) return Vector3.zero;
            float across = (rel - Direction * along).magnitude;
            float spread = Width + along * 0.25f;
            float f = Mathf.Exp(-(across * across) / (spread * spread)) * Mathf.Clamp01(1.2f - along / Range);
            return Direction * (Speed * Strength * f);
        }

        void BuildStreaks()
        {
            const int n = 28;
            _streaks = new Transform[n];
            _offsets = new Vector3[n];
            var mat = FlyMaterials.Unlit("windStreak", new Color(0.85f, 0.95f, 1f, 0.35f), FlyMaterials.RadialSoft());
            var rot = Quaternion.LookRotation(Direction);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("streak");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                _offsets[i] = rot * new Vector3(Random.Range(-1f, 1f) * Width, Random.Range(-1f, 1f) * Width * 0.6f, Random.Range(-10f, 5f));
                _streaks[i] = go.transform;
            }
        }

        void Update()
        {
            _age += Time.deltaTime;
            var cam = Camera.main;
            for (int i = 0; i < _streaks.Length; i++)
            {
                float travel = _age * Speed * 0.12f + i * 1.7f;
                Vector3 p = transform.position + _offsets[i] + Direction * Mathf.Repeat(travel, Range * 0.8f);
                _streaks[i].position = p;
                Vector3 toCam = cam != null ? (cam.transform.position - p).normalized : Vector3.up;
                Vector3 side = Vector3.Cross(Direction, toCam).normalized;
                if (side.sqrMagnitude > 0.001f) _streaks[i].rotation = Quaternion.LookRotation(-toCam, side);
                float fade = Strength;
                _streaks[i].localScale = new Vector3(4.5f, 0.35f, 1f) * (0.3f + 0.7f * fade);
            }
            if (_age > Duration + 0.2f) Destroy(gameObject);
        }
    }

    /// <summary>A dark object that approaches the fly: the classic looming stimulus for LC4 / LPLC2 and the giant fiber.</summary>
    public sealed class LoomingThreat : MonoBehaviour
    {
        public float Radius = 4f;
        public Vector3 Velocity;
        public float Life = 4f;

        public static LoomingThreat Create(Transform parent, Vector3 position, Vector3 velocity, float radius)
        {
            var go = new GameObject("Looming threat");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var t = go.AddComponent<LoomingThreat>();
            t.Radius = radius;
            t.Velocity = velocity;
            var visual = new GameObject("Sphere");
            visual.transform.SetParent(go.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Ellipsoid(Vector3.one * radius, 32, 20);
            visual.AddComponent<MeshRenderer>().sharedMaterial = FlyMaterials.Lit("threat", new Color(0.05f, 0.05f, 0.06f), 0.7f, new Color(0.3f, 0.3f, 0.3f));
            visual.layer = FlyLayers.Creatures;
            go.layer = FlyLayers.Creatures;
            SeenObject.Attach(go, radius, "угроза");
            return t;
        }

        void Update()
        {
            transform.position += Velocity * Time.deltaTime;
            if (FlyEnvironment.SurfaceBelow(transform.position, out var hit, 0f, Radius) && Velocity.y < 0) Velocity.y = Mathf.Abs(Velocity.y) * 0.3f;
            Life -= Time.deltaTime;
            if (Life <= 0) Destroy(gameObject);
        }
    }

    /// <summary>A small moving object (a mite) that circles around a center: drives LC10 object-tracking neurons.</summary>
    public sealed class MovingTarget : MonoBehaviour
    {
        public Vector3 Center;
        public Vector3 Up = Vector3.up;
        public float OrbitRadius = 7f;
        public float AngularSpeed = 1.2f;
        public float Size = 0.6f;
        public float Life = 12f;
        float _angle;

        public static MovingTarget Create(Transform parent, Vector3 center, Vector3 up, float startAngle)
        {
            var go = new GameObject("Moving target");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<MovingTarget>();
            m.Center = center;
            m.Up = up.sqrMagnitude > 0.1f ? up.normalized : Vector3.up;
            m._angle = startAngle;
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0, m.Size * 0.6f, 0);
            body.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Ellipsoid(new Vector3(0.45f, 0.35f, 0.7f) * m.Size * 1.4f, 16, 12);
            body.AddComponent<MeshRenderer>().sharedMaterial = FlyMaterials.Lit("mite", new Color(0.12f, 0.08f, 0.06f), 0.6f, new Color(0.25f, 0.2f, 0.2f));
            go.layer = body.layer = FlyLayers.Creatures;
            SeenObject.Attach(go, m.Size, "клещ");
            m.Update();
            return m;
        }

        void Update()
        {
            _angle += AngularSpeed * Time.deltaTime;
            var rot = Quaternion.FromToRotation(Vector3.up, Up);
            Vector3 p = Center + rot * (new Vector3(Mathf.Cos(_angle), 0, Mathf.Sin(_angle)) * OrbitRadius);
            if (Physics.Raycast(p + Up * 3f, -Up, out var hit, 8f, FlyLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) p = hit.point;
            Vector3 tangent = rot * new Vector3(-Mathf.Sin(_angle), 0, Mathf.Cos(_angle)) * Mathf.Sign(AngularSpeed);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(tangent, Up));
            Life -= Time.deltaTime;
            if (Life <= 0) Destroy(gameObject);
        }
    }

    /// <summary>A dust grain or pollen stuck to the head: stimulates eye bristle mechanosensory neurons until groomed off.</summary>
    public sealed class DustParticle : MonoBehaviour
    {
        public static readonly List<DustParticle> All = new List<DustParticle>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public int Side;       // -1 left, +1 right
        public bool OnEye;
        public Transform Head;

        public static DustParticle Create(Transform head, Vector3 localPosition, int side, bool onEye, Color? color = null)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(head, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Random.rotation;
            float s = Random.Range(0.035f, 0.07f);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Ellipsoid(new Vector3(1f, 0.7f, 0.85f), 8, 6);
            go.transform.localScale = Vector3.one * s;
            var mr = go.AddComponent<MeshRenderer>();
            var c = color ?? new Color(0.82f, 0.8f, 0.72f);
            mr.sharedMaterial = FlyMaterials.Lit("dust" + ColorUtility.ToHtmlStringRGB(c), c, 0.1f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.layer = FlyLayers.Flies;
            var d = go.AddComponent<DustParticle>();
            d.Side = side;
            d.OnEye = onEye;
            d.Head = head;
            return d;
        }

        /// <summary>Grains on a particular fly's head.</summary>
        public static int CountOn(Transform head, int side)
        {
            int n = 0;
            foreach (var d in All) if (d.Head == head && d.Side == side) n++;
            return n;
        }

        /// <summary>Knocked off by a grooming leg: falls to the ground.</summary>
        public void Detach()
        {
            transform.SetParent(null, true);
            enabled = false;
            gameObject.AddComponent<FallingGrain>();
        }
    }

    sealed class FallingGrain : MonoBehaviour
    {
        float _vy;
        float _life = 3f;

        void Update()
        {
            _vy -= 9810f * 0.02f * Time.deltaTime;
            var p = transform.position;
            float floor = FlyEnvironment.SurfaceBelow(p, out var hit, 0.5f, 50f) ? hit.point.y + 0.02f : p.y - 1f;
            p.y = Mathf.Max(floor, p.y + _vy * Time.deltaTime);
            p += Random.insideUnitSphere * 0.002f;
            transform.position = p;
            _life -= Time.deltaTime;
            if (_life <= 0) Destroy(gameObject);
        }
    }
}
