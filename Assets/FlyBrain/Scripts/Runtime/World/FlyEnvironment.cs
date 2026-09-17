using UnityEngine;

namespace FlyBrain
{
    /// <summary>Physics layers used by the demo (layers are addressed by index, no TagManager entries needed).</summary>
    public static class FlyLayers
    {
        /// <summary>Static world: every collider on this layer can be walked on and landed on.</summary>
        public const int Surface = 0;
        /// <summary>Fly bodies (the brain-driven fly and the other flies): ignored by surface and vision rays.</summary>
        public const int Flies = 2;
        /// <summary>Animals and stimuli that are seen but not walked on (spider, bird, ants, threats).</summary>
        public const int Creatures = 9;

        public const int SurfaceMask = 1 << Surface;
        public const int VisionMask = (1 << Surface) | (1 << Creatures);
    }

    /// <summary>
    /// A world the fly lives in. <see cref="NatureEnvironment"/> is the orchard floor with fallen fruit (default),
    /// <see cref="LabEnvironment"/> the original petri dish arena used for the classic experiments.
    /// </summary>
    public abstract class FlyEnvironment : MonoBehaviour
    {
        public static FlyEnvironment Current { get; private set; }

        /// <summary>Parent of objects placed by the user (drops, puffs, threats); cleared with Del.</summary>
        public Transform Dynamic { get; protected set; }
        public Light Sun { get; protected set; }
        /// <summary>Day and night; null in the laboratory (constant light).</summary>
        public DayCycle Clock { get; protected set; }
        public WindField Wind { get; protected set; }

        /// <summary>Soft boundary of the region the fly explores (the fly turns back beyond it).</summary>
        public Vector3 Center = Vector3.zero;
        public float Radius = 60f;

        public abstract string DisplayName { get; }
        public virtual bool IsNature => false;
        /// <summary>Whether the fly may take off on its own (the laboratory arena is too small; escapes still happen).</summary>
        public virtual bool AllowVoluntaryFlight => IsNature;
        /// <summary>Illumination 0 (moonless night) .. 1 (full daylight), scales vision.</summary>
        public virtual float LightLevel => Clock != null ? Clock.LightLevel : 1f;

        protected abstract void Build();
        public abstract Pose SpawnPose();
        public virtual void SpawnDefaultFood() { }

        public static T Create<T>() where T : FlyEnvironment
        {
            var env = new GameObject(typeof(T).Name).AddComponent<T>();
            Current = env;
            env.Dynamic = new GameObject("Dynamic objects").transform;
            env.Dynamic.SetParent(env.transform, false);
            env.Build();
            return env;
        }

        protected virtual void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        public FoodSource SpawnFood(Taste taste, Vector3 position, Vector3 normal, float radius = 1.8f, float concentration = 1f)
        {
            return FoodSource.Create(Dynamic, taste, position, normal, radius, concentration);
        }

        public void ClearDynamic()
        {
            for (int i = Dynamic.childCount - 1; i >= 0; i--) Destroy(Dynamic.GetChild(i).gameObject);
        }

        /// <summary>Closest walkable surface below a point (along world down), or false.</summary>
        public static bool SurfaceBelow(Vector3 p, out RaycastHit hit, float above = 40f, float range = 400f)
        {
            return Physics.Raycast(p + Vector3.up * above, Vector3.down, out hit, above + range, FlyLayers.SurfaceMask, QueryTriggerInteraction.Ignore);
        }

        protected static Transform Visual(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot,
                                          bool castShadows = true, bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go.transform;
        }
    }
}
