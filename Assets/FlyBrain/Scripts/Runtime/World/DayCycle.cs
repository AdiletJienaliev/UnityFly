using UnityEngine;
using UnityEngine.Rendering;

namespace FlyBrain
{
    /// <summary>
    /// Day and night on the orchard floor: sun and moon path, sky, fog, ambient light and air temperature.
    /// World time is compressed: one day lasts <see cref="DayLengthSeconds"/> of world time, and the fly's
    /// physiology (hunger, thirst, sleep pressure, circadian clock) runs on the same compressed biological clock.
    /// </summary>
    public sealed class DayCycle : MonoBehaviour
    {
        [Tooltip("World seconds per 24 h")]
        public float DayLengthSeconds = 1440f;
        [Range(0, 24)] public float Hour = 7.2f;
        public int Day = 1;
        public bool Running = true;

        public Light Sun;
        public Material Sky;
        /// <summary>Sway of the canopy cookie (dappled light) in mm.</summary>
        public Vector3 CookieAnchor;

        /// <summary>0 night .. 1 full day.</summary>
        public float Daylight { get; private set; }
        /// <summary>Illumination for vision: daylight with a floor of moonlight.</summary>
        public float LightLevel => Mathf.Max(Daylight, 0.04f);
        public float SunElevation { get; private set; }
        public Vector3 SunDirection { get; private set; }
        public float Temperature { get; private set; }
        /// <summary>Biological seconds that pass per second of world time.</summary>
        public float BioTimeScale => 86400f / Mathf.Max(1f, DayLengthSeconds);
        public string TimeText => $"{Mathf.FloorToInt(Hour):00}:{Mathf.FloorToInt(Mathf.Repeat(Hour, 1f) * 60):00}";

        static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");
        static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        static readonly int DayId = Shader.PropertyToID("_Day");
        static readonly int DuskId = Shader.PropertyToID("_Dusk");
        static readonly int CloudId = Shader.PropertyToID("_CloudOffset");

        public string PhaseName
        {
            get
            {
                float h = Hour;
                if (h < 4.5f || h >= 21.8f) return "ночь";
                if (h < 6.5f) return "рассвет";
                if (h < 11f) return "утро";
                if (h < 15f) return "полдень";
                if (h < 19.8f) return "вечер";
                return "сумерки";
            }
        }

        void Update()
        {
            if (Running)
            {
                Hour += Time.deltaTime * 24f / Mathf.Max(1f, DayLengthSeconds);
                if (Hour >= 24f)
                {
                    Hour -= 24f;
                    Day++;
                }
            }
            Apply();
        }

        static float SStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        public const float Sunrise = 5.5f, Sunset = 20.5f;

        /// <summary>Phase 0..1 over the day (sunrise..sunset) and 1..2 over the night.</summary>
        static float DayPhase(float hour)
        {
            float h = Mathf.Repeat(hour - Sunrise, 24f);
            float day = Sunset - Sunrise;
            return h < day ? h / day : 1f + (h - day) / (24f - day);
        }

        static Vector3 CelestialDirection(float hour, float maxElevation, out float elevation)
        {
            // late summer: rises in the east (+x) at 5:30, culminates in the south (-z), sets in the west (-x) at 20:30
            float u = DayPhase(hour);
            float phi = Mathf.PI * u;
            elevation = u <= 1f ? maxElevation * Mathf.Sin(phi) : 40f * Mathf.Sin(phi);
            float e = elevation * Mathf.Deg2Rad;
            var horizontal = new Vector3(Mathf.Cos(phi), 0, -Mathf.Sin(phi) * 0.85f + 0.25f).normalized;
            return (horizontal * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e)).normalized;
        }

