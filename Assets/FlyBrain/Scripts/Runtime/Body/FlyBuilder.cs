using UnityEngine;
using UnityEngine.Rendering;

namespace FlyBrain
{
    /// <summary>
    /// Builds a Drosophila melanogaster body at 1 unit = 1 mm: thorax, head with compound eyes, antennae with aristae,
    /// proboscis, banded abdomen, wings, halteres and six 4-segment legs. Every bone is created with identity rotation;
    /// the pose comes from the IK chains only.
    /// </summary>
    public static class FlyBuilder
    {
        public const int IgnoreRaycastLayer = 2;

        public static FlyRig Build(Transform parent, Vector3 position, float yawDegrees, bool male = false)
        {
            var rig = new FlyRig();
            rig.Root = new GameObject("Fly").transform;
            rig.Root.SetParent(parent, false);
            rig.Root.SetPositionAndRotation(position, Quaternion.Euler(0, yawDegrees, 0));

            rig.Body = Bone(rig.Root, "Body (thorax)", new Vector3(0, FlyRig.StandHeight, 0));
            BuildThorax(rig);
            BuildHead(rig);
            BuildAbdomen(rig, male);
            BuildWings(rig);
            BuildLegs(rig);

            foreach (var t in rig.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IgnoreRaycastLayer;
            return rig;
        }

        // ------------------------------------------------------------------ parts

        static void BuildThorax(FlyRig rig)
        {
            var b = rig.Body;
            Visual(b, "Thorax", MeshFactory.Ellipsoid(new Vector3(0.42f, 0.4f, 0.54f), 32, 24), FlyMaterials.Thorax(), new Vector3(0, 0.04f, 0), Quaternion.Euler(-6, 0, 0));
            Visual(b, "Scutellum", MeshFactory.Ellipsoid(new Vector3(0.2f, 0.1f, 0.16f)), FlyMaterials.Thorax(), new Vector3(0, 0.34f, -0.4f), Quaternion.Euler(-20, 0, 0));
            Visual(b, "Pleura", MeshFactory.Ellipsoid(new Vector3(0.37f, 0.27f, 0.42f)), FlyMaterials.Head(), new Vector3(0, -0.13f, 0.02f), Quaternion.identity);

            // dorsocentral and scutellar macrochaetae
            var bristle = MeshFactory.Segment(0.26f, 0.011f, 0.003f, 5);
            Vector3[] spots =
            {
                new Vector3(0.12f, 0.41f, 0.18f), new Vector3(0.13f, 0.43f, -0.05f), new Vector3(0.29f, 0.33f, 0.14f),
                new Vector3(0.34f, 0.24f, -0.14f), new Vector3(0.1f, 0.41f, -0.44f), new Vector3(0.05f, 0.4f, -0.52f),
            };
            foreach (var s in spots)
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = new Vector3(s.x * side, s.y, s.z);
                    Visual(b, "Bristle", bristle, FlyMaterials.Dark(), p, Quaternion.LookRotation(new Vector3(side * 0.22f, 0.22f, -1f)), false);
                }

            var col = b.gameObject.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.center = new Vector3(0, 0, -0.4f);
            col.radius = 0.45f;
            col.height = 2.7f;
            rig.Colliders.Add(col);
        }

