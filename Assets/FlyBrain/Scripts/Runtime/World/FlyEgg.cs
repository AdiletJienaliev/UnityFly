using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyBrain
{
    /// <summary>
    /// A Drosophila egg (0.5 mm, two respiratory filaments) laid into fermenting fruit. After about a day of world
    /// time a larva hatches and crawls into the fruit flesh.
    /// </summary>
    public sealed class FlyEgg : MonoBehaviour
    {
        public static readonly List<FlyEgg> All = new List<FlyEgg>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        float _age;
        Transform _larva;
        float _crawl;

        public static FlyEgg Lay(FlyRig rig, FlyMotor motor)
        {
            var up = motor.SurfaceUp;
            var tip = rig.Abdomen.Tip;
            Vector3 point = tip;
            if (Physics.Raycast(tip + up * 1f, -up, out var hit, 3f, FlyLayers.SurfaceMask)) point = hit.point;
            var env = FlyEnvironment.Current;
            var go = new GameObject("Fly egg");
            go.transform.SetParent(env != null ? env.transform : null, true);
            go.transform.SetPositionAndRotation(point + up * 0.1f, Quaternion.LookRotation(Vector3.ProjectOnPlane(-rig.Root.forward, up).normalized + up * 0.3f, up));
            var white = FlyMaterials.Lit("egg", new Color(0.95f, 0.94f, 0.9f), 0.55f, new Color(0.25f, 0.25f, 0.25f));
            Part(go.transform, MeshFactory.Ellipsoid(new Vector3(0.1f, 0.1f, 0.26f), 12, 8), white, Vector3.zero, Quaternion.identity);
            var filament = MeshFactory.Segment(0.28f, 0.015f, 0.008f, 4);
            Part(go.transform, filament, white, new Vector3(-0.03f, 0.05f, 0.22f), Quaternion.Euler(-35, -12, 0));
            Part(go.transform, filament, white, new Vector3(0.03f, 0.05f, 0.22f), Quaternion.Euler(-35, 12, 0));
            go.layer = FlyLayers.Creatures;
            return go.AddComponent<FlyEgg>();
        }

        static Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.layer = FlyLayers.Creatures;
            return go.transform;
        }

        void Update()
        {
            var clock = FlyEnvironment.Current != null ? FlyEnvironment.Current.Clock : null;
            float day = clock != null ? clock.DayLengthSeconds : 1440f;
            _age += Time.deltaTime / day;
            if (_larva == null && _age > 0.9f)
            {
                // first instar larva: a translucent white maggot that crawls away and burrows
                var mat = FlyMaterials.Lit("larva", new Color(0.93f, 0.92f, 0.86f), 0.7f, new Color(0.3f, 0.3f, 0.3f));
                _larva = Part(transform, MeshFactory.Ellipsoid(new Vector3(0.12f, 0.1f, 0.5f), 12, 8), mat, new Vector3(0, 0.05f, 0.4f), Quaternion.identity);
                Part(_larva, MeshFactory.Ellipsoid(new Vector3(0.05f, 0.04f, 0.05f), 6, 4), FlyMaterials.Dark(), new Vector3(0, 0.02f, 0.5f), Quaternion.identity);
            }
            if (_larva != null)
            {
                _crawl += Time.deltaTime;
                float wave = Mathf.Sin(_crawl * 6f);
                _larva.localScale = new Vector3(1f - 0.1f * wave, 1f, 1f + 0.2f * wave);
                _larva.localPosition += new Vector3(Mathf.Sin(_crawl * 0.7f) * 0.02f, 0, 0.05f) * Time.deltaTime;
                if (_age > 1.6f) Destroy(gameObject); // burrowed into the fruit
            }
        }
    }
}
