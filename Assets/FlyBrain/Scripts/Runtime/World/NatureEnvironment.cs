using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyBrain
{
    /// <summary>
    /// The natural habitat of Drosophila melanogaster: the floor of an orchard under an apple tree in late summer.
    /// Fallen apples, a pear and plums ferment in the grass (sugar, yeast, odor plumes), dew forms at dawn and
    /// dries by mid-morning, a puddle holds water, leaves, twigs and stones make a landscape that can be walked on
    /// everywhere. Sun, moon, wind and dappled light change through a compressed day. 1 unit = 1 mm.
    /// </summary>
    public sealed class NatureEnvironment : FlyEnvironment
    {
        public override string DisplayName => "Сад: опавшие фрукты под яблоней";
        public override bool IsNature => true;

        public static readonly Vector3 TrunkPosition = new Vector3(560, 0, -520);
        public static readonly Vector3 PuddlePosition = new Vector3(330, 0, 70);
        const float InnerSize = 3200f;

        public readonly List<Transform> Fruits = new List<Transform>();
        Transform _static;
        Material _terrainMat, _fruitMat, _leafMat, _barkMat, _stoneMat, _grassMat, _foliageMat;
        readonly List<FoodSource> _dew = new List<FoodSource>();
        System.Random _rng;

        // ------------------------------------------------------------------ terrain height

        public static float Height(float x, float z)
        {
            float h = 16f * (NatureMeshes.Fbm(x * 0.0011f, z * 0.0011f, 4) - 0.5f) * 2f;
            h += 2.2f * (Perlin.Noise(x * 0.013f + 50, z * 0.013f + 50) - 0.5f);
            float dt = new Vector2(x - TrunkPosition.x, z - TrunkPosition.z).magnitude;
            h += 26f * Mathf.Exp(-dt * dt / (340f * 340f));
            float dp = new Vector2(x - PuddlePosition.x, z - PuddlePosition.z).magnitude;
            h -= 7f * Mathf.Exp(-dp * dp / (70f * 70f));
            float r = new Vector2(x, z).magnitude;
            float far = Mathf.Clamp01((r - 2200f) / 4000f);
            h += 380f * (NatureMeshes.Fbm(x * 0.0002f + 3, z * 0.0002f + 9, 3) - 0.35f) * far * far;
            return h;
        }

        public static float PuddleLevel => Height(PuddlePosition.x, PuddlePosition.z) + 4.2f;

        const int InnerCells = 160;

        /// <summary>Height of the rendered ground mesh (piecewise linear triangles), so flat props never intersect it.</summary>
        public static float MeshHeight(float x, float z)
        {
            float step = InnerSize / InnerCells;
            float fx = (x + InnerSize * 0.5f) / step, fz = (z + InnerSize * 0.5f) / step;
            if (fx < 0 || fz < 0 || fx >= InnerCells || fz >= InnerCells) return Height(x, z);
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fz);
            float tx = fx - i, tz = fz - j;
            float x0 = -InnerSize * 0.5f + i * step, z0 = -InnerSize * 0.5f + j * step;
            float h00 = Height(x0, z0), h11 = Height(x0 + step, z0 + step);
            // quads are split along the (i,j)-(i+1,j+1) diagonal
            if (tz >= tx)
            {
                float h01 = Height(x0, z0 + step);
                return h00 + (h01 - h00) * tz + (h11 - h01) * tx;
            }
            float h10 = Height(x0 + step, z0);
            return h00 + (h10 - h00) * tx + (h11 - h10) * tz;
        }

        // ------------------------------------------------------------------ build

        protected override void Build()
        {
            _rng = new System.Random(20260917);
            Center = Vector3.zero;
            Radius = 1450f;
            _static = new GameObject("Orchard floor").transform;
            _static.SetParent(transform, false);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder();
            void Step(string name, System.Action a)
            {
                long t0 = sw.ElapsedMilliseconds;
                a();
                times.Append($" {name}={sw.ElapsedMilliseconds - t0}");
            }
            Step("materials", BuildMaterials);
            Step("sky", BuildSkyAndLight);
            Step("terrain", BuildTerrain);
            Step("tree", BuildTree);
            Step("fruit", BuildFruit);
            Step("puddle", BuildPuddle);
            Step("stones", BuildStonesAndTwigs);
            Step("leaves", BuildLeaves);
            Step("grass", BuildGrass);
            Step("distance", BuildDistance);
            Physics.SyncTransforms();
            Debug.Log($"[FlyBrain] orchard built in {sw.ElapsedMilliseconds} ms:{times}");
        }

        float R01() => (float)_rng.NextDouble();
        float Range(float a, float b) => a + (b - a) * R01();

        void BuildMaterials()
        {
            var nature = FlyMaterials.GetShader("FlyBrainNature");
            var grain = NatureTextures.Grain();
            Material M(string name, Texture2D tex, float gloss, float detail, float detailScale, Vector2 tiling, float twoSided = 2, float translucency = 0)
            {
                var m = new Material(nature) { name = name };
                if (tex != null) m.SetTexture("_MainTex", tex);
                m.mainTextureScale = tiling;
                m.SetTexture("_DetailTex", grain);
                m.SetFloat("_DetailScale", detailScale);
                m.SetFloat("_DetailStrength", detail);
                m.SetFloat("_Glossiness", gloss);
                m.SetFloat("_TwoSided", twoSided);
                m.SetFloat("_Translucency", translucency);
                return m;
            }
            _terrainMat = M("soil", NatureTextures.Soil(), 0.25f, 0.55f, 1f / 9f, new Vector2(1f / 170f, 1f / 170f));
            _fruitMat = M("fruit", NatureTextures.Skin(), 0.5f, 0.35f, 1f / 3f, Vector2.one);
            _fruitMat.SetColor("_SpecColor2", new Color(0.12f, 0.11f, 0.1f));
            _leafMat = M("leaf", NatureTextures.LeafVeins(), 0.35f, 0.15f, 1f / 4f, Vector2.one, 0, 0.6f);
            _barkMat = M("bark", NatureTextures.Bark(), 0.2f, 0.4f, 1f / 6f, Vector2.one);
            _stoneMat = M("stone", null, 0.35f, 0.6f, 1f / 5f, Vector2.one);
            _foliageMat = M("foliage", NatureTextures.LeafVeins(), 0.2f, 0.5f, 1f / 40f, new Vector2(6, 6), 2, 0.3f);
            _grassMat = new Material(FlyMaterials.GetShader("FlyBrainGrass")) { name = "grass" };
        }

        void BuildSkyAndLight()
        {
            var sky = new Material(FlyMaterials.GetShader("FlyBrainSky")) { name = "sky" };
            RenderSettings.skybox = sky;

            Sun = new GameObject("Sun and moon").AddComponent<Light>();
            Sun.transform.SetParent(transform, false);
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowBias = 0.02f;
            Sun.shadowNormalBias = 0.25f;
            Sun.cookie = NatureTextures.Canopy(1500f / 6000f);
            Sun.cookieSize2D = new Vector2(6000f, 6000f);
            RenderSettings.sun = Sun;

            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowDistance = 1400f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowCascade4Split = new Vector3(0.012f, 0.05f, 0.2f);

            Clock = gameObject.AddComponent<DayCycle>();
            Clock.Sun = Sun;
            Clock.Sky = sky;
            Clock.CookieAnchor = TrunkPosition + new Vector3(0, 2300, 0);
            Clock.Apply();

            Wind = gameObject.AddComponent<WindField>();
            Wind.Clock = Clock;
        }

        void BuildTerrain()
        {
            Color TerrainColor(float x, float z, Vector3 n)
            {
                float dt = new Vector2(x - TrunkPosition.x, z - TrunkPosition.z).magnitude;
                float dp = new Vector2(x - PuddlePosition.x, z - PuddlePosition.z).magnitude;
                float r = new Vector2(x, z).magnitude;
                float moss = Mathf.Clamp01((NatureMeshes.Fbm(x * 0.004f + 7, z * 0.004f, 3) - 0.52f) * 5f);
                float grassy = Mathf.Clamp01((r - 450f) / 700f) * (0.6f + 0.4f * NatureMeshes.Noise(x * 0.002f, z * 0.002f));
                var c = Color.Lerp(Color.white, new Color(0.85f, 0.95f, 0.7f), moss * 0.6f);
                c = Color.Lerp(c, new Color(0.72f, 0.82f, 0.5f), grassy * 0.75f);
                float damp = Mathf.Exp(-dp / 90f) * 0.8f + Mathf.Exp(-dt / 500f) * 0.25f;
                c *= 1f - 0.35f * damp;
                c.a = 0.04f + 0.5f * Mathf.Exp(-dp / 60f);
                return c;
            }
            var inner = NatureMeshes.Terrain("terrain inner", Vector2.zero, InnerSize, InnerCells, Height, TerrainColor);
            Visual(_static, "Ground", inner, _terrainMat, Vector3.zero, Quaternion.identity, true, true);
            var outer = NatureMeshes.Terrain("terrain outer", Vector2.zero, 26000f, 130, Height, TerrainColor, InnerSize * 0.5f);
            Visual(_static, "Ground far", outer, _terrainMat, Vector3.zero, Quaternion.identity, false, true);
        }

        void BuildTree()
        {
            var t = TrunkPosition;
            t.y = Height(t.x, t.z) - 12f;
            var trunk = Visual(_static, "Apple tree trunk", NatureMeshes.Trunk(95f, 1700f, 3), _barkMat, t, Quaternion.Euler(0, 20, 0), true, true);

            // branches leaving the frame and a low branch with leaves and hanging apples
            var barkA = new Color(0.45f, 0.39f, 0.33f);
            var barkB = new Color(0.32f, 0.28f, 0.24f);
            Vector3 top = t + Vector3.up * 1650f;
            foreach (var dir in new[] { new Vector3(-0.6f, 1f, 0.3f), new Vector3(0.7f, 1.1f, -0.2f), new Vector3(0.1f, 1f, 0.8f) })
            {
                var path = new List<Vector3>();
                var p = top;
                var d = dir.normalized;
                for (int k = 0; k < 8; k++)
                {
                    path.Add(p);
                    p += d * 160f;
                    d = (d + new Vector3(Range(-0.15f, 0.15f), 0.05f, Range(-0.15f, 0.15f))).normalized;
                }
                Visual(_static, "Branch", NatureMeshes.Tube("branch", path, 62f, 22f, 16, barkA, barkB), _barkMat, Vector3.zero, Quaternion.identity, false);
            }
            var low = new List<Vector3>();
            {
                var p = t + new Vector3(-40, 980, 60);
                var d = new Vector3(-0.8f, -0.12f, 0.55f).normalized;
                for (int k = 0; k < 10; k++)
                {
                    low.Add(p);
                    p += d * 70f;
                    d = (d + new Vector3(Range(-0.08f, 0.08f), -0.05f, Range(-0.08f, 0.08f))).normalized;
                }
            }
            Visual(_static, "Low branch", NatureMeshes.Tube("low branch", low, 34f, 9f, 14, barkA, barkB), _barkMat, Vector3.zero, Quaternion.identity, true, true);
            for (int k = 3; k < low.Count; k++)
            {
                for (int m = 0; m < 3; m++)
                {
                    var leafColor = Color.Lerp(new Color(0.3f, 0.5f, 0.16f), new Color(0.45f, 0.58f, 0.2f), R01());
                    var mesh = NatureMeshes.Leaf(Range(55, 75), Range(28, 36), Range(0.2f, 0.6f), 0.35f, leafColor, _rng.Next());
                    var rot = Quaternion.LookRotation(new Vector3(Range(-1, 1), Range(-0.7f, 0.2f), Range(-1, 1)).normalized, Vector3.up) * Quaternion.Euler(0, 0, Range(-40, 40));
                    Visual(_static, "Branch leaf", mesh, _leafMat, low[k] + Random.insideUnitSphere * 6f, rot, true, true);
                }
            }
            foreach (int k in new[] { 5, 8 })
            {
                var shape = new NatureMeshes.FruitShape { Name = "hanging apple", Kind = 0, Radius = 34, Height = 62, Seed = k, SkinA = new Color(0.55f, 0.72f, 0.18f), SkinB = new Color(0.78f, 0.12f, 0.08f), Gloss = 0.7f };
                var pos = low[k] + new Vector3(0, -80, 0);
                var apple = Visual(_static, "Hanging apple", shape.Build(), _fruitMat, pos - new Vector3(0, 62, 0), Quaternion.identity, true, true);
                Visual(_static, "Stalk", NatureMeshes.Tube("stalk", new[] { low[k], low[k] + new Vector3(3, -40, 2), pos }, 2.2f, 1.6f, 6, new Color(0.4f, 0.32f, 0.2f), new Color(0.3f, 0.25f, 0.15f)), _barkMat, Vector3.zero, Quaternion.identity, false);
                _ = apple;
            }

            // canopy (shadowing comes from the light cookie)
            for (int k = 0; k < 14; k++)
            {
                float a = k * 2.4f;
                var pos = TrunkPosition + new Vector3(Mathf.Cos(a) * Range(150, 1100), Range(1900, 2900), Mathf.Sin(a) * Range(150, 1100));
                var radii = new Vector3(Range(450, 800), Range(300, 520), Range(450, 800));
                var blob = NatureMeshes.Blob(radii, 100 + k, new Color(0.14f, 0.26f, 0.08f), new Color(0.32f, 0.45f, 0.14f), 0.45f);
                Visual(_static, "Canopy", blob, _foliageMat, pos, Quaternion.Euler(0, Range(0, 360), 0), false);
            }
        }

        // ------------------------------------------------------------------ fruit

        struct FruitSpec
        {
            public string Name;
            public int Kind;
            public Vector2 Pos;
            public float Radius, Height;
            public Color A, B;
            public float Rot, Wound, Mold; // number / size of spots
            public float Sink;
            public bool Fresh;
        }

        void BuildFruit()
        {
            var red = new Color(0.72f, 0.12f, 0.07f);
            var yellow = new Color(0.78f, 0.62f, 0.18f);
            var green = new Color(0.52f, 0.66f, 0.2f);
            var plum = new Color(0.25f, 0.07f, 0.2f);
            var plumB = new Color(0.4f, 0.12f, 0.3f);
            var specs = new[]
            {
                new FruitSpec { Name = "Гниющее яблоко", Kind = 0, Pos = new Vector2(-6, 62), Radius = 38, Height = 70, A = yellow, B = red, Rot = 2, Wound = 0, Sink = 7 },
                new FruitSpec { Name = "Расклёванное яблоко", Kind = 0, Pos = new Vector2(-230, -150), Radius = 36, Height = 66, A = green, B = red, Rot = 1, Wound = 1, Sink = 4 },
                new FruitSpec { Name = "Свежее яблоко", Kind = 0, Pos = new Vector2(250, 205), Radius = 37, Height = 68, A = green, B = red, Fresh = true, Sink = 2 },
                new FruitSpec { Name = "Лопнувшая слива", Kind = 2, Pos = new Vector2(-120, 250), Radius = 19, Height = 42, A = plum, B = plumB, Rot = 0, Wound = 1, Sink = 2 },
                new FruitSpec { Name = "Слива", Kind = 2, Pos = new Vector2(360, -210), Radius = 18, Height = 40, A = plum, B = plumB, Rot = 1, Sink = 3 },
                new FruitSpec { Name = "Гнилая груша", Kind = 1, Pos = new Vector2(-400, 50), Radius = 34, Height = 88, A = new Color(0.7f, 0.64f, 0.25f), B = new Color(0.55f, 0.5f, 0.18f), Rot = 3, Sink = 6 },
                new FruitSpec { Name = "Вишня", Kind = 3, Pos = new Vector2(150, -310), Radius = 10, Height = 19, A = new Color(0.45f, 0.02f, 0.05f), B = new Color(0.6f, 0.05f, 0.08f), Wound = 1, Sink = 1 },
                new FruitSpec { Name = "Вишня", Kind = 3, Pos = new Vector2(178, -292), Radius = 9.5f, Height = 18, A = new Color(0.45f, 0.02f, 0.05f), B = new Color(0.6f, 0.05f, 0.08f), Fresh = true, Sink = 1 },
                new FruitSpec { Name = "Заплесневелое яблоко", Kind = 0, Pos = new Vector2(-70, -430), Radius = 35, Height = 64, A = yellow, B = new Color(0.5f, 0.2f, 0.1f), Rot = 1, Mold = 1, Sink = 8 },
                new FruitSpec { Name = "Яблоко у ствола", Kind = 0, Pos = new Vector2(430, -330), Radius = 39, Height = 70, A = yellow, B = red, Rot = 1, Sink = 5 },
            };
            foreach (var s in specs) MakeFruit(s);
        }

        void MakeFruit(FruitSpec s)
        {
            int seed = _rng.Next(1000);
            var shape = new NatureMeshes.FruitShape { Name = s.Name, Kind = s.Kind, Radius = s.Radius, Height = s.Height, Seed = seed, SkinA = s.A, SkinB = s.B, Gloss = s.Kind == 2 ? 0.2f : 0.42f };
            // fallen fruit rests on its side
            var tilt = Quaternion.AngleAxis(s.Kind == 1 ? 80f : Range(55f, 110f), new Vector3(Range(-1, 1), 0, Range(-1, 1)).normalized) * Quaternion.Euler(0, Range(0, 360), 0);
            var up = Vector3.up;
            var spotWorldDirs = new List<(Vector3 dir, int kind, float angle, float depth)>();
            for (int k = 0; k < s.Rot; k++)
                spotWorldDirs.Add(((up + new Vector3(Range(-0.9f, 0.9f), Range(-0.2f, 0.4f), Range(-0.9f, 0.9f))).normalized, 0, Range(0.45f, 0.75f), Range(2f, 5f)));
            for (int k = 0; k < s.Wound; k++)
                spotWorldDirs.Add(((up * 1.3f + new Vector3(Range(-0.5f, 0.5f), 0, Range(-0.5f, 0.5f))).normalized, 1, s.Kind >= 2 ? 0.55f : 0.42f, s.Radius * 0.3f));
            for (int k = 0; k < s.Mold; k++)
                spotWorldDirs.Add(((up + new Vector3(Range(-0.4f, 0.4f), 0.3f, Range(-0.4f, 0.4f))).normalized, 2, 0.5f, 3f));
            var inv = Quaternion.Inverse(tilt);
            foreach (var (dir, kind, angle, depth) in spotWorldDirs)
                shape.Spots.Add(new NatureMeshes.FruitSpot { Dir = inv * dir, Kind = kind, Angle = angle, Depth = depth });
            var mesh = shape.Build(s.Kind == 3 ? 32 : 72, s.Kind == 3 ? 20 : 44);

            // rest the lowest point on the ground, sunk a little into the soil
            float lowest = float.MaxValue;
            var verts = mesh.vertices;
            foreach (var v in verts) lowest = Mathf.Min(lowest, (tilt * v).y);
            var basePos = new Vector3(s.Pos.x, 0, s.Pos.y);
            basePos.y = Height(basePos.x, basePos.z) - lowest - s.Sink;
            var fruit = Visual(_static, s.Name, mesh, _fruitMat, basePos, tilt, true, true);
            Fruits.Add(fruit);

            if (s.Kind == 0 || s.Kind == 1)
            {
                // stalk
                shape.Surface(Vector3.up, out var stemBase, out var stemN);
                var stalk = NatureMeshes.Tube("stalk", new[] { stemBase - stemN * 4, stemBase + stemN * 8, stemBase + stemN * 16 + new Vector3(3, 0, 2) }, 1.6f, 1.1f, 6, new Color(0.35f, 0.25f, 0.12f), new Color(0.25f, 0.18f, 0.1f));
                var st = Visual(fruit, "Stalk", stalk, _barkMat, fruit.position, fruit.rotation, false);
                st.localPosition = Vector3.zero;
                st.localRotation = Quaternion.identity;
            }

            foreach (var sp in shape.Spots)
            {
                shape.Surface(sp.Dir, out var lp, out var ln);
                var wp = fruit.TransformPoint(lp);
                var wn = fruit.TransformDirection(ln);
                if (sp.Kind == 2)
                {
                    var f = FoodSource.Create(fruit, Taste.Bitter, wp, wn, s.Radius * 0.3f, 0.9f, 0.08f,
                        FlyMaterials.Transparent("moldFilm", new Color(0.5f, 0.7f, 0.6f, 0.08f), new Color(0.9f, 0.95f, 0.9f, 0.25f), 0.3f));
                    f.DestroyWhenEmpty = false;
                    f.Label = "плесень (горькое)";
                    OdorSource.Attach(f.gameObject, 0.35f, 10f);
                    continue;
                }
                float r = sp.Kind == 1 ? s.Radius * 0.26f : s.Radius * Mathf.Sin(sp.Angle) * 0.55f;
                var juice = FoodSource.Create(fruit, Taste.Sugar, wp + wn * 0.05f, wn, r, sp.Kind == 1 ? 0.8f : 0.65f, sp.Kind == 1 ? 0.18f : 0.1f,
                    FlyMaterials.Transparent("juice", new Color(0.85f, 0.55f, 0.2f, 0.28f), new Color(1f, 0.9f, 0.7f, 0.5f), 0.97f));
                juice.DestroyWhenEmpty = false;
                juice.MaxVolume = 1f;
                juice.Refill = 0.004f;
                juice.Label = sp.Kind == 1 ? "мякоть и сок" : "бродящий сок";
                OdorSource.Attach(juice.gameObject, sp.Kind == 1 ? 1.1f : 0.8f, r + 6f);
            }
        }

        void BuildPuddle()
        {
            var p = PuddlePosition;
            p.y = PuddleLevel;
            var water = new Material(FlyMaterials.GetShader("FlyBrainWater")) { name = "puddle" };
            var disc = MeshFactory.Disc(62f, 64);
            var surface = Visual(_static, "Puddle", disc, water, p, Quaternion.identity, false, true);
            surface.GetComponent<MeshRenderer>().receiveShadows = true;
            var f = FoodSource.Create(_static, Taste.Water, p - Vector3.up * 0.2f, Vector3.up, 58f, 1f, 0.004f);
            f.Hidden = true;
            f.DestroyWhenEmpty = false;
            f.Refill = 0.01f;
            f.Label = "лужа";
        }

        void BuildStonesAndTwigs()
        {
            var avoid = new List<Vector3>();
            foreach (var f in Fruits) avoid.Add(f.position);
            avoid.Add(PuddlePosition);
            avoid.Add(Vector3.zero);
            for (int k = 0; k < 22; k++)
            {
                var pos = RandomSpot(80, 1100, avoid, 70);
                float size = k < 4 ? Range(22, 40) : Range(5, 16);
                var radii = new Vector3(size * Range(0.9f, 1.3f), size * Range(0.45f, 0.7f), size * Range(0.8f, 1.2f));
                var col = Color.Lerp(new Color(0.5f, 0.47f, 0.42f), new Color(0.62f, 0.56f, 0.46f), R01());
                var mesh = NatureMeshes.Stone(radii, _rng.Next(), col * 0.75f, col);
                pos.y = Height(pos.x, pos.z) + radii.y * 0.35f;
                Visual(_static, "Stone", mesh, _stoneMat, pos, Quaternion.Euler(Range(-8, 8), Range(0, 360), Range(-8, 8)), true, true);
                avoid.Add(pos);
            }
            for (int k = 0; k < 9; k++)
            {
                var start = RandomSpot(60, 900, avoid, 40);
                float yaw = Range(0, 360);
                var d = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var path = new List<Vector3>();
                float len = Range(60, 200);
                float r = Range(1.8f, 4.5f);
                var p = start;
                for (int s = 0; s < 8; s++)
                {
                    var q = p;
                    q.y = Height(q.x, q.z) + r * 0.7f;
                    path.Add(q);
                    d = Quaternion.Euler(0, Range(-18, 18), 0) * d;
                    p += d * (len / 7f);
                }
                Visual(_static, "Twig", NatureMeshes.Tube("twig", path, r, r * 0.6f, 8, new Color(0.42f, 0.33f, 0.24f), new Color(0.3f, 0.24f, 0.18f), _rng.Next()),
                       _barkMat, Vector3.zero, Quaternion.identity, true, true);
            }
        }

        Vector3 RandomSpot(float minR, float maxR, List<Vector3> avoid, float clearance)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                float a = Range(0, Mathf.PI * 2);
                float r = Mathf.Sqrt(Range(minR * minR, maxR * maxR));
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (new Vector2(p.x - TrunkPosition.x, p.z - TrunkPosition.z).magnitude < 260) continue;
                bool ok = true;
                foreach (var q in avoid)
                    if (new Vector2(p.x - q.x, p.z - q.z).magnitude < clearance) { ok = false; break; }
                if (ok) return p;
            }
            return new Vector3(Range(-maxR, maxR), 0, Range(-maxR, maxR));
        }

        static Color LeafColor(float t, float v)
        {
            // late summer litter: dry brown, yellow, orange, a few still green
            if (t < 0.35f) return Color.Lerp(new Color(0.42f, 0.3f, 0.16f), new Color(0.55f, 0.4f, 0.2f), v);
            if (t < 0.6f) return Color.Lerp(new Color(0.75f, 0.58f, 0.2f), new Color(0.8f, 0.45f, 0.14f), v);
            if (t < 0.8f) return Color.Lerp(new Color(0.6f, 0.3f, 0.12f), new Color(0.45f, 0.2f, 0.1f), v);
            return Color.Lerp(new Color(0.4f, 0.5f, 0.18f), new Color(0.55f, 0.55f, 0.2f), v);
        }

        void BuildLeaves()
        {
            // walkable leaves near the fruit
            var avoid = new List<Vector3> { PuddlePosition };
            var walkable = new List<Vector3>();
            for (int k = 0; k < 48; k++)
            {
                var pos = RandomSpot(30, 800, avoid, 20);
                float len = Range(50, 85);
                var mesh = NatureMeshes.Leaf(len, len * Range(0.45f, 0.58f), Range(0.1f, 0.9f), 0.4f, LeafColor(R01(), R01()), _rng.Next());
                float e = 20f;
                var n = new Vector3(Height(pos.x - e, pos.z) - Height(pos.x + e, pos.z), 2 * e, Height(pos.x, pos.z - e) - Height(pos.x, pos.z + e)).normalized;
                var rot = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0, Range(0, 360), 0);
                var origin = new Vector3(pos.x, 0, pos.z) - rot * new Vector3(0, 0, len * 0.4f);
                // lift the rigid blade until no part of it is below the ground
                float lift = float.NegativeInfinity;
                foreach (var local in new[] { new Vector3(0, 0, 0), new Vector3(0, 0, len * 0.25f), new Vector3(0, 0, len * 0.5f), new Vector3(0, 0, len * 0.75f), new Vector3(0, 0, len),
                                              new Vector3(-len * 0.22f, 0, len * 0.45f), new Vector3(len * 0.22f, 0, len * 0.45f), new Vector3(-len * 0.12f, 0, len * 0.2f), new Vector3(len * 0.12f, 0, len * 0.7f) })
                {
                    var w = origin + rot * local;
                    lift = Mathf.Max(lift, MeshHeight(w.x, w.z) - w.y);
                }
                origin.y += lift + 0.45f;
                Visual(_static, "Leaf", mesh, _leafMat, origin, rot, true, true);
                walkable.Add(new Vector3(pos.x, len, pos.z));
            }

            // litter (not walkable, flat on the ground), in chunks for culling
            const float chunk = 700f;
            var builders = new Dictionary<Vector2Int, MeshBuilder>();
            for (int k = 0; k < 4200; k++)
            {
                float a = Range(0, Mathf.PI * 2);
                float r = Mathf.Sqrt(R01()) * 2400f;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float dt = new Vector2(p.x - TrunkPosition.x, p.z - TrunkPosition.z).magnitude;
                // more litter under the tree
                if (R01() > Mathf.Lerp(1f, 0.25f, Mathf.Clamp01((dt - 400f) / 1400f))) continue;
                if (new Vector2(p.x - PuddlePosition.x, p.z - PuddlePosition.z).magnitude < 70) continue;
                bool underLeaf = false;
                foreach (var w in walkable)
                    if (new Vector2(p.x - w.x, p.z - w.z).magnitude < w.y * 0.9f) { underLeaf = true; break; }
                if (underLeaf) continue;
                var key = new Vector2Int(Mathf.FloorToInt(p.x / chunk), Mathf.FloorToInt(p.z / chunk));
                if (!builders.TryGetValue(key, out var b)) builders[key] = b = new MeshBuilder();
                float len = Range(35, 80);
                NatureMeshes.AddLitterLeaf(b, p, Range(0, 360), len, len * Range(0.4f, 0.6f), LeafColor(R01(), R01()) * Range(0.75f, 1f), MeshHeight, _rng.Next(), Range(0.3f, 1.1f));
            }
            foreach (var kv in builders)
                Visual(_static, "Leaf litter", kv.Value.Build("litter"), _leafMat, Vector3.zero, Quaternion.identity, false);
        }

        void BuildGrass()
        {
            const float chunk = 500f;
            var builders = new Dictionary<Vector2Int, MeshBuilder>();
            MeshBuilder B(Vector3 p)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p.x / chunk), Mathf.FloorToInt(p.z / chunk));
                if (!builders.TryGetValue(key, out var b)) builders[key] = b = new MeshBuilder { UseUV2 = true };
                return b;
            }
            var fruitSpots = new List<Vector3>(Fruits.ConvertAll(f => f.position));

            // meadow ring around the tree's shade: blades grow in clumps
            int blades = 0;
            for (int k = 0; k < 40000 && blades < 90000; k++)
            {
                float a = Range(0, Mathf.PI * 2);
                float r = Mathf.Sqrt(Range(400f * 400f, 3000f * 3000f));
                var c = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float dt = new Vector2(c.x - TrunkPosition.x, c.z - TrunkPosition.z).magnitude;
                float shade = Mathf.Clamp01(1f - (dt - 450f) / 900f);
                float patch = NatureMeshes.Fbm(c.x * 0.0022f + 40, c.z * 0.0022f, 3);
                float density = Mathf.Clamp01((r - 400f) / 450f) * (1f - 0.85f * shade) * Mathf.Clamp01((patch - 0.3f) * 3f);
                if (new Vector2(c.x - PuddlePosition.x, c.z - PuddlePosition.z).magnitude < 90) density = 0;
                foreach (var f in fruitSpots)
                    if (new Vector2(c.x - f.x, c.z - f.z).magnitude < 60) { density *= 0.15f; break; }
                if (R01() > density) continue;
                float far = Mathf.Clamp01((r - 1400f) / 1600f);
                int count = _rng.Next(5, 16);
                float clumpHeight = Range(55f, 200f) * (0.75f + 0.45f * patch) * (1f + far * 0.4f);
                float spread = Range(6f, 16f) * (1f + far);
                bool dryClump = R01() < 0.15f;
                for (int m = 0; m < count; m++)
                {
                    var p = c + new Vector3(Range(-spread, spread), 0, Range(-spread, spread));
                    p.y = MeshHeight(p.x, p.z) - 1.5f;
                    var col = Color.Lerp(new Color(0.2f, 0.36f, 0.08f), new Color(0.46f, 0.56f, 0.17f), R01());
                    if (dryClump || R01() < 0.1f) col = Color.Lerp(col, new Color(0.72f, 0.64f, 0.36f), Range(0.4f, 0.95f));
                    float height = clumpHeight * Range(0.55f, 1.15f);
                    float width = Range(2.2f, 4.2f) * (1f + far * 1.2f);
                    // blades lean outwards from the clump center
                    Vector3 outward = p - c;
                    float yaw = outward.sqrMagnitude > 1f ? Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg + Range(-35, 35) : Range(0, 360);
                    NatureMeshes.AddBlade(B(p), p, height, width, yaw, Range(0.12f, 0.75f), col);
                    blades++;
                }
            }
            // sparse short tufts and moss near the fruit
            for (int k = 0; k < 260; k++)
            {
                var c = RandomSpot(40, 520, fruitSpots, 45);
                int n = _rng.Next(4, 12);
                for (int m = 0; m < n; m++)
                {
                    var p = c + new Vector3(Range(-8, 8), 0, Range(-8, 8));
                    p.y = Height(p.x, p.z) - 0.5f;
                    var col = Color.Lerp(new Color(0.3f, 0.45f, 0.12f), new Color(0.55f, 0.6f, 0.2f), R01());
                    NatureMeshes.AddBlade(B(p), p, Range(18, 55), Range(1.5f, 2.6f), Range(0, 360), Range(-0.5f, 0.5f), col);
                }
            }
            foreach (var kv in builders)
            {
                var t = Visual(_static, "Grass", kv.Value.Build("grass"), _grassMat, Vector3.zero, Quaternion.identity, false);
                t.GetComponent<MeshRenderer>().receiveShadows = true;
            }
        }

        void BuildDistance()
        {
            // hedges and trees around the orchard, softened by haze
            for (int k = 0; k < 34; k++)
            {
                float a = k / 34f * Mathf.PI * 2 + Range(-0.05f, 0.05f);
                float r = Range(4200, 6500);
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                p.y = Height(p.x, p.z);
                var radii = new Vector3(Range(500, 1200), Range(400, 900), Range(500, 1200));
                var blob = NatureMeshes.Blob(radii, 500 + k, new Color(0.12f, 0.22f, 0.07f), new Color(0.3f, 0.42f, 0.13f), 0.4f);
                Visual(_static, "Hedge", blob, _foliageMat, p + Vector3.up * radii.y * 0.6f, Quaternion.Euler(0, Range(0, 360), 0), false);
            }
            for (int k = 0; k < 7; k++)
            {
                float a = k * 0.9f + 0.6f;
                float r = Range(2600, 4000);
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (Vector3.Distance(p, TrunkPosition) < 1500) continue;
                p.y = Height(p.x, p.z) - 10f;
                Visual(_static, "Orchard tree", NatureMeshes.Trunk(Range(70, 100), 1800f, k), _barkMat, p, Quaternion.Euler(0, Range(0, 360), 0), false);
                for (int m = 0; m < 5; m++)
                {
                    var radii = new Vector3(Range(500, 800), Range(350, 550), Range(500, 800));
                    var blob = NatureMeshes.Blob(radii, 700 + k * 10 + m, new Color(0.13f, 0.25f, 0.08f), new Color(0.3f, 0.44f, 0.13f), 0.45f);
                    Visual(_static, "Orchard canopy", blob, _foliageMat, p + new Vector3(Range(-600, 600), Range(1900, 2700), Range(-600, 600)), Quaternion.identity, false);
                }
            }
        }

        // ------------------------------------------------------------------ runtime: dew

        void Update()
        {
            if (Clock == null) return;
            float h = Clock.Hour;
            // dew condenses before sunrise on cool surfaces
            if (h > 3.5f && h < 6.5f && _dew.Count < 70 && Random.value < Time.deltaTime * 3f)
            {
                var p = new Vector3(Random.Range(-750f, 750f), 0, Random.Range(-750f, 750f));
                if (SurfaceBelow(p + Vector3.up * 400f, out var hit, 0f, 900f) && hit.normal.y > 0.3f)
                {
                    var f = FoodSource.Create(_static, Taste.Water, hit.point, hit.normal, Random.Range(0.7f, 2.4f), 1f, 0.75f);
                    f.Volume = 0.1f;
                    f.MaxVolume = 1f;
                    f.Refill = 0.02f;
                    f.Label = "роса";
                    _dew.Add(f);
                }
            }
            _dew.RemoveAll(d => d == null);
            float evaporate = h > 7.2f && h < 20f ? 0.0015f + 0.004f * Clock.Daylight * Mathf.Clamp01((Clock.Temperature - 16f) / 10f) : 0f;
            foreach (var d in _dew)
            {
                d.Evaporation = evaporate;
                if (evaporate > 0) d.Refill = 0;
            }
        }

        public override Pose SpawnPose()
        {
            var p = new Vector3(8, 0, -2);
            if (SurfaceBelow(p + Vector3.up * 200f, out var hit, 0f, 500f)) p = hit.point;
            return new Pose(p, Quaternion.LookRotation(new Vector3(-0.1f, 0, 1f), Vector3.up));
        }
    }
}