        static void BuildHead(FlyRig rig)
        {
            var neck = Bone(rig.Body, "Head", new Vector3(0, 0.12f, 0.48f));
            rig.Head = new IKChain("head", new[] { neck }, new[] { 0.3f });
            rig.HeadVisual = neck;

            Visual(neck, "Head capsule", MeshFactory.Ellipsoid(new Vector3(0.28f, 0.26f, 0.19f), 24, 16), FlyMaterials.Head(), new Vector3(0, 0.02f, 0.17f), Quaternion.identity);
            Visual(neck, "Face", MeshFactory.Ellipsoid(new Vector3(0.12f, 0.2f, 0.08f)), FlyMaterials.Proboscis(), new Vector3(0, -0.04f, 0.33f), Quaternion.Euler(10, 0, 0));
            for (int side = -1; side <= 1; side += 2)
                Visual(neck, side < 0 ? "Compound eye L" : "Compound eye R", MeshFactory.Ellipsoid(new Vector3(0.16f, 0.25f, 0.21f), 32, 24), FlyMaterials.Eye(),
                       new Vector3(side * 0.22f, 0.03f, 0.19f), Quaternion.Euler(0, side * 14f, 0));
            var ocellus = MeshFactory.Ellipsoid(new Vector3(0.03f, 0.02f, 0.03f), 8, 6);
            Visual(neck, "Ocellus", ocellus, FlyMaterials.Dark(), new Vector3(0, 0.285f, 0.2f), Quaternion.identity, false);
            Visual(neck, "Ocellus", ocellus, FlyMaterials.Dark(), new Vector3(-0.05f, 0.27f, 0.13f), Quaternion.identity, false);
            Visual(neck, "Ocellus", ocellus, FlyMaterials.Dark(), new Vector3(0.05f, 0.27f, 0.13f), Quaternion.identity, false);
            var vib = MeshFactory.Segment(0.22f, 0.01f, 0.003f, 5);
            for (int side = -1; side <= 1; side += 2)
            {
                Visual(neck, "Orbital bristle", vib, FlyMaterials.Dark(), new Vector3(side * 0.12f, 0.25f, 0.22f), Quaternion.LookRotation(new Vector3(side * 0.3f, 0.6f, -1f)), false);
                Visual(neck, "Vertical bristle", vib, FlyMaterials.Dark(), new Vector3(side * 0.16f, 0.24f, 0.05f), Quaternion.LookRotation(new Vector3(side * 0.6f, 0.5f, -1f)), false);
                Visual(neck, "Vibrissa", vib, FlyMaterials.Dark(), new Vector3(side * 0.1f, -0.18f, 0.32f), Quaternion.LookRotation(new Vector3(side * 0.4f, -0.4f, 1f)), false);
            }

            var col = neck.gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0, 0.2f);
            col.radius = 0.42f;
            rig.Colliders.Add(col);

            // antennae: scape+pedicel, funiculus (3rd segment) with the feathery arista
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                string n = side < 0 ? "L" : "R";
                var a0 = Bone(neck, "Antenna " + n + " scape", new Vector3(side * 0.07f, 0.07f, 0.36f));
                Visual(a0, "Pedicel", MeshFactory.Segment(0.13f, 0.04f, 0.035f, 8), FlyMaterials.Head(), Vector3.zero, Quaternion.identity);
                var a1 = Bone(a0, "Antenna " + n + " funiculus", new Vector3(0, 0, 0.13f));
                Visual(a1, "Funiculus", MeshFactory.Ellipsoid(new Vector3(0.055f, 0.07f, 0.11f), 12, 8), FlyMaterials.Proboscis(), new Vector3(0, 0, 0.09f), Quaternion.identity);
                var arista = new GameObject("Arista").transform;
                arista.SetParent(a1, false);
                arista.localPosition = new Vector3(side * 0.035f, 0.02f, 0.1f);
                arista.localRotation = Quaternion.LookRotation(new Vector3(side * 0.9f, 0.5f, 0.6f));
                Visual(arista, "Arista shaft", MeshFactory.Segment(0.4f, 0.012f, 0.003f, 5), FlyMaterials.Dark(), Vector3.zero, Quaternion.identity, false);
                var hair = MeshFactory.Segment(0.09f, 0.004f, 0.002f, 4);
                for (int h = 0; h < 7; h++)
                {
                    float z = 0.08f + h * 0.042f;
                    Visual(arista, "Arista branch", hair, FlyMaterials.Dark(), new Vector3(0, 0, z), Quaternion.Euler(h % 2 == 0 ? -50 : 50, 0, 0), false);
                }
                rig.Antennae[s] = new IKChain("antenna " + n, new[] { a0, a1 }, new[] { 0.13f, 0.2f });
                rig.Aristae[s] = arista;
            }

