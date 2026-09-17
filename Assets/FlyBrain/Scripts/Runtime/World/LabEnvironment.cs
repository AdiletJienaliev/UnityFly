using UnityEngine;
using UnityEngine.Rendering;

namespace FlyBrain
{
    /// <summary>The laboratory: a petri dish arena on a wooden table with a few props (constant light, still air).</summary>
    public sealed class LabEnvironment : FlyEnvironment
    {
        public const float ArenaRadius = 60f;

        public override string DisplayName => "Лаборатория (чашка Петри)";

        protected override void Build()
        {
            Radius = ArenaRadius - 2f;

            // lighting
            Sun = new GameObject("Lamp").AddComponent<Light>();
            Sun.transform.SetParent(transform, false);
            Sun.type = LightType.Directional;
            Sun.color = new Color(1f, 0.95f, 0.86f);
            Sun.intensity = 1.0f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.75f;
            Sun.shadowBias = 0.03f;
            Sun.shadowNormalBias = 0.2f;
            Sun.transform.rotation = Quaternion.Euler(55, -35, 0);
            RenderSettings.sun = Sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.46f, 0.53f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.34f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.1f);
            RenderSettings.fog = false;
            QualitySettings.shadowDistance = 160f;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

            // table
            var table = GameObject.CreatePrimitive(PrimitiveType.Plane);
            table.name = "Table";
            table.transform.SetParent(transform, false);
            table.transform.localScale = new Vector3(100, 1, 100);
            var wood = FlyMaterials.Lit("wood", Color.white, 0.35f, new Color(0.08f, 0.07f, 0.06f), FlyMaterials.WoodTexture());
            wood.mainTextureScale = new Vector2(3, 3);
            table.GetComponent<MeshRenderer>().sharedMaterial = wood;

            // petri dish: filter paper floor and a glass wall
            var floor = Visual(transform, "Filter paper", MeshFactory.Disc(ArenaRadius + 1f), FlyMaterials.Lit("paper", Color.white, 0.12f, new Color(0.03f, 0.03f, 0.03f),
                FlyMaterials.Noise("paperTex", 256, new Color(0.8f, 0.78f, 0.72f), new Color(0.7f, 0.68f, 0.62f), 40f, false)), new Vector3(0, 0.06f, 0), Quaternion.identity);
            floor.gameObject.AddComponent<MeshCollider>().sharedMesh = MeshFactory.Disc(ArenaRadius + 1f);
            var glass = FlyMaterials.Transparent("glass", new Color(0.8f, 0.9f, 0.95f, 0.08f), new Color(0.9f, 0.97f, 1f, 0.45f), 0.98f);
            var wall = Visual(transform, "Petri dish wall", MeshFactory.Ring(ArenaRadius + 1.2f, 10f, 1f), glass, Vector3.zero, Quaternion.identity, false);
            wall.gameObject.layer = FlyLayers.Creatures; // seen, but the fly does not climb the glass
            Visual(transform, "Petri dish base", MeshFactory.Disc(ArenaRadius + 2.2f), glass, new Vector3(0, 0.02f, 0), Quaternion.identity, false);

            // props for scale
            var orange = FlyMaterials.Lit("orange", Color.white, 0.55f, new Color(0.2f, 0.12f, 0.05f),
                FlyMaterials.Noise("orangeTex", 256, new Color(1f, 0.55f, 0.1f), new Color(0.85f, 0.35f, 0.05f), 60f, false), new Color(0.4f, 0.2f, 0.05f), 3f);
            Visual(transform, "Orange", MeshFactory.Drop(34f, 30f), orange, new Vector3(120, 0, 70), Quaternion.identity);
            var leafMat = FlyMaterials.Lit("leaf", Color.white, 0.45f, new Color(0.08f, 0.1f, 0.06f),
                FlyMaterials.Noise("leafTex", 256, new Color(0.25f, 0.55f, 0.18f), new Color(0.12f, 0.35f, 0.1f), 12f, true));
            Visual(transform, "Leaf", MeshFactory.Ellipsoid(new Vector3(22f, 0.6f, 48f), 32, 12), leafMat, new Vector3(-105, 0.6f, 60), Quaternion.Euler(0, 35, 0));
            var pencil = FlyMaterials.Lit("pencil", new Color(0.98f, 0.78f, 0.12f), 0.6f, new Color(0.2f, 0.18f, 0.1f));
            Visual(transform, "Pencil", MeshFactory.Segment(150f, 3.6f, 3.6f, 6), pencil, new Vector3(-200, 3.6f, -110), Quaternion.Euler(0, 80, 30));
            Visual(transform, "Pencil tip", MeshFactory.Segment(18f, 3.4f, 0.4f, 12), FlyMaterials.Lit("pencilWood", new Color(0.85f, 0.7f, 0.5f), 0.2f), new Vector3(-52.3f, 3.6f, -83.9f), Quaternion.Euler(0, 80, 0));
            var metal = FlyMaterials.Lit("coin", new Color(0.72f, 0.6f, 0.38f), 0.85f, new Color(0.8f, 0.7f, 0.5f));
            Visual(transform, "Coin", MeshFactory.Ring(12f, 1.6f, 0.01f), metal, new Vector3(90, 0, -85), Quaternion.identity);
            Visual(transform, "Coin face", MeshFactory.Disc(12f), metal, new Vector3(90, 1.6f, -85), Quaternion.identity);
            var sugar = FlyMaterials.Lit("sugarCube", new Color(0.97f, 0.97f, 0.95f), 0.25f, new Color(0.1f, 0.1f, 0.1f),
                FlyMaterials.Noise("sugarTex", 128, new Color(1f, 1f, 1f), new Color(0.85f, 0.85f, 0.83f), 80f, false));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Sugar cube";
            cube.transform.SetParent(transform, false);
            cube.transform.SetPositionAndRotation(new Vector3(35, 7, 100), Quaternion.Euler(0, 20, 0));
            cube.transform.localScale = Vector3.one * 14;
            cube.GetComponent<MeshRenderer>().sharedMaterial = sugar;
        }

        public override Pose SpawnPose() => new Pose(new Vector3(0, 0.06f, 0), Quaternion.identity);

        public override void SpawnDefaultFood()
        {
            SpawnFood(Taste.Sugar, new Vector3(5f, 0.06f, 24f), Vector3.up, 1.8f);
            SpawnFood(Taste.Water, new Vector3(-20f, 0.06f, -8f), Vector3.up, 1.6f);
            SpawnFood(Taste.Bitter, new Vector3(18f, 0.06f, -18f), Vector3.up, 1.8f);
        }
    }
}
