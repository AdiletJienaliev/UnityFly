using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace FlyBrain
{
    /// <summary>Growable mesh with colors and two UV channels.</summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Vector3> UV2 = new List<Vector3>();
        public readonly List<int> T = new List<int>();
        public bool UseUV2;

        public int Count => V.Count;

        public int Add(Vector3 p, Vector3 n, Color c, Vector2 uv, Vector3 uv2 = default)
        {
            V.Add(p);
            N.Add(n);
            C.Add(c);
            UV.Add(uv);
            if (UseUV2) UV2.Add(uv2);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            T.Add(a);
            T.Add(b);
            T.Add(c);
        }

        public void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        public Mesh Build(string name, bool recalcNormals = false)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetColors(C);
            m.SetUVs(0, UV);
            if (UseUV2) m.SetUVs(1, UV2);
            m.SetTriangles(T, 0);
            if (recalcNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    /// <summary>Procedural meshes for the orchard floor (1 unit = 1 mm).</summary>
    public static class NatureMeshes
    {
        public static float Noise(float x, float y) => Mathf.PerlinNoise(x + 1000.3f, y + 1000.7f);

        public static float Fbm(float x, float y, int octaves = 4)
        {
            float v = 0, a = 0.5f, norm = 0;
            for (int k = 0; k < octaves; k++)
            {
                v += a * Noise(x, y);
                norm += a;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f + 3.7f;
                a *= 0.5f;
            }
            return v / norm;
        }

        static float SStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>Height field grid centered at <paramref name="center"/>; triangles inside <paramref name="hole"/> (half size) are skipped.</summary>
        public static Mesh Terrain(string name, Vector2 center, float size, int cells, Func<float, float, float> height,
                                   Func<float, float, Vector3, Color> color, float hole = 0f)
        {
            var b = new MeshBuilder();
            float step = size / cells;
            float x0 = center.x - size * 0.5f, z0 = center.y - size * 0.5f;
            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    float y = height(x, z);
                    float e = Mathf.Max(step * 0.5f, 1f);
                    var n = new Vector3(height(x - e, z) - height(x + e, z), 2 * e, height(x, z - e) - height(x, z + e)).normalized;
                    b.Add(new Vector3(x, y, z), n, color(x, z, n), new Vector2(x, z));
                }
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    if (hole > 0)
                    {
                        float cx = x0 + (i + 0.5f) * step - center.x, cz = z0 + (j + 0.5f) * step - center.y;
                        if (Mathf.Abs(cx) < hole && Mathf.Abs(cz) < hole) continue;
                    }
                    int a = j * (cells + 1) + i;
                    int c = a + cells + 1;
                    b.Quad(a, c, c + 1, a + 1);
                }
            return b.Build(name);
        }

        // ------------------------------------------------------------------ surfaces of revolution (fruit, trunk)

        /// <summary>
        /// Closed surface of revolution around +Y. <paramref name="shape"/>(phi, t) returns (radius, height) for t in [0,1]
        /// (0 = bottom pole, 1 = top pole); <paramref name="color"/> receives the unit direction from the axis.
        /// </summary>
        public static Mesh Lathe(string name, int segments, int rings, Func<float, float, Vector2> shape,
                                 Func<float, float, Vector3, Color> color, Vector2 uvScale, bool capBottom = true, bool capTop = true)
        {
            var b = new MeshBuilder();
            Vector3 P(float phi, float t)
            {
                var s = shape(phi, t);
                return new Vector3(Mathf.Cos(phi) * s.x, s.y, Mathf.Sin(phi) * s.x);
            }
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float phi = Mathf.PI * 2 * s / segments;
                    var p = P(phi, t);
                    float dp = Mathf.PI * 2 / segments * 0.5f, dt = 0.5f / rings;
                    var tu = P(phi + dp, t) - P(phi - dp, t);
                    var tv = P(phi, Mathf.Min(1, t + dt)) - P(phi, Mathf.Max(0, t - dt));
                    var n = Vector3.Cross(tv, tu);
                    if (n.sqrMagnitude < 1e-10f) n = t < 0.5f ? Vector3.down : Vector3.up;
                    n.Normalize();
                    if ((r == 0 && capBottom) || (r == rings && capTop)) n = r == 0 ? Vector3.down : Vector3.up;
                    b.Add(p, n, color(phi, t, n), new Vector2((float)s / segments * uvScale.x, t * uvScale.y));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s, c = a + segments + 1;
                    b.Quad(a, c, c + 1, a + 1);
                }
            return b.Build(name);
        }

        /// <summary>A spot on a fruit (rot, wound or mold), as a direction from the center and an angular radius.</summary>
        public struct FruitSpot
        {
            public Vector3 Dir;
            public float Angle;     // radians
            public float Depth;     // mm, inward
            public int Kind;        // 0 rot, 1 wound (flesh), 2 mold (bitter)
        }

        public sealed class FruitShape
        {
            public string Name;
            public float Radius, Height;
            public Color SkinA, SkinB;
            public float Gloss = 0.55f;
            public int Kind; // 0 apple, 1 pear, 2 plum, 3 cherry
            public readonly List<FruitSpot> Spots = new List<FruitSpot>();
            public int Seed;

            /// <summary>Local surface point and normal in the direction <paramref name="dir"/> (fruit space, +Y = stem).</summary>
            public void Surface(Vector3 dir, out Vector3 point, out Vector3 normal)
            {
                dir.Normalize();
                float phi = Mathf.Atan2(dir.z, dir.x);
                // invert the profile numerically: find t whose profile direction matches the polar angle
                float targetPolar = Mathf.Acos(Mathf.Clamp(dir.y, -1, 1));
                float lo = 0, hi = 1;
                for (int k = 0; k < 24; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var s = Profile(phi, mid);
                    float polar = Mathf.Atan2(s.x, s.y - Height * 0.5f);
                    if (polar > targetPolar) lo = mid;
                    else hi = mid;
                }
                float t = (lo + hi) * 0.5f;
                point = Point(phi, t);
                const float e = 0.01f;
                var tu = Point(phi + e, t) - Point(phi - e, t);
                var tv = Point(phi, Mathf.Min(1, t + e)) - Point(phi, Mathf.Max(0, t - e));
                normal = Vector3.Cross(tv, tu).normalized;
                if (Vector3.Dot(normal, point - new Vector3(0, Height * 0.5f, 0)) < 0) normal = -normal;
            }

            Vector3 Point(float phi, float t)
            {
                var s = Profile(phi, t);
                return new Vector3(Mathf.Cos(phi) * s.x, s.y, Mathf.Sin(phi) * s.x);
            }

            public Vector2 Profile(float phi, float t)
            {
                float polar = Mathf.PI * (1 - t); // bottom pole t=0 -> pi, top pole t=1 -> 0
                float rho, y;
                switch (Kind)
                {
                    case 1: // pear: broad bottom, narrow neck
                    {
                        float bulge = Mathf.Sin(polar) * Mathf.Lerp(1f, 0.55f, SStep(0.45f, 0.95f, t));
                        rho = Radius * bulge * (1 + 0.03f * Mathf.Sin(phi * 3 + Seed));
                        y = Height * t + Radius * 0.08f * Mathf.Sin(t * Mathf.PI);
                        break;
                    }
                    case 2: // plum: ellipsoid with a suture groove
                    {
                        float groove = 1 - 0.06f * Mathf.Exp(-Mathf.Pow(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, 0) / 12f, 2));
                        rho = Radius * Mathf.Sin(polar) * groove;
                        y = Height * 0.5f + Height * 0.5f * Mathf.Cos(polar);
                        break;
                    }
                    case 3: // cherry
                    {
                        rho = Radius * Mathf.Sin(polar);
                        y = Height * 0.5f + Height * 0.5f * Mathf.Cos(polar);
                        break;
                    }
                    default: // apple: dimples at the stem and the calyx, faint lobes
                    {
                        float lobes = 1 + 0.025f * Mathf.Cos(5 * phi + Seed) * Mathf.Sin(polar) * SStep(0.2f, 0.9f, polar / Mathf.PI);
                        rho = Radius * Mathf.Sin(polar) * lobes * (1 + 0.04f * Mathf.Sin(polar * 2));
                        float dimpleTop = 0.22f * Mathf.Exp(-Mathf.Pow(polar / 0.38f, 2));
                        float dimpleBottom = 0.12f * Mathf.Exp(-Mathf.Pow((Mathf.PI - polar) / 0.42f, 2));
                        y = Height * 0.5f + Height * 0.5f * Mathf.Cos(polar) * (1 - dimpleTop - dimpleBottom);
                        y -= Height * dimpleTop * 0.5f * Mathf.Cos(polar);
                        break;
                    }
                }
                // spots: soft rot collapses, wounds are craters
                var dir = new Vector3(Mathf.Cos(phi) * Mathf.Sin(polar), Mathf.Cos(polar), Mathf.Sin(phi) * Mathf.Sin(polar));
                float sink = 0;
                foreach (var sp in Spots)
                {
                    float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, sp.Dir), -1, 1));
                    if (a > sp.Angle * 1.3f) continue;
                    float u = a / sp.Angle;
                    float profile = sp.Kind == 1 ? Mathf.Clamp01(1 - u * u) * (1 + 0.15f * Noise(phi * 6, t * 6)) : SStep(1.25f, 0.2f, u);
                    sink = Mathf.Max(sink, sp.Depth * profile);
                }
                if (sink > 0)
                {
                    var p = new Vector3(Mathf.Cos(phi) * rho, y - Height * 0.5f, Mathf.Sin(phi) * rho);
                    float len = p.magnitude;
                    if (len > 1e-3f)
                    {
                        p *= Mathf.Max(0.2f, (len - sink) / len);
                        rho = new Vector2(p.x, p.z).magnitude;
                        y = p.y + Height * 0.5f;
                    }
                }
                return new Vector2(rho, y);
            }

            public Color ColorAt(float phi, float t, Vector3 n)
            {
                float polar = Mathf.PI * (1 - t);
                var dir = new Vector3(Mathf.Cos(phi) * Mathf.Sin(polar), Mathf.Cos(polar), Mathf.Sin(phi) * Mathf.Sin(polar));
                // skin: blush on one side, streaks and lenticels
                float blush = Mathf.Clamp01(0.5f + 0.5f * Vector3.Dot(dir, new Vector3(0.6f, 0.3f, 0.75f).normalized) + (Noise(phi * 2 + Seed, t * 3) - 0.5f) * 0.6f);
                float streak = Noise(phi * 18 + Seed, t * 2.5f) * 0.35f;
                var c = Color.Lerp(SkinA, SkinB, Mathf.Clamp01(blush + streak - 0.15f));
                if (Kind == 2) c = Color.Lerp(c, new Color(0.55f, 0.55f, 0.75f), Noise(phi * 9, t * 9) * 0.35f); // bloom
                c.a = Gloss;

                foreach (var sp in Spots)
                {
                    float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, sp.Dir), -1, 1));
                    float u = a / sp.Angle;
                    if (u > 1.35f) continue;
                    float edge = Noise(phi * 7 + sp.Dir.x * 10, t * 7) * 0.25f;
                    float inside = SStep(1.2f + edge, 0.85f + edge, u);
                    if (inside <= 0) continue;
                    Color spot;
                    if (sp.Kind == 1)
                    {
                        // exposed flesh browning towards the rim, glistening juice
                        var flesh = new Color(0.93f, 0.84f, 0.58f);
                        var brown = new Color(0.55f, 0.36f, 0.16f);
                        spot = Color.Lerp(flesh, brown, SStep(0.3f, 1.0f, u) * 0.8f + Noise(phi * 20, t * 20) * 0.25f);
                        spot.a = 0.95f;
                    }
                    else if (sp.Kind == 2)
                    {
                        // blue-green Penicillium with a white fringe
                        var mold = Color.Lerp(new Color(0.35f, 0.55f, 0.45f), new Color(0.55f, 0.72f, 0.62f), Noise(phi * 30, t * 30));
                        spot = Color.Lerp(mold, new Color(0.92f, 0.92f, 0.88f), SStep(0.6f, 1.0f, u));
                        spot.a = 0.05f;
                    }
                    else
                    {
                        // brown rot with concentric rings and white yeast pustules
                        float ring = 0.5f + 0.5f * Mathf.Sin(u * 22f + Noise(phi * 4, t * 4) * 4);
                        spot = Color.Lerp(new Color(0.3f, 0.17f, 0.07f), new Color(0.48f, 0.3f, 0.12f), ring * 0.6f + u * 0.3f);
                        float yeast = Noise(phi * 45 + 3, t * 45);
                        if (yeast > 0.72f && u < 0.85f) spot = Color.Lerp(spot, new Color(0.88f, 0.85f, 0.75f), 0.8f);
                        spot.a = 0.75f;
                    }
                    c = Color.Lerp(c, spot, inside);
                }
                return c;
            }

            public Mesh Build(int segments = 64, int rings = 40)
            {
                return Lathe(Name, segments, rings, Profile, ColorAt, new Vector2(4, 2));
            }
        }

        /// <summary>Tree trunk with buttress roots and bark ridges; UV.x around, UV.y along (mm / 300).</summary>
        public static Mesh Trunk(float radius, float height, int seed)
        {
            Vector2 Shape(float phi, float t)
            {
                float y = t * height;
                float flare = 1 + 0.9f * Mathf.Exp(-y / 70f);
                float roots = 0;
                for (int k = 0; k < 5; k++)
                {
                    float a = Mathf.DeltaAngle(phi * Mathf.Rad2Deg, k * 72 + seed * 13) / 20f;
                    roots += Mathf.Exp(-a * a) * 1.3f * Mathf.Exp(-y / 55f);
                }
                float taper = Mathf.Lerp(1f, 0.7f, t);
                float ridges = 1 + 0.035f * (Noise(phi * 9 + seed, y * 0.004f) - 0.5f) * 2 + 0.02f * Mathf.Sin(phi * 23 + Noise(y * 0.01f, phi) * 6);
                return new Vector2(radius * taper * (flare + roots) * ridges, y);
            }
            Color Col(float phi, float t, Vector3 n)
            {
                float moss = SStep(0.3f, 0.9f, n.z * 0.5f + 0.5f) * (1 - SStep(0.02f, 0.2f, t)) * 0.9f + (Noise(phi * 5, t * 30) > 0.62f ? 0.35f : 0);
                var bark = Color.Lerp(new Color(0.42f, 0.36f, 0.3f), new Color(0.3f, 0.26f, 0.22f), Noise(phi * 12, t * 60));
                var c = Color.Lerp(bark, new Color(0.34f, 0.42f, 0.2f), Mathf.Clamp01(moss));
                c.a = 0.08f;
                return c;
            }
            return Lathe("trunk", 48, 70, Shape, Col, new Vector2(4, height / 300f), capBottom: false, capTop: true);
        }

        /// <summary>Tapered, bent tube along a polyline (branches and twigs).</summary>
        public static Mesh Tube(string name, IList<Vector3> path, float r0, float r1, int sides, Color c0, Color c1, int seed = 0)
        {
            var b = new MeshBuilder();
            int n = path.Count;
            float total = 0;
            for (int i = 1; i < n; i++) total += Vector3.Distance(path[i - 1], path[i]);
            float along = 0;
            Vector3 prevUp = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) along += Vector3.Distance(path[i - 1], path[i]);
                float t = total > 0 ? along / total : 0;
                Vector3 fwd = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(fwd, prevUp);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(fwd, Vector3.right);
                side.Normalize();
                Vector3 up = Vector3.Cross(side, fwd).normalized;
                prevUp = up;
                float r = Mathf.Lerp(r0, r1, t);
                for (int s = 0; s <= sides; s++)
                {
                    float a = Mathf.PI * 2 * s / sides;
                    Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    float bump = 1 + 0.08f * (Noise(a * 3 + seed, along * 0.05f) - 0.5f);
                    var col = Color.Lerp(c0, c1, Noise(a * 2 + seed, along * 0.02f));
                    col.a = 0.08f;
                    b.Add(path[i] + dir * r * bump, dir, col, new Vector2((float)s / sides, along / 40f));
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * (sides + 1) + s, c = a + sides + 1;
                    b.Quad(a, c, c + 1, a + 1);
                }
            // end caps
            foreach (var end in new[] { 0, n - 1 })
            {
                Vector3 fwd = (end == 0 ? path[0] - path[1] : path[n - 1] - path[n - 2]).normalized;
                var center = b.Add(path[end], fwd, c0, new Vector2(0.5f, 0.5f));
                int ring = end * (sides + 1);
                for (int s = 0; s < sides; s++)
                {
                    if (end == 0) b.Tri(center, ring + s, ring + s + 1);
                    else b.Tri(center, ring + s + 1, ring + s);
                }
            }
            return b.Build(name);
        }

        /// <summary>Pebble: noisy ellipsoid.</summary>
        public static Mesh Stone(Vector3 radii, int seed, Color a, Color b)
        {
            var rng = new Random(seed);
            float ox = (float)rng.NextDouble() * 100, oy = (float)rng.NextDouble() * 100;
            var mb = new MeshBuilder();
            const int slices = 40, stacks = 24;
            for (int i = 0; i <= stacks; i++)
            {
                float theta = Mathf.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = 2 * Mathf.PI * j / slices;
                    var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    float disp = 1 + 0.18f * (Fbm(unit.x * 1.3f + ox, unit.z * 1.3f + unit.y * 0.7f + oy, 3) - 0.5f) * 2;
                    disp *= unit.y < 0 ? 1 - 0.3f * -unit.y : 1; // flatter underside
                    var p = Vector3.Scale(unit * disp, radii);
                    var col = Color.Lerp(a, b, Fbm(unit.x * 4 + ox, unit.z * 4 + unit.y * 3 + oy, 3));
                    if (Noise(unit.x * 30 + ox, unit.z * 30 + unit.y * 20) > 0.8f) col = Color.Lerp(col, new Color(0.85f, 0.83f, 0.78f), 0.4f); // mineral flecks
                    if (unit.y > 0.5f && Noise(unit.x * 6 + ox, unit.z * 6) > 0.62f) col = Color.Lerp(col, new Color(0.55f, 0.6f, 0.4f), 0.45f); // lichen
                    col.a = 0.12f;
                    mb.Add(p, unit, col, new Vector2((float)j / slices * 2, (float)i / stacks));
                }
            }
            for (int i = 0; i < stacks; i++)
                for (int j = 0; j < slices; j++)
                {
                    int x = i * (slices + 1) + j, y = x + slices + 1;
                    mb.Quad(x, x + 1, y + 1, y);
                }
            return mb.Build("stone" + seed, recalcNormals: true);
        }

        /// <summary>
        /// Fallen leaf with thickness (so both sides can be walked on): midrib along +Z, curled edges.
        /// UV: x across the blade (0..1), y along (0 petiole .. 1 tip).
        /// </summary>
        public static Mesh Leaf(float length, float width, float curl, float thickness, Color color, int seed, bool closed = true)
        {
            var rng = new Random(seed);
            float wav = (float)rng.NextDouble() * 10;
            var b = new MeshBuilder();
            const int along = 18, across = 8;
            Vector3 P(float u, float v, out float halfWidth)
            {
                // u in [-1,1] across, v in [0,1] along
                halfWidth = width * 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01(v), 0.85f))), 0.8f);
                float x = u * halfWidth;
                float z = v * length;
                float y = curl * (u * u) * width * 0.25f + 0.04f * width * Mathf.Sin(v * 9 + wav) * Mathf.Abs(u) + curl * 0.1f * length * (v - 0.5f) * (v - 0.5f);
                return new Vector3(x, y, z);
            }
            for (int side = 0; side < (closed ? 2 : 1); side++)
            {
                float offset = side == 0 ? thickness * 0.5f : -thickness * 0.5f;
                int start = b.Count;
                for (int j = 0; j <= along; j++)
                {
                    float v = (float)j / along;
                    for (int i = 0; i <= across; i++)
                    {
                        float u = (float)i / across * 2 - 1;
                        var p = P(u, v, out _);
                        float eps = 0.01f;
                        var du = P(u + eps, v, out _) - P(u - eps, v, out _);
                        var dv = P(u, Mathf.Min(1, v + eps), out _) - P(u, Mathf.Max(0, v - eps), out _);
                        var n = Vector3.Cross(dv, du).normalized;
                        if (n.y < 0) n = -n;
                        if (side == 1) n = -n;
                        var c = color * (side == 0 ? 1f : 0.85f);
                        c.a = 0.15f;
                        b.Add(p + n * Mathf.Abs(offset), n, c, new Vector2((u + 1) * 0.5f, v));
                    }
                }
                for (int j = 0; j < along; j++)
                    for (int i = 0; i < across; i++)
                    {
                        int a = start + j * (across + 1) + i, c = a + across + 1;
                        if (side == 0) b.Quad(a, c, c + 1, a + 1);
                        else b.Quad(a, a + 1, c + 1, c);
                    }
            }
            if (closed)
            {
                // rim joining top and bottom sheets
                int per = (along + 1) * (across + 1);
                for (int j = 0; j < along; j++)
                {
                    foreach (int i in new[] { 0, across })
                    {
                        int a = j * (across + 1) + i, c = a + across + 1;
                        if (i == 0) b.Quad(a, a + per, c + per, c);
                        else b.Quad(a, c, c + per, a + per);
                    }
                }
            }
            return b.Build("leaf" + seed, recalcNormals: false);
        }

        /// <summary>Appends a flat, single-sided leaf (litter) to a builder, lying on the terrain.</summary>
        public static void AddLitterLeaf(MeshBuilder b, Vector3 position, float yaw, float length, float width, Color color, Func<float, float, float> height, int seed, float lift = 0.35f)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            const int along = 8, across = 4;
            int start = b.Count;
            float wav = seed % 10;
            for (int j = 0; j <= along; j++)
            {
                float v = (float)j / along;
                float half = width * 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01(v), 0.85f))), 0.8f);
                for (int i = 0; i <= across; i++)
                {
                    float u = (float)i / across * 2 - 1;
                    var local = new Vector3(u * half, 0, (v - 0.35f) * length);
                    var p = position + rot * local;
                    float curlUp = 0.05f * width * u * u + 0.015f * width * Mathf.Sin(v * 7 + wav);
                    p.y = height(p.x, p.z) + lift + Mathf.Max(0f, curlUp);
                    var c = color;
                    c.a = 0.12f;
                    b.Add(p, Vector3.up, c, new Vector2((u + 1) * 0.5f, v));
                }
            }
            for (int j = 0; j < along; j++)
                for (int i = 0; i < across; i++)
                {
                    int a = start + j * (across + 1) + i, c = a + across + 1;
                    b.Quad(a, c, c + 1, a + 1);
                }
        }

        /// <summary>Appends one curved grass blade (uv.y 0 root .. 1 tip, uv.x = blade height, uv2 = root).</summary>
        public static void AddBlade(MeshBuilder b, Vector3 root, float height, float width, float yaw, float lean, Color color)
        {
            const int segs = 5;
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector3 side = rot * Vector3.right;
            Vector3 fwd = rot * Vector3.forward;
            int start = b.Count;
            for (int k = 0; k <= segs; k++)
            {
                float t = (float)k / segs;
                float w = width * (1 - t * t) * 0.5f + 0.05f;
                Vector3 center = root + Vector3.up * (height * t) + fwd * (lean * height * t * t);
                Vector3 n = (Vector3.Cross(side, Vector3.up * height + fwd * (2 * lean * height * t))).normalized;
                if (k < segs)
                {
                    b.Add(center - side * w, n, color, new Vector2(height, t), root);
                    b.Add(center + side * w, n, color, new Vector2(height, t), root);
                }
                else
                {
                    b.Add(center, n, color, new Vector2(height, t), root);
                }
            }
            for (int k = 0; k < segs - 1; k++)
            {
                int a = start + k * 2;
                b.Quad(a, a + 1, a + 3, a + 2);
            }
            int last = start + (segs - 1) * 2;
            b.Tri(last, last + 1, start + segs * 2);
        }

        /// <summary>Blobby foliage cluster (bushes, canopy, distant trees).</summary>
        public static Mesh Blob(Vector3 radii, int seed, Color a, Color b, float roughness = 0.35f)
        {
            var rng = new Random(seed);
            float ox = (float)rng.NextDouble() * 100, oy = (float)rng.NextDouble() * 100;
            var mb = new MeshBuilder();
            const int slices = 36, stacks = 20;
            for (int i = 0; i <= stacks; i++)
            {
                float theta = Mathf.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = 2 * Mathf.PI * j / slices;
                    var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    float n = Fbm(unit.x * 2.2f + ox, unit.z * 2.2f + unit.y * 1.7f + oy, 4);
                    float disp = 1 + roughness * (n - 0.5f) * 2;
                    var col = Color.Lerp(a, b, Mathf.Clamp01(n * 1.3f - 0.1f + unit.y * 0.25f));
                    col.a = 0.1f;
                    mb.Add(Vector3.Scale(unit * disp, radii), unit, col, new Vector2((float)j / slices * 3, (float)i / stacks * 2));
                }
            }
            for (int i = 0; i < stacks; i++)
                for (int j = 0; j < slices; j++)
                {
                    int x = i * (slices + 1) + j, y = x + slices + 1;
                    mb.Quad(x, x + 1, y + 1, y);
                }
            return mb.Build("blob" + seed, recalcNormals: true);
        }
    }
}