            // proboscis: rostrum, haustellum, labellum with two lobes
            var p0 = Bone(neck, "Proboscis rostrum", new Vector3(0, -0.2f, 0.24f));
            Visual(p0, "Rostrum", MeshFactory.Segment(0.2f, 0.08f, 0.06f), FlyMaterials.Proboscis(), Vector3.zero, Quaternion.identity);
            var p1 = Bone(p0, "Proboscis haustellum", new Vector3(0, 0, 0.2f));
            Visual(p1, "Haustellum", MeshFactory.Segment(0.24f, 0.05f, 0.045f), FlyMaterials.Head(), Vector3.zero, Quaternion.identity);
            var p2 = Bone(p1, "Proboscis labellum", new Vector3(0, 0, 0.24f));
            Visual(p2, "Labellum base", MeshFactory.Segment(0.1f, 0.045f, 0.05f), FlyMaterials.Proboscis(), Vector3.zero, Quaternion.identity);
            rig.Proboscis = new IKChain("proboscis", new[] { p0, p1, p2 }, new[] { 0.2f, 0.24f, 0.1f });
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                var lobe = Bone(p2, "Labellar lobe", new Vector3(side * 0.025f, 0, 0.07f));
                Visual(lobe, "Lobe", MeshFactory.Ellipsoid(new Vector3(0.045f, 0.03f, 0.085f), 12, 8), FlyMaterials.Proboscis(), new Vector3(0, 0, 0.06f), Quaternion.identity);
                rig.LabellumLobes[s] = new IKChain("labellum lobe", new[] { lobe }, new[] { 0.12f });
            }
        }

        static void BuildAbdomen(FlyRig rig, bool male)
        {
            var a0 = Bone(rig.Body, "Abdomen A1-A4", new Vector3(0, 0.0f, -0.42f));
            // mesh +Z faces the thorax so the texture's dark posterior bands end up at the tip
            Visual(a0, "Abdomen", MeshFactory.Ellipsoid(new Vector3(0.39f, 0.35f, 0.64f), 32, 24), FlyMaterials.Abdomen(male), new Vector3(0, 0, 0.5f), Quaternion.Euler(0, 180, 0));
            var a1 = Bone(a0, "Abdomen A5-A7", new Vector3(0, 0, 0.5f));
            // males: shorter, rounded, with a fully dark tip
            Visual(a1, "Abdomen tip", MeshFactory.Ellipsoid(male ? new Vector3(0.22f, 0.2f, 0.17f) : new Vector3(0.2f, 0.17f, 0.24f)), male ? FlyMaterials.Dark() : FlyMaterials.AbdomenTip(), new Vector3(0, 0, 0.36f), Quaternion.identity);
            rig.Abdomen = new IKChain("abdomen", new[] { a0, a1 }, new[] { 0.5f, 0.5f });
        }

        static void BuildWings(FlyRig rig)
        {
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                var hinge = Bone(rig.Body, side < 0 ? "Wing L" : "Wing R", new Vector3(side * 0.22f, 0.4f + s * 0.015f, 0.1f));
                var wing = Visual(hinge, "Wing membrane", MeshFactory.WingQuad(2.0f, side * 0.85f), FlyMaterials.Wing(), new Vector3(0, 0, -0.04f), Quaternion.identity, false);
                wing.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                rig.Wings[s] = new IKChain("wing", new[] { hinge }, new[] { 1.9f });
                var blur = Visual(rig.Body, "Wing beat blur", MeshFactory.Fan(2.05f, 72f, side), FlyMaterials.WingBlur(), hinge.localPosition, Quaternion.identity, false);
                blur.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                blur.gameObject.SetActive(false);
                rig.WingBlur[s] = blur;

                var haltere = Bone(rig.Body, "Haltere", new Vector3(side * 0.28f, 0.08f, -0.36f));
                Visual(haltere, "Haltere stalk", MeshFactory.Segment(0.18f, 0.016f, 0.012f, 6), FlyMaterials.Proboscis(), Vector3.zero, Quaternion.identity, false);
                Visual(haltere, "Haltere knob", MeshFactory.Ellipsoid(new Vector3(0.05f, 0.05f, 0.065f), 10, 8), FlyMaterials.Proboscis(), new Vector3(0, 0, 0.2f), Quaternion.identity, false);
                rig.Halteres[s] = new IKChain("haltere", new[] { haltere }, new[] { 0.22f });
            }
        }

        static void BuildLegs(FlyRig rig)
        {
            rig.Legs = new LegRig[6];
            Vector3[] roots = { new Vector3(0.17f, -0.26f, 0.34f), new Vector3(0.24f, -0.28f, 0.06f), new Vector3(0.26f, -0.28f, -0.2f) };
            float[,] lengths = { { 0.26f, 0.5f, 0.46f, 0.52f }, { 0.18f, 0.56f, 0.54f, 0.56f }, { 0.2f, 0.6f, 0.56f, 0.52f } };
            Vector3[] homes = { new Vector3(0.72f, 0, 1.15f), new Vector3(1.3f, 0, 0.05f), new Vector3(1.02f, 0, -0.98f) };
            string[] pairNames = { "front", "middle", "hind" };
            float[] tarsomeres = { 0.34f, 0.2f, 0.16f, 0.14f, 0.16f };

            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                for (int pair = 0; pair < 3; pair++)
                {
                    string name = (side < 0 ? "L" : "R") + (pair + 1) + " " + pairNames[pair];
                    float lc = lengths[pair, 0], lf = lengths[pair, 1], lt = lengths[pair, 2], ltar = lengths[pair, 3];
                    var r = roots[pair];
                    var coxa = Bone(rig.Body, name + " coxa", new Vector3(side * r.x, r.y, r.z));
                    var femur = Bone(coxa, name + " femur", new Vector3(0, 0, lc));
                    var tibia = Bone(femur, name + " tibia", new Vector3(0, 0, lf));
                    var tarsus = Bone(tibia, name + " tarsus", new Vector3(0, 0, lt));

                    Visual(coxa, "Coxa", MeshFactory.Segment(lc, 0.07f, 0.055f), FlyMaterials.Leg(), Vector3.zero, Quaternion.identity);
                    Visual(femur, "Femur", MeshFactory.Segment(lf, 0.052f, 0.038f), FlyMaterials.Leg(), Vector3.zero, Quaternion.identity);
                    Visual(tibia, "Tibia", MeshFactory.Segment(lt, 0.032f, 0.025f), FlyMaterials.Leg(), Vector3.zero, Quaternion.identity);
                    float z = 0;
                    for (int k = 0; k < tarsomeres.Length; k++)
                    {
                        float len = tarsomeres[k] * ltar;
                        Visual(tarsus, "Tarsomere " + (k + 1), MeshFactory.Segment(len * 0.92f, 0.022f - k * 0.0015f, 0.018f - k * 0.0015f, 6), FlyMaterials.Leg(), new Vector3(0, 0, z), Quaternion.identity, false);
                        z += len;
                    }
                    var claw = MeshFactory.Segment(0.06f, 0.01f, 0.003f, 4);
                    Visual(tarsus, "Claw", claw, FlyMaterials.Dark(), new Vector3(0.01f, 0, z - 0.02f), Quaternion.Euler(40, 25, 0), false);
                    Visual(tarsus, "Claw", claw, FlyMaterials.Dark(), new Vector3(-0.01f, 0, z - 0.02f), Quaternion.Euler(40, -25, 0), false);
                    // tibial bristles
                    var hair = MeshFactory.Segment(0.1f, 0.006f, 0.002f, 4);
                    for (int h = 0; h < 4; h++)
                        Visual(tibia, "Bristle", hair, FlyMaterials.Dark(), new Vector3(0, 0.03f, lt * (0.2f + 0.2f * h)), Quaternion.Euler(-25, h * 70, 0), false);

                    rig.Legs[FlyRig.LegIndex(side, pair)] = new LegRig
                    {
                        Name = name,
                        Side = side,
                        Pair = pair,
                        Chain = new IKChain(name, new[] { coxa, femur, tibia }, new[] { lc, lf, lt }),
                        Tarsus = new IKChain(name + " tarsus", new[] { tarsus }, new[] { ltar }),
                        TarsusLength = ltar,
                        HomeLocal = new Vector3(side * homes[pair].x, 0, homes[pair].z),
                    };
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        static Transform Bone(Transform parent, string name, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;
            return t;
        }

        static Transform Visual(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go.transform;
        }
    }
}