        public void Apply()
        {
            var sunDir = CelestialDirection(Hour, 64f, out float sunElev);
            var moonDir = CelestialDirection(Hour + 12f, 42f, out float moonElev);
            if (DayPhase(Hour) > 1f) moonElev = Mathf.Max(moonElev, 25f) ;
            SunElevation = sunElev;
            SunDirection = sunDir;
            Daylight = SStep(-5f, 12f, sunElev);
            float dusk = Mathf.Clamp01(1f - Mathf.Abs(sunElev - 2f) / 14f); // golden hour and twilight

            // one shadow-casting directional light: the sun by day, the moon by night
            float sunI = 1.3f * SStep(-3f, 14f, sunElev);
            float moonI = 0.26f * SStep(-4f, 12f, moonElev) * (1f - Daylight);
            bool useSun = sunI >= moonI;
            if (Sun != null)
            {
                var src = useSun ? sunDir : moonDir;
                // keep the light a little above the horizon so shadows never stretch to infinity
                if (Vector3.Dot(src, Vector3.up) < 0.12f) src = (Vector3.ProjectOnPlane(src, Vector3.up).normalized * 0.99f + Vector3.up * 0.12f).normalized;
                Sun.transform.SetPositionAndRotation(CookieAnchor, Quaternion.LookRotation(-src, Vector3.up));
                Sun.intensity = useSun ? sunI : moonI;
                var warm = new Color(1f, 0.52f, 0.26f);
                var white = new Color(1f, 0.95f, 0.87f);
                Sun.color = useSun ? Color.Lerp(warm, white, SStep(2f, 28f, sunElev)) : new Color(0.62f, 0.72f, 1f);
                Sun.shadowStrength = useSun ? Mathf.Lerp(0.55f, 0.85f, Daylight) : 0.6f;
                Sun.enabled = Sun.intensity > 0.002f;
            }

            // ambient
            var daySky = new Color(0.5f, 0.6f, 0.75f);
            var dayEq = new Color(0.46f, 0.45f, 0.4f);
            var dayGround = new Color(0.22f, 0.19f, 0.14f);
            var duskSky = new Color(0.42f, 0.36f, 0.45f);
            var duskEq = new Color(0.55f, 0.36f, 0.26f);
            var nightSky = new Color(0.06f, 0.08f, 0.14f);
            var nightEq = new Color(0.04f, 0.05f, 0.08f);
            var nightGround = new Color(0.015f, 0.018f, 0.025f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(Color.Lerp(nightSky, daySky, Daylight), duskSky, dusk * 0.5f);
            RenderSettings.ambientEquatorColor = Color.Lerp(Color.Lerp(nightEq, dayEq, Daylight), duskEq, dusk * 0.5f);
            RenderSettings.ambientGroundColor = Color.Lerp(nightGround, dayGround, Daylight);

            // horizon haze (the sky shader uses the same palette)
            var dayHorizon = new Color(0.7f, 0.78f, 0.86f);
            var duskHorizon = new Color(0.95f, 0.6f, 0.38f);
            var nightHorizon = new Color(0.03f, 0.04f, 0.07f);
            var horizon = Color.Lerp(Color.Lerp(nightHorizon, dayHorizon, Daylight), duskHorizon, dusk * 0.6f * Mathf.Max(Daylight, 0.3f));
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = horizon;
            float mist = SStep(3.5f, 5.5f, Hour) * (1f - SStep(6.5f, 9.5f, Hour)); // morning mist
            RenderSettings.fogDensity = 0.00022f + 0.00035f * mist;

            // air temperature: coolest before dawn, warmest mid-afternoon
            Temperature = 19f + 7f * Mathf.Sin(Mathf.PI * 2f * (Hour - 9f) / 24f);

            if (Sky != null)
            {
                Sky.SetVector(SunDirId, sunDir);
                Sky.SetVector(MoonDirId, moonDir);
                Sky.SetColor(SunColorId, Sun != null && useSun ? Sun.color : new Color(1f, 0.6f, 0.35f));
                Sky.SetFloat(DayId, Daylight);
                Sky.SetFloat(DuskId, dusk);
                Sky.SetVector(CloudId, new Vector4(Time.time * 0.0035f, Time.time * 0.0012f, 0, 0));
            }
        }
    }
}
