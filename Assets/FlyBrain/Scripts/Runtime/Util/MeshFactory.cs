using System.Collections.Generic;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>Procedural meshes for the fly body and the environment (all sizes in millimeters).</summary>
    public static class MeshFactory
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>Ellipsoid centered at the origin. UV.y runs from the front (+Z) to the back (-Z).</summary>
        public static Mesh Ellipsoid(Vector3 radii, int slices = 24, int stacks = 16)
        {
            string key = $"ell{radii}{slices}{stacks}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= stacks; i++)
            {
                float theta = Mathf.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = 2 * Mathf.PI * j / slices;
                    var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta));
                    verts.Add(Vector3.Scale(unit, radii));
                    normals.Add(new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized);
                    uvs.Add(new Vector2((float)j / slices, (float)i / stacks));
                }
            }
            for (int i = 0; i < stacks; i++)
                for (int j = 0; j < slices; j++)
                {
                    int a = i * (slices + 1) + j, b = a + slices + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
                }
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        /// <summary>Tapered segment from z=0 to z=length with rounded ends (for leg segments, antennae, proboscis).</summary>
        public static Mesh Segment(float length, float r0, float r1, int sides = 10)
        {
            string key = $"seg{length:F3}{r0:F3}{r1:F3}{sides}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // profile: hemisphere cap at start, cone body, hemisphere cap at end
            var profile = new List<Vector2>(); // (z, radius)
            const int capSteps = 4;
            for (int k = 0; k <= capSteps; k++)
            {
                float a = Mathf.PI * 0.5f * (1 - (float)k / capSteps);
                profile.Add(new Vector2(-Mathf.Sin(a) * r0 * 0.8f, Mathf.Cos(a) * r0));
            }
            for (int k = 1; k < 6; k++)
            {
                float t = k / 6f;
                profile.Add(new Vector2(t * length, Mathf.Lerp(r0, r1, t) * (1 + 0.08f * Mathf.Sin(t * Mathf.PI))));
            }
            for (int k = 0; k <= capSteps; k++)
            {
                float a = Mathf.PI * 0.5f * k / capSteps;
                profile.Add(new Vector2(length + Mathf.Sin(a) * r1 * 0.8f, Mathf.Cos(a) * r1));
            }

            for (int p = 0; p < profile.Count; p++)
            {
                float z = profile[p].x, r = profile[p].y;
                float dz = profile[Mathf.Min(p + 1, profile.Count - 1)].x - profile[Mathf.Max(p - 1, 0)].x;
                float dr = profile[Mathf.Min(p + 1, profile.Count - 1)].y - profile[Mathf.Max(p - 1, 0)].y;
                var n2 = new Vector2(dz, -dr).normalized; // (radial, axial)
                for (int s = 0; s <= sides; s++)
                {
                    float phi = 2 * Mathf.PI * s / sides;
                    var dir = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0);
                    verts.Add(dir * r + new Vector3(0, 0, z));
                    normals.Add((dir * n2.x + new Vector3(0, 0, n2.y)).normalized);
                    uvs.Add(new Vector2((float)s / sides, (float)p / (profile.Count - 1)));
                }
            }
            for (int p = 0; p < profile.Count - 1; p++)
                for (int s = 0; s < sides; s++)
                {
                    int a = p * (sides + 1) + s, b = a + sides + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        /// <summary>Flat quad in the XZ plane from z=0 to z=length, x in [0, width] (sign of width picks the side).</summary>
        public static Mesh WingQuad(float length, float width, int subdivisions = 8)
        {
            string key = $"wing{length:F3}{width:F3}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= subdivisions; i++)
                for (int j = 0; j <= subdivisions; j++)
                {
                    float u = (float)i / subdivisions, v = (float)j / subdivisions;
                    // slight camber so that the membrane catches light
                    verts.Add(new Vector3(u * width, 0.02f * Mathf.Sin(u * Mathf.PI) * Mathf.Abs(width), v * length));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(v, u));
                }
            for (int i = 0; i < subdivisions; i++)
                for (int j = 0; j < subdivisions; j++)
                {
                    int a = i * (subdivisions + 1) + j, b = a + subdivisions + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                }
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        /// <summary>Spherical cap (liquid drop) resting on y=0 with base radius and height.</summary>
        public static Mesh Drop(float radius, float height, int slices = 32, int rings = 10)
        {
            string key = $"drop{radius:F3}{height:F3}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            // sphere of radius R whose cap of height h has base radius r: R = (r^2 + h^2) / 2h
            float R = (radius * radius + height * height) / (2 * height);
            float thetaMax = Mathf.Asin(Mathf.Clamp01(radius / R));
            if (height > R) thetaMax = Mathf.PI - thetaMax;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float theta = thetaMax * i / rings;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = 2 * Mathf.PI * j / slices;
                    var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    verts.Add(n * R + new Vector3(0, height - R, 0));
                    normals.Add(n);
                    uvs.Add(new Vector2((float)j / slices, (float)i / rings));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < slices; j++)
                {
                    int a = i * (slices + 1) + j, b = a + slices + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                }
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        /// <summary>Horizontal disc (y=0) with planar UVs covering [0,1] across the diameter.</summary>
        public static Mesh Disc(float radius, int slices = 96)
        {
            string key = $"disc{radius:F3}{slices}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3> { Vector3.zero };
            var normals = new List<Vector3> { Vector3.up };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int j = 0; j <= slices; j++)
            {
                float phi = 2 * Mathf.PI * j / slices;
                var p = new Vector3(Mathf.Cos(phi), 0, Mathf.Sin(phi));
                verts.Add(p * radius);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + 0.5f * p.x, 0.5f + 0.5f * p.z));
                if (j > 0) { tris.Add(0); tris.Add(j + 1); tris.Add(j); }
            }
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        /// <summary>Open cylinder wall (a ring) from y=0 to y=height.</summary>
        public static Mesh Ring(float radius, float height, float thickness, int slices = 96)
        {
            string key = $"ring{radius:F3}{height:F3}{thickness:F3}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            void Wall(float r, float sign)
            {
                int start = verts.Count;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = 2 * Mathf.PI * j / slices;
                    var d = new Vector3(Mathf.Cos(phi), 0, Mathf.Sin(phi));
                    verts.Add(d * r); normals.Add(d * sign); uvs.Add(new Vector2((float)j / slices, 0));
                    verts.Add(d * r + Vector3.up * height); normals.Add(d * sign); uvs.Add(new Vector2((float)j / slices, 1));
                }
                for (int j = 0; j < slices; j++)
                {
                    int a = start + j * 2;
                    if (sign > 0) { tris.Add(a); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 3); }
                    else { tris.Add(a); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3); tris.Add(a + 1); }
                }
            }
            Wall(radius + thickness, 1);
            Wall(radius, -1);
            // top rim
            int rim = verts.Count;
            for (int j = 0; j <= slices; j++)
            {
                float phi = 2 * Mathf.PI * j / slices;
                var d = new Vector3(Mathf.Cos(phi), 0, Mathf.Sin(phi));
                verts.Add(d * radius + Vector3.up * height); normals.Add(Vector3.up); uvs.Add(new Vector2((float)j / slices, 0));
                verts.Add(d * (radius + thickness) + Vector3.up * height); normals.Add(Vector3.up); uvs.Add(new Vector2((float)j / slices, 1));
            }
            for (int j = 0; j < slices; j++)
            {
                int a = rim + j * 2;
                tris.Add(a); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3); tris.Add(a + 1);
            }
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        /// <summary>Flat sector in the XZ plane around the lateral axis (sign of <paramref name="side"/>), for the wing stroke blur.
        /// UV.x runs along the arc (0..1), UV.y from the hinge (0) to the rim (1).</summary>
        public static Mesh Fan(float radius, float halfAngleDeg, float side, int segments = 24)
        {
            string key = $"fan{radius:F3}{halfAngleDeg:F1}{side}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int k = 0; k <= segments; k++)
            {
                float u = (float)k / segments;
                float a = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, u) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sign(side) * Mathf.Cos(a), 0.12f, Mathf.Sin(a));
                verts.Add(dir * 0.08f);
                verts.Add(dir * radius);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(u, 0));
                uvs.Add(new Vector2(u, 1));
                if (k < segments)
                {
                    int i = k * 2;
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + 3);
                    tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
                }
            }
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        /// <summary>Unit quad in the XY plane centered at the origin (for billboards / streaks).</summary>
        public static Mesh Quad()
        {
            const string key = "quad";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var verts = new List<Vector3> { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            var normals = new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var uvs = new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            var tris = new List<int> { 0, 2, 1, 0, 3, 2 };
            return Cache[key] = Build(key, verts, normals, uvs, tris);
        }

        static Mesh Build(string name, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
