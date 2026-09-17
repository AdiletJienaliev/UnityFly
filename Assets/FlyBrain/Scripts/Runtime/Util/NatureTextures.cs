using System.Threading.Tasks;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>Procedural textures for the orchard floor. Generated in parallel at startup.</summary>
    public static class NatureTextures
    {
        static float SStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        delegate Color PixelFn(float u, float v);

        static Texture2D Make(string name, int size, PixelFn fn, TextureWrapMode wrap = TextureWrapMode.Repeat, bool linear = false)
        {
            var px = new Color[size * size];
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++) px[y * size + x] = fn((x + 0.5f) / size, (y + 0.5f) / size);
            });
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = wrap, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Tileable noise (period 1 in u and v) built from Perlin noise on a torus-like blend.</summary>
        static float TileNoise(float u, float v, float freq, float seed)
        {
            float a = Perlin.Noise(u * freq + seed, v * freq + seed * 0.7f);
            float b = Perlin.Noise((u - 1) * freq + seed, v * freq + seed * 0.7f);
            float c = Perlin.Noise(u * freq + seed, (v - 1) * freq + seed * 0.7f);
            float d = Perlin.Noise((u - 1) * freq + seed, (v - 1) * freq + seed * 0.7f);
            float ab = Mathf.Lerp(a, b, u);
            float cd = Mathf.Lerp(c, d, u);
            return Mathf.Lerp(ab, cd, v);
        }

        static float TileFbm(float u, float v, float freq, float seed, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int k = 0; k < octaves; k++)
            {
                sum += amp * TileNoise(u, v, freq, seed + k * 13.1f);
                norm += amp;
                freq *= 2;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
        }

        /// <summary>Cellular pebbles: distance to the nearest jittered point (tileable with period cells).</summary>
        static float Cells(float u, float v, int cells, int seed, out float id)
        {
            float x = u * cells, y = v * cells;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float best = 10f;
            id = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = ix + dx, cy = iy + dy;
                    int wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
                    float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 1);
                    float d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d < best)
                    {
                        best = d;
                        id = Hash(wx, wy, seed + 2);
                    }
                }
            return Mathf.Sqrt(best);
        }

        /// <summary>Humus soil with crumbs, grit, rootlets and bits of old leaves (tile ~170 mm).</summary>
        public static Texture2D Soil() => Make("soil", 1024, (u, v) =>
        {
            float n = TileFbm(u, v, 5, 1.3f, 5);
            float n2 = TileFbm(u, v, 23, 9.7f, 3);
            var dark = new Color(0.16f, 0.12f, 0.085f);
            var mid = new Color(0.3f, 0.23f, 0.16f);
            var dry = new Color(0.44f, 0.36f, 0.26f);
            var c = Color.Lerp(dark, mid, SStep(0.25f, 0.6f, n));
            c = Color.Lerp(c, dry, SStep(0.55f, 0.85f, n) * 0.7f);
            c *= 0.85f + 0.3f * n2;
            // crumbs: irregular aggregates with a lit top edge
            float warp = (TileNoise(u, v, 60, 4.4f) - 0.5f) * 0.35f;
            float d = Cells(u + warp * 0.01f, v - warp * 0.01f, 110, 7, out float id);
            float crumb = SStep(0.42f + warp, 0.18f + warp, d) * SStep(0.35f, 0.7f, id);
            c = Color.Lerp(c, Color.Lerp(mid, dry, id) * (0.9f + 0.2f * id), crumb * 0.35f);
            // grit: rare small mineral grains
            float g = Cells(u, v, 260, 13, out float gid);
            if (gid > 0.93f) c = Color.Lerp(c, Color.Lerp(new Color(0.42f, 0.39f, 0.34f), new Color(0.55f, 0.5f, 0.43f), Hash((int)(gid * 9999), 1, 2)), SStep(0.3f, 0.12f, g) * 0.55f);
            // rootlets and fibers
            float fiber = Mathf.Abs(TileNoise(u, v, 18, 91.1f) - 0.5f);
            if (fiber < 0.006f && TileNoise(u, v, 7, 3.3f) > 0.62f) c = Color.Lerp(c, new Color(0.34f, 0.25f, 0.15f), 0.4f);
            // fragments of decayed leaves
            float leafBits = TileFbm(u, v, 11, 55.5f, 3);
            if (leafBits > 0.66f) c = Color.Lerp(c, new Color(0.36f, 0.24f, 0.12f), SStep(0.66f, 0.72f, leafBits) * 0.6f);
            c.a = 1;
            return c;
        });

        /// <summary>Neutral grey grain for world-space detail (value 0.5 = no change).</summary>
        public static Texture2D Grain() => Make("grain", 512, (u, v) =>
        {
            float d = Cells(u, v, 64, 5, out float id);
            float bump = SStep(0.55f, 0.05f, d) * (0.6f + 0.4f * id);
            float n = TileNoise(u, v, 32, 2.2f);
            float g = 0.42f + 0.16f * bump + 0.12f * (n - 0.5f) + 0.06f * (id - 0.5f);
            return new Color(g, g, g, 1);
        }, linear: true);

        /// <summary>Bark: vertical fissures and plates (u around, v along).</summary>
        public static Texture2D Bark() => Make("bark", 512, (u, v) =>
        {
            float warp = TileFbm(u, v, 3, 4.4f, 3);
            float fissure = Mathf.Abs(Mathf.Sin((u * 14 + warp * 2.5f) * Mathf.PI));
            float plates = TileNoise(u * 1f, v, 24, 8.1f);
            float t = SStep(0.0f, 0.35f, fissure) * (0.7f + 0.3f * plates);
            var c = Color.Lerp(new Color(0.16f, 0.13f, 0.1f), new Color(0.62f, 0.56f, 0.5f), t);
            float lichen = TileFbm(u, v, 5, 20.2f, 3);
            if (lichen > 0.62f) c = Color.Lerp(c, new Color(0.6f, 0.66f, 0.5f), SStep(0.62f, 0.7f, lichen) * 0.6f);
            c.a = t;
            return c;
        });

        /// <summary>Leaf blade: midrib and lateral veins (u across 0..1, v along 0..1).</summary>
        public static Texture2D LeafVeins() => Make("leafVeins", 256, (u, v) =>
        {
            float x = (u - 0.5f) * 2;
            float mid = SStep(0.05f, 0.0f, Mathf.Abs(x));
            float lateral = 0;
            float phase = v * 9 - Mathf.Abs(x) * 1.6f;
            float f = Mathf.Abs(Mathf.Repeat(phase, 1f) - 0.5f);
            lateral = SStep(0.06f, 0.0f, f) * SStep(0.95f, 0.2f, Mathf.Abs(x));
            float spots = TileNoise(u, v, 12, 3.3f);
            float g = 1f - 0.22f * mid - 0.12f * lateral - 0.18f * SStep(0.62f, 0.8f, spots);
            float edge = SStep(0.75f, 1f, Mathf.Abs(x)) * 0.25f; // dried edges
            return new Color(g - edge * 0.2f, g - edge * 0.35f, g - edge * 0.6f, 1);
        }, TextureWrapMode.Clamp);

        /// <summary>Apple / plum skin micro detail: fine streaks and pale lenticel dots (multiplies the vertex color).</summary>
        public static Texture2D Skin() => Make("skin", 512, (u, v) =>
        {
            float streak = TileNoise(u * 1f, v, 6, 1.1f) * 0.6f + TileNoise(u, v, 40, 7.3f) * 0.4f;
            float g = 0.82f + 0.16f * streak;
            float d = Cells(u, v, 42, 21, out float id);
            if (id > 0.35f) g = Mathf.Lerp(g, 1.12f, SStep(0.2f, 0.08f, d) * 0.8f);
            return new Color(g, g * 0.99f, g * 0.95f, 1);
        });

        /// <summary>
        /// Dappled light under a tree canopy, used as the directional light cookie. The canopy is centered on the
        /// texture corner (uv 0,0) because a directional cookie is anchored at the light's position and tiles.
        /// Small gaps between leaves project round sunflecks (pinhole images of the sun); larger openings give lit patches.
        /// </summary>
        public static Texture2D Canopy(float canopyRadiusUv)
        {
            const int cells = 260;
            // per-cell sunfleck parameters, computed once
            var chance = new float[cells * cells];
            Parallel.For(0, cells, cy =>
            {
                for (int cx = 0; cx < cells; cx++)
                    chance[cy * cells + cx] = SStep(0.4f, 0.66f, TileFbm((cx + 0.5f) / cells, (cy + 0.5f) / cells, 16, 3.1f, 4));
            });
            return Make("canopyCookie", 2048, (u, v) =>
            {
                float du = u > 0.5f ? u - 1 : u, dv = v > 0.5f ? v - 1 : v;
                float r = Mathf.Sqrt(du * du + dv * dv) / canopyRadiusUv;
                if (r > 1.9f) return new Color(1, 1, 1, 1);
                float edgeNoise = TileFbm(u, v, 5, 7.7f, 3) * 0.5f;
                float cover = SStep(1.2f + edgeNoise, 0.65f + edgeNoise, r);
                if (cover <= 0f) return new Color(1, 1, 1, 1);
                float field = TileFbm(u, v, 16, 3.1f, 3);
                float open = SStep(0.64f, 0.74f, field);
                float x = u * cells, y = v * cells;
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                float fleck = 0;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = ix + ox, cy = iy + oy;
                        int wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
                        if (Hash(wx, wy, 31) > chance[wy * cells + wx] * 0.8f) continue;
                        float px = cx + Hash(wx, wy, 32), py = cy + Hash(wx, wy, 33);
                        float rad = 0.18f + 0.3f * Hash(wx, wy, 34);
                        float dist = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y) * 1.15f);
                        fleck = Mathf.Max(fleck, SStep(rad, rad * 0.45f, dist) * (0.45f + 0.55f * Hash(wx, wy, 35)));
                    }
                float light = Mathf.Max(open, fleck);
                float value = Mathf.Lerp(1f, 0.3f + 0.7f * light, cover);
                return new Color(value, value, value, value);
            }, linear: true);
        }
    }
}
