using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// Air movement near the ground: a breeze whose direction wanders slowly, gusts that travel with the wind,
    /// a boundary layer (air is almost still in the first millimeters above a surface) and the user's air puffs.
    /// Units are mm/s. Also drives grass and foliage sway through global shader properties.
    /// </summary>
    public sealed class WindField : MonoBehaviour
    {
        public static WindField Instance { get; private set; }

        [Tooltip("Breeze speed at 1 m above the ground (mm/s)")]
        public float BaseSpeed = 1100f;
        [Tooltip("Mean direction the wind blows towards (degrees, 0 = +Z)")]
        public float Heading = 60f;
        public DayCycle Clock;

        public Vector3 Direction { get; private set; } = Vector3.forward;
        public float Strength { get; private set; }
        public float Gust { get; private set; }

        static readonly int WindId = Shader.PropertyToID("_FlyWind");

        void OnEnable() => Instance = this;
        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float t = Time.time;
            float heading = Heading + 70f * (Mathf.PerlinNoise(t * 0.0035f, 1.3f) - 0.5f);
            Direction = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            float day = Clock != null ? Clock.Daylight : 1f;
            float afternoon = Clock != null ? Mathf.Clamp01(1f - Mathf.Abs(Clock.Hour - 15f) / 5f) : 0.5f;
            Strength = BaseSpeed * (0.3f + 0.45f * day + 0.35f * afternoon) * (0.7f + 0.6f * Mathf.PerlinNoise(t * 0.01f, 5.2f));
            float g = Mathf.PerlinNoise(t * 0.16f, 7.1f);
            Gust = 0.35f + 1.3f * g * g;
            Shader.SetGlobalVector(WindId, new Vector4(Direction.x, Direction.z, Strength / 1000f, Gust));
        }

        /// <summary>Breeze at a point, without puffs. <paramref name="heightAboveGround"/> sets the boundary layer.</summary>
        public Vector3 Breeze(Vector3 p, float heightAboveGround)
        {
            float t = Time.time;
            float travel = Strength * 0.001f * t;
            // gust cells ~0.5 m across drifting downwind
            float gx = p.x * 0.0021f - Direction.x * travel * 2.1f;
            float gz = p.z * 0.0021f - Direction.z * travel * 2.1f;
            float cell = Mathf.PerlinNoise(gx + 31.7f, gz + 11.3f);
            float gust = 0.35f + 1.3f * cell * cell * Gust;
            float h = Mathf.Max(0f, heightAboveGround);
            float layer = Mathf.Clamp(Mathf.Log(1f + h / 1.5f) / Mathf.Log(1f + 1000f / 1.5f), 0.02f, 1.25f);
            // small eddies change the local direction
            float swirl = (Mathf.PerlinNoise(gx * 3f + 5f, gz * 3f - t * 0.2f) - 0.5f) * 50f;
            return Quaternion.Euler(0, swirl, 0) * Direction * (Strength * layer * gust);
        }

        /// <summary>Total air velocity at a point: breeze plus air puffs.</summary>
        public static Vector3 At(Vector3 p, float heightAboveGround)
        {
            var w = Vector3.zero;
            if (Instance != null) w += Instance.Breeze(p, heightAboveGround);
            foreach (var puff in WindPuff.All) w += puff.WindAt(p);
            return w;
        }
    }
}
