using FlyBrain.Brain;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// Live view of all 138 639 neurons at their FlyWire positions (a separate camera in a screen corner).
    /// Each neuron glows for a moment after it spikes; the resting brain is drawn dimly by cell class.
    /// </summary>
    public sealed unsafe class BrainActivityView : MonoBehaviour
    {
        public bool Visible = true;
        public Rect Viewport = new Rect(0.705f, 0.015f, 0.285f, 0.42f);
        public float SpinSpeed = 4f;

        BrainService _brain;
        Camera _camera;
        Material _material;
        GraphicsBuffer _neurons, _activity, _palette;
        float[] _act;
        float[] _decay;
        int _n;
        float _spin = 0f;
        Vector3 _center;
        float _scale;
        MaterialPropertyBlock _props;
        static readonly Vector3 Origin = new Vector3(0, -5000, 0);
        const int ViewLayer = 31;

        public static readonly Color[] Palette =
        {
            new Color(0.5f, 0.5f, 0.5f),   // unknown
            new Color(0.25f, 0.45f, 1f),   // optic
            new Color(0.75f, 0.45f, 1f),   // central
            new Color(0.3f, 1f, 0.45f),    // sensory
            new Color(0.2f, 0.9f, 1f),     // visual projection
            new Color(1f, 0.9f, 0.3f),     // ascending
            new Color(1f, 0.5f, 0.15f),    // descending
            new Color(0.65f, 1f, 0.3f),    // sensory ascending
            new Color(0.45f, 0.7f, 1f),    // visual centrifugal
            new Color(1f, 0.22f, 0.22f),   // motor
            new Color(1f, 0.4f, 0.8f),     // endocrine
        };

        public Camera Camera => _camera;

        public void Init(BrainService brain) => _brain = brain;

        void Setup()
        {
            var cat = _brain.Catalog;
            _n = cat.Count;
            var data = new Vector4[_n];
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            for (int i = 0; i < _n; i++)
            {
                var p = new Vector3(cat.Position[i * 3], cat.Position[i * 3 + 1], cat.Position[i * 3 + 2]);
                if (float.IsNaN(p.x)) continue;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            _center = (min + max) * 0.5f;
            _scale = 2f / Mathf.Max(max.x - min.x, max.y - min.y, max.z - min.z);
            for (int i = 0; i < _n; i++)
            {
                var p = new Vector3(cat.Position[i * 3], cat.Position[i * 3 + 1], cat.Position[i * 3 + 2]);
                if (float.IsNaN(p.x)) p = _center;
                // FlyWire: x lateral, y dorsal->ventral, z anterior->posterior
                var q = (p - _center) * _scale;
                data[i] = new Vector4(q.x, -q.y, -q.z, cat.SuperClass[i]);
            }
            _neurons = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _n, 16);
            _neurons.SetData(data);
            _activity = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _n, 4);
            _act = new float[_n];
            _activity.SetData(_act);
            _palette = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Palette.Length, 16);
            var pal = new Vector4[Palette.Length];
            for (int i = 0; i < pal.Length; i++) pal[i] = Palette[i];
            _palette.SetData(pal);
            _decay = new float[2000];
            for (int d = 0; d < _decay.Length; d++) _decay[d] = Mathf.Exp(-d / 250f); // 25 ms glow

            _material = new Material(FlyMaterials.GetShader("FlyBrainNeurons"));
            _props = new MaterialPropertyBlock();
            _props.SetBuffer("_Neurons", _neurons);
            _props.SetBuffer("_Activity", _activity);
            _props.SetBuffer("_Palette", _palette);

            _camera = new GameObject("Brain View Camera").AddComponent<Camera>();
            _camera.transform.SetParent(transform, false);
            _camera.cullingMask = 1 << ViewLayer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.015f, 0.02f, 0.04f, 1f);
            _camera.depth = 10;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 50f;
            _camera.fieldOfView = 30f;
            _camera.transform.SetPositionAndRotation(Origin + new Vector3(0, 0.9f, -2.9f), Quaternion.Euler(17, 0, 0));
        }

        void LateUpdate()
        {
            if (_brain == null || !_brain.IsReady) return;
            if (_camera == null) Setup();
            _camera.enabled = Visible;
            if (!Visible) return;
            _camera.rect = Viewport;

            var brain = _brain.Brain;
            int now = (int)brain.Step;
            int* last = brain.LastSpikeStep;
            int maxD = _decay.Length - 1;
            for (int i = 0; i < _n; i++)
            {
                int d = now - last[i];
                _act[i] = (uint)d <= (uint)maxD ? _decay[d] : 0f;
            }
            _activity.SetData(_act);

            _spin += Time.unscaledDeltaTime * SpinSpeed;
            var m = Matrix4x4.TRS(Origin, Quaternion.Euler(0, _spin, 0), Vector3.one);
            _props.SetMatrix("_BrainMatrix", m);
            _props.SetFloat("_PointSize", Mathf.Max(1.5f, Screen.height / 540f));
            _props.SetFloat("_BaseBrightness", 0.03f);
            var rp = new RenderParams(_material)
            {
                camera = _camera,
                layer = ViewLayer,
                worldBounds = new Bounds(Origin, Vector3.one * 10),
                matProps = _props,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, _n * 6, 1);
        }

        void OnDestroy()
        {
            _neurons?.Release();
            _activity?.Release();
            _palette?.Release();
        }
    }
}
