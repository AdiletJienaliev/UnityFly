using System.Collections.Generic;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>Runtime materials and procedural textures (shaders live in Resources/Shaders so builds include them).</summary>
    public static class FlyMaterials
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();

        public static Shader GetShader(string name)
        {
            if (Shaders.TryGetValue(name, out var s) && s != null) return s;
            s = Resources.Load<Shader>("Shaders/" + name);
            if (s == null) s = UnityEngine.Shader.Find("Standard");
            return Shaders[name] = s;
        }

        public static Material Lit(string key, Color color, float smoothness = 0.5f, Color? specular = null, Texture2D tex = null,
                                   Color? rim = null, float rimPower = 3f, Color? emission = null)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GetShader("FlyBrainLit")) { name = key };
            m.SetColor("_Color", color);
            m.SetFloat("_Glossiness", smoothness);
            m.SetColor("_SpecColor2", specular ?? new Color(0.15f, 0.15f, 0.15f));
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetColor("_RimColor", rim ?? Color.clear);
            m.SetFloat("_RimPower", rimPower);
            m.SetColor("_EmissionColor", emission ?? Color.clear);
            return Cache[key] = m;
        }

        public static Material Transparent(string key, Color color, Color fresnel, float smoothness = 0.95f)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GetShader("FlyBrainTransparent")) { name = key };
            m.SetColor("_Color", color);
            m.SetColor("_FresnelColor", fresnel);
            m.SetFloat("_Glossiness", smoothness);
            return Cache[key] = m;
        }

        public static Material Unlit(string key, Color color, Texture2D tex = null)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GetShader("FlyBrainUnlit")) { name = key };
            m.SetColor("_Color", color);
            if (tex != null) m.SetTexture("_MainTex", tex);
            return Cache[key] = m;
        }

        public static Material Wing()
        {
            if (Cache.TryGetValue("wing", out var m) && m != null) return m;
            m = new Material(GetShader("FlyBrainWing")) { name = "wing" };
            m.SetTexture("_MainTex", WingTexture());
            m.SetColor("_Color", new Color(0.74f, 0.75f, 0.78f, 0.32f));
            m.SetFloat("_Iridescence", 0.3f);
            m.SetColor("_VeinColor", new Color(0.3f, 0.24f, 0.18f, 0.85f));
            return Cache["wing"] = m;
        }

        public static Material WingBlur()
        {
            if (Cache.TryGetValue("wingBlur", out var m) && m != null) return m;
            const int w = 128, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "wingBlurTex", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    // wings dwell at the stroke reversals, so the blur is denser at both ends of the arc
                    float ends = 0.3f + 0.7f * Mathf.Pow(Mathf.Abs(2 * u - 1), 5);
                    float radial = SStep(0.05f, 0.35f, v) * (1 - SStep(0.8f, 1f, v));
                    px[y * w + x] = new Color(1, 1, 1, ends * radial);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            m = new Material(GetShader("FlyBrainUnlit")) { name = "wingBlur" };
            m.SetColor("_Color", new Color(0.85f, 0.88f, 0.92f, 0.22f));
            m.SetTexture("_MainTex", tex);
            return Cache["wingBlur"] = m;
        }

        // ------------------------------------------------------------------ fly materials

        public static Material Thorax() => Lit("thorax", new Color(0.42f, 0.33f, 0.22f), 0.45f, new Color(0.12f, 0.1f, 0.08f),
            Noise("thoraxTex", 128, new Color(0.62f, 0.52f, 0.4f), new Color(0.42f, 0.33f, 0.25f), 9f, stripes: false), new Color(0.35f, 0.3f, 0.2f), 3f);
        public static Material Abdomen(bool male = false) => male
            ? Lit("abdomenMale", Color.white, 0.5f, new Color(0.15f, 0.12f, 0.1f), AbdomenTexture(true), new Color(0.3f, 0.25f, 0.15f), 3f)
            : Lit("abdomen", Color.white, 0.5f, new Color(0.15f, 0.12f, 0.1f), AbdomenTexture(false), new Color(0.3f, 0.25f, 0.15f), 3f);
        public static Material AbdomenTip() => Lit("abdomenTip", new Color(0.2f, 0.14f, 0.1f), 0.5f, new Color(0.15f, 0.12f, 0.1f), null, new Color(0.3f, 0.25f, 0.15f), 3f);
        public static Material Eye() => Lit("eye", Color.white, 0.75f, new Color(0.35f, 0.2f, 0.2f), EyeTexture(), new Color(0.6f, 0.1f, 0.05f), 2.5f);
        public static Material Head() => Lit("head", new Color(0.62f, 0.48f, 0.3f), 0.4f, new Color(0.1f, 0.08f, 0.05f));
        public static Material Leg() => Lit("leg", new Color(0.52f, 0.4f, 0.26f), 0.35f, new Color(0.08f, 0.07f, 0.05f), null, new Color(0.25f, 0.2f, 0.12f), 4f);
        public static Material Dark() => Lit("dark", new Color(0.08f, 0.06f, 0.05f), 0.6f, new Color(0.2f, 0.2f, 0.2f));
        public static Material Proboscis() => Lit("proboscis", new Color(0.72f, 0.6f, 0.42f), 0.5f, new Color(0.12f, 0.1f, 0.08f));

        // ------------------------------------------------------------------ textures

        /// <summary>GLSL-style smoothstep (Unity's Mathf.SmoothStep interpolates between values instead).</summary>
        static float SStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3 - 2 * t);
        }

        public static Texture2D Noise(string key, int size, Color a, Color b, float scale, bool stripes)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = key, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float n = Mathf.PerlinNoise(u * scale + 13.1f, v * scale + 7.7f) * 0.7f + Mathf.PerlinNoise(u * scale * 4, v * scale * 4) * 0.3f;
                    if (stripes) n = Mathf.Clamp01(n * 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(v * scale * 6.28f)));
                    px[y * size + x] = Color.Lerp(a, b, n);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static Texture2D AbdomenTexture(bool male)
        {
            // Drosophila tergites: pale anterior band, dark posterior band on each segment (UV.y runs front -> back)
            const int w = 64, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "abdomenTex", wrapMode = TextureWrapMode.Clamp };
            var pale = new Color(0.78f, 0.66f, 0.45f);
            var dark = new Color(0.14f, 0.09f, 0.06f);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (float)y / h;
                    float u = (float)x / w;
                    float seg = Mathf.Repeat((v - 0.08f) * 6.2f, 1f);
                    float band = SStep(0.5f, 0.64f, seg) * (1 - SStep(0.93f, 1f, seg));
                    // dorsal side (around u = 0.25 of the parametrization) is darker than the ventral side
                    float dorsal = Mathf.Clamp01(Mathf.Sin(u * Mathf.PI * 2) * 2.2f + 0.45f);
                    band *= dorsal;
                    if (v > 0.84f) band = Mathf.Max(band, dorsal * 0.85f);
                    if (male && v > 0.62f) band = Mathf.Max(band, 0.92f); // the dark posterior tergites of males
                    float noise = Mathf.PerlinNoise(u * 20, v * 40) * 0.15f;
                    var c = Color.Lerp(pale, dark, Mathf.Clamp01(band + noise));
                    c.a = 1;
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static Texture2D EyeTexture()
        {
            // compound eye: hexagonal facet lattice
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "eyeTex", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[size * size];
            var baseCol = new Color(0.62f, 0.05f, 0.03f);
            var edge = new Color(0.32f, 0.02f, 0.02f);
            const float facets = 28f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size * facets, v = (float)y / size * facets * 0.866f;
                    float row = Mathf.Floor(v / 0.866f);
                    float offset = (row % 2) * 0.5f;
                    float cx = Mathf.Floor(u + offset) - offset + 0.5f;
                    float cy = (row + 0.5f) * 0.866f;
                    float d = new Vector2(u - cx, (v - cy) * 1.15f).magnitude;
                    float t = SStep(0.32f, 0.5f, d);
                    var c = Color.Lerp(baseCol, edge, t);
                    c.a = 1 - 0.7f * t; // glossy facets, rougher edges
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static Texture2D WingTexture()
        {
            // R = inside of the wing outline, A = membrane (1) vs vein (0). UV.x along the wing, UV.y across.
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "wingTex", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = (float)x / (w - 1);   // 0 at hinge, 1 at tip
                    float t = (float)y / (h - 1);   // 0 leading edge, 1 trailing edge
                    // outline: narrow at the hinge, broad rounded blade
                    float half = 0.5f * Mathf.Sqrt(Mathf.Clamp01(s * 1.3f)) * Mathf.Sqrt(Mathf.Clamp01((1 - s) * 5f));
                    half = Mathf.Max(half, 0.06f * (1 - s));
                    float center = 0.42f + 0.1f * s;
                    float dist = Mathf.Abs(t - center) / Mathf.Max(half, 1e-3f);
                    float inside = 1 - SStep(0.9f, 1.0f, dist);

                    float vein = 0;
                    // longitudinal veins L1..L5 and costa (leading edge)
                    float[] veins = { center - half * 0.97f, center - half * 0.55f, center - half * 0.15f, center + half * 0.25f, center + half * 0.62f };
                    foreach (var vy in veins)
                        vein = Mathf.Max(vein, 1 - SStep(0.008f, 0.02f, Mathf.Abs(t - vy)));
                    // cross veins
                    vein = Mathf.Max(vein, (1 - SStep(0.004f, 0.012f, Mathf.Abs(s - 0.42f))) * (t > veins[2] && t < veins[3] ? 1 : 0));
                    vein = Mathf.Max(vein, (1 - SStep(0.004f, 0.012f, Mathf.Abs(s - 0.58f))) * (t > veins[3] && t < veins[4] ? 1 : 0));
                    // veins fade near the tip
                    vein *= Mathf.Clamp01((0.97f - s) * 8f);

                    px[y * w + x] = new Color(inside, inside, inside, 1 - vein * 0.9f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D WoodTexture()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "wood", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            var px = new Color[size * size];
            var light = new Color(0.78f, 0.62f, 0.44f);
            var dark = new Color(0.55f, 0.38f, 0.24f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float warp = Mathf.PerlinNoise(u * 3, v * 0.6f) * 2.5f;
                    float grain = 0.5f + 0.5f * Mathf.Sin((u * 38 + warp * 6) * Mathf.PI);
                    grain = Mathf.Pow(grain, 3);
                    float fine = Mathf.PerlinNoise(u * 120, v * 6) * 0.35f;
                    var c = Color.Lerp(light, dark, Mathf.Clamp01(grain * 0.7f + fine));
                    c.a = 0.5f + 0.5f * (1 - grain);
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D RadialSoft()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "soft", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color(1, 1, 1, a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
