using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyBrain
{
    /// <summary>
    /// Orbit camera that follows the fly. RMB drag: orbit, wheel: zoom, MMB drag: pan, F: follow on/off,
    /// C: cinematic mode (slow automatic orbit). The camera backs off while the fly is flying and does not
    /// pass through fruit, leaves or the ground.
    /// </summary>
    public sealed class FlyCamera : MonoBehaviour
    {
        public Transform Target;
        public FlyMotor Motor;
        public bool Follow = true;
        public bool Cinematic;
        public float Distance = 14f;
        public float Yaw = 150f;
        public float Pitch = 22f;
        public float MinDistance = 2.5f, MaxDistance = 4000f;
        public System.Func<Vector2, bool> IsOverUI;

        Vector3 _pivot;
        Vector3 _panOffset;
        float _smoothDistance, _flightZoom = 1f, _cineTimer, _cineYawSpeed = 5f;

        public Camera Camera { get; private set; }

        public static FlyCamera Create(Transform target, FlyMotor motor)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40000f;
            cam.fieldOfView = 45f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.7f, 0.78f);
            go.AddComponent<AudioListener>();
            var fc = go.AddComponent<FlyCamera>();
            fc.Camera = cam;
            fc.Target = target;
            fc.Motor = motor;
            fc._pivot = target != null ? target.position : Vector3.zero;
            fc._smoothDistance = fc.Distance;
            return fc;
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            bool overUI = mouse != null && IsOverUI != null && IsOverUI(mouse.position.ReadValue());

            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed)
                {
                    Yaw += delta.x * 0.25f;
                    Pitch = Mathf.Clamp(Pitch - delta.y * 0.25f, -20f, 89f);
                    Cinematic = false;
                }
                if (mouse.middleButton.isPressed)
                {
                    float scale = _smoothDistance * 0.0015f;
                    _panOffset += (-transform.right * delta.x - transform.up * delta.y) * scale;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (!overUI && Mathf.Abs(scroll) > 0.01f)
                    Distance = Mathf.Clamp(Distance * Mathf.Pow(0.9f, Mathf.Sign(scroll)), MinDistance, MaxDistance);
            }
            if (kb != null && kb.fKey.wasPressedThisFrame)
            {
                Follow = !Follow;
                if (Follow) _panOffset = Vector3.zero;
            }
            if (kb != null && kb.cKey.wasPressedThisFrame) Cinematic = !Cinematic;

            if (Cinematic)
            {
                _cineTimer -= dt;
                if (_cineTimer <= 0)
                {
                    _cineTimer = Random.Range(6f, 14f);
                    _cineYawSpeed = Random.Range(-9f, 9f);
                }
                Yaw += _cineYawSpeed * dt;
                Pitch = Mathf.Lerp(Pitch, 18f + 12f * Mathf.Sin(Time.unscaledTime * 0.05f), dt * 0.3f);
            }

            // back off while flying so the fly stays in view
            bool flying = Motor != null && Motor.IsAirborne;
            _flightZoom = Mathf.Lerp(_flightZoom, flying ? Mathf.Clamp(90f / Mathf.Max(Distance, 1f), 1f, 8f) : 1f, 1 - Mathf.Exp(-dt * (flying ? 1.5f : 0.8f)));

            if (Follow && Target != null)
            {
                float follow = flying ? 7f : 6f;
                _pivot = Vector3.Lerp(_pivot, Target.position + Vector3.up * 0.5f, 1 - Mathf.Exp(-dt * follow));
            }
            _smoothDistance = Mathf.Lerp(_smoothDistance, Distance * _flightZoom, 1 - Mathf.Exp(-dt * 6));
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            Vector3 center = _pivot + _panOffset;
            Vector3 back = -(rot * Vector3.forward);
            float dist = _smoothDistance;
            // keep the view clear of fruit, leaves and the ground
            float radius = Mathf.Clamp(dist * 0.004f, 0.04f, 1.5f);
            float start = Mathf.Min(dist, 1.5f + radius);
            // close up the camera must not end inside fruit or bark; from afar it may look through a leaf
            if (dist > start && dist < 45f && Physics.SphereCast(center + back * start, radius, back, out var hit, dist - start, FlyLayers.SurfaceMask, QueryTriggerInteraction.Ignore))
                dist = start + hit.distance;
            Vector3 pos = center + back * dist;
            float groundY = FlyEnvironment.Current is NatureEnvironment ? NatureEnvironment.Height(pos.x, pos.z) : 0.06f;
            pos.y = Mathf.Max(pos.y, groundY + Mathf.Clamp(dist * 0.02f, 0.15f, 3f));
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(center - pos));
        }
    }
}
