using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyBrain
{
    public enum Tool { None, Sugar, Water, Bitter, AirPuff, Dust, Threat, Target, Spider, Bird }

    /// <summary>Mouse and keyboard tools that change the fly's world (drops, air, dust, threats, animals).</summary>
    public sealed class InteractionTools : MonoBehaviour
    {
        public Tool Current = Tool.None;
        FlyBrainApp _app;

        public static readonly (Tool tool, string key, string label)[] Catalog =
        {
            (Tool.Sugar, "1", "Сахар"),
            (Tool.Water, "2", "Вода"),
            (Tool.Bitter, "3", "Горечь"),
            (Tool.AirPuff, "4", "Воздух"),
            (Tool.Dust, "5", "Пыль"),
            (Tool.Threat, "6", "Угроза"),
            (Tool.Target, "7", "Точка"),
            (Tool.Spider, "8", "Паук"),
            (Tool.Bird, "9", "Птица"),
        };

        public void Init(FlyBrainApp app) => _app = app;

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;
            if (_app.Hud != null && _app.Hud.IsTyping) return;

            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < keys.Length; i++)
                if (kb[keys[i]].wasPressedThisFrame) Select(Catalog[i].tool);
            if (kb.escapeKey.wasPressedThisFrame) Current = Tool.None;
            if (kb.deleteKey.wasPressedThisFrame) _app.World.ClearDynamic();

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Current != Tool.None)
            {
                Vector2 pos = mouse.position.ReadValue();
                if (_app.Hud == null || !_app.Hud.IsOverUI(pos)) Apply(Current, pos);
            }
        }

        public void Select(Tool tool)
        {
            if ((tool == Tool.Spider || tool == Tool.Bird) && _app.Wildlife == null) return;
            Current = Current == tool ? Tool.None : tool;
        }

        public void Apply(Tool tool, Vector2 screenPosition)
        {
            var cam = _app.Cam.Camera;
            var ray = cam.ScreenPointToRay(screenPosition);
            var head = _app.Rig.HeadVisual;
            switch (tool)
            {
                case Tool.Sugar:
                case Tool.Water:
                case Tool.Bitter:
                    if (Physics.Raycast(ray, out var hit, 50000f, FlyLayers.SurfaceMask))
                        _app.World.SpawnFood(tool == Tool.Sugar ? Taste.Sugar : tool == Tool.Water ? Taste.Water : Taste.Bitter, hit.point, hit.normal, 1.8f);
                    break;
                case Tool.AirPuff:
                    PuffAt(cam.transform.position);
                    break;
                case Tool.Dust:
                {
                    int side = 0;
                    if (Physics.Raycast(ray, out var flyHit, 50000f, 1 << FlyLayers.Flies))
                        side = Vector3.Dot(flyHit.point - head.position, head.right) < 0 ? -1 : 1;
                    SprinkleDust(side, 6);
                    break;
                }
                case Tool.Threat:
                    LaunchThreat(cam.transform.position);
                    break;
                case Tool.Target:
                    MovingTarget.Create(_app.World.Dynamic, _app.Rig.Root.position, _app.Motor.SurfaceUp, Random.Range(0f, Mathf.PI * 2));
                    break;
                case Tool.Spider:
                    _app.Wildlife?.SendSpider();
                    break;
                case Tool.Bird:
                    _app.Wildlife?.SwoopBird();
                    break;
            }
        }

        public void PuffAt(Vector3 from)
        {
            var head = _app.Rig.HeadVisual.position;
            Vector3 dir = (head - from).normalized;
            WindPuff.Create(_app.World.Dynamic, head - dir * 30f, dir, 450f);
        }

        /// <summary>Side -1 = left eye, +1 = right eye, 0 = both.</summary>
        public void SprinkleDust(int side, int count)
        {
            var head = _app.Rig.HeadVisual;
            for (int k = 0; k < count; k++)
            {
                int s = side != 0 ? side : (k % 2 == 0 ? -1 : 1);
                Vector3 dir = Random.onUnitSphere;
                dir.x = Mathf.Abs(dir.x) * s;
                Vector3 local = new Vector3(s * 0.22f, 0.03f, 0.19f) + Vector3.Scale(dir, new Vector3(0.17f, 0.26f, 0.22f));
                DustParticle.Create(head, local, s, true);
            }
        }

        public void LaunchThreat(Vector3 fromDirectionOf)
        {
            var head = _app.Rig.HeadVisual.position;
            Vector3 dir = fromDirectionOf - head;
            dir.y = Mathf.Max(dir.y, dir.magnitude * 0.35f);
            dir.Normalize();
            LoomingThreat.Create(_app.World.Dynamic, head + dir * 90f, -dir * 120f, 5f);
        }
    }
}
