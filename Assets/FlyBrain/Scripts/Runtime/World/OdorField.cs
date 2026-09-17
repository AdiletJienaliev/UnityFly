using System.Collections.Generic;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>Fermenting fruit: emits the odor plume flies follow (vinegar, ethanol, esters).</summary>
    public sealed class OdorSource : MonoBehaviour
    {
        public static readonly List<OdorSource> All = new List<OdorSource>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public float Strength = 1f;
        public float Radius = 30f;
        public float Seed;

        public static OdorSource Attach(GameObject go, float strength, float radius)
        {
            var s = go.AddComponent<OdorSource>();
            s.Strength = strength;
            s.Radius = radius;
            s.Seed = Random.Range(0f, 100f);
            return s;
        }
    }

    /// <summary>
    /// Odor and humidity in the air. Odor from each fermenting fruit spreads as a diffusive halo plus a wind-borne
    /// plume that widens downwind and breaks into moving filaments (intermittency), which is what makes
    /// real plume tracking hard: flies surge upwind while they smell it and cast crosswind when they lose it.
    /// Olfaction is not fed into the connectome (see README: olfactory input makes this LIF model run away).
    /// </summary>
    public static class OdorField
    {
        public static float Concentration(Vector3 p)
        {
            if (OdorSource.All.Count == 0) return 0f;
            Vector3 wind = WindField.Instance != null ? WindField.Instance.Direction * WindField.Instance.Strength : Vector3.zero;
            float speed = wind.magnitude;
            Vector3 dir = speed > 1f ? wind / speed : Vector3.forward;
            float windy = Mathf.Clamp01(speed / 500f);
            float t = Time.time;
            float total = 0f;
            foreach (var s in OdorSource.All)
            {
                Vector3 rel = p - s.transform.position;
                float d = rel.magnitude;
                if (d > 2600f) continue;
                float halo = Mathf.Exp(-(d * d) / (2f * (s.Radius + 35f) * (s.Radius + 35f)));
                float x = Vector3.Dot(rel, dir);
                float plume = 0f;
                if (x > -s.Radius)
                {
                    float along = Mathf.Max(x, 0f);
                    float sigma = s.Radius * 0.6f + 14f + 0.2f * along;
                    Vector3 cross = rel - dir * x;
                    float fall = Mathf.Exp(-(cross.sqrMagnitude) / (2f * sigma * sigma));
                    plume = Mathf.Clamp01(45f / sigma) * fall * Mathf.Exp(-along / 2200f);
                    // filaments carried by the wind
                    float n = Mathf.PerlinNoise((along - speed * 0.9f * t) * 0.011f + s.Seed, cross.magnitude * 0.025f + s.Seed * 0.37f);
                    float filament = Mathf.Clamp01((n - 0.28f) / 0.44f);
                    plume *= Mathf.Lerp(0.05f, 1.5f, filament * filament) * windy;
                }
                total += s.Strength * Mathf.Max(halo, plume);
            }
            return total;
        }

        /// <summary>Near-field odor only (diffusion around the source, no wind-borne filaments): a smooth gradient at close range.</summary>
        public static float NearField(Vector3 p)
        {
            float total = 0f;
            foreach (var s in OdorSource.All)
            {
                float d = Vector3.Distance(p, s.transform.position);
                if (d > 400f) continue;
                float sigma = s.Radius + 35f;
                total += s.Strength * Mathf.Exp(-(d * d) / (2f * sigma * sigma));
            }
            return total;
        }

        /// <summary>Relative humidity excess near open water (0 dry .. ~1 at the water's edge).</summary>
        public static float Humidity(Vector3 p)
        {
            float h = 0f;
            foreach (var f in FoodSource.All)
            {
                if (f.Taste != Taste.Water || f.Volume <= 0.02f) continue;
                float d = Vector3.Distance(p, f.transform.position) - f.CurrentRadius;
                float scale = 10f + f.CurrentRadius * 0.9f;
                h += Mathf.Clamp01(f.Volume) * Mathf.Exp(-Mathf.Max(0, d) / scale) * Mathf.Clamp01(f.CurrentRadius / 3f + 0.3f);
                // open water humidifies the air far downwind and around it
                if (f.CurrentRadius > 10f) h += 0.25f * Mathf.Exp(-Mathf.Max(0, d) / 450f);
            }
            return Mathf.Clamp01(h);
        }
    }
}
