using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// Scripted smoke test (player command line: -flybrainAutotest &lt;folder&gt; [-flybrainLab] [-flybrainShots]):
    /// lets the fly live, provokes its needs and the classic reflexes, logs neuron rates, physiology and behavior,
    /// takes screenshots and quits.
    /// </summary>
    public sealed class FlyAutotest : MonoBehaviour
    {
        FlyBrainApp _app;
        string _dir;
        StreamWriter _log;
        float _start;

        public void Init(FlyBrainApp app, string dir)
        {
            _app = app;
            _dir = string.IsNullOrEmpty(dir) ? Path.Combine(Application.persistentDataPath, "autotest") : dir;
            Directory.CreateDirectory(_dir);
            _log = new StreamWriter(Path.Combine(_dir, "autotest.log")) { AutoFlush = true };
            _start = Time.realtimeSinceStartup;
            if (FlyBrainApp.CommandLineValue("-flybrainShots") != null) StartCoroutine(Shots());
            else if (!_app.World.IsNature) StartCoroutine(Lab());
            else StartCoroutine(Nature());
        }

        IEnumerator WaitBrain()
        {
            Log($"start: {SystemInfo.processorType}, {SystemInfo.processorCount} threads, {SystemInfo.graphicsDeviceName}, world={_app.World.DisplayName}");
            while (!_app.Brain.IsReady)
            {
                if (_app.Brain.Error != null || Time.realtimeSinceStartup - _start > 120)
                {
                    Log("brain failed: " + _app.Brain.Status);
                    Quit();
                    yield break;
                }
                yield return null;
            }
            Log($"brain ready after {Time.realtimeSinceStartup - _start:F1} s");
            _app.Hud.ShowHelp = false;
        }

        // ------------------------------------------------------------------ visual check of the environment

        IEnumerator Shots()
        {
            _app.Hud.ShowHelp = false;
            _app.Hud.Visible = false;
            _app.BrainView.Visible = false;
            var clock = _app.World.Clock;
            yield return Wait(2f);
            foreach (var (hour, tag) in new[] { (9.5f, "morning"), (13f, "noon"), (19.2f, "evening"), (23f, "night"), (5.8f, "dawn") })
            {
                if (clock != null) clock.Hour = hour;
                _app.Cam.Distance = 14f; _app.Cam.Pitch = 18f; _app.Cam.Yaw = 150f;
                yield return Wait(1.2f);
                Shot($"env_{tag}_close");
                _app.Cam.Distance = 140f; _app.Cam.Pitch = 22f;
                yield return Wait(1.2f);
                Shot($"env_{tag}_mid");
                _app.Cam.Distance = 900f; _app.Cam.Pitch = 16f;
                yield return Wait(1.5f);
                Shot($"env_{tag}_wide");
                Status(tag);
            }
            // the other inhabitants
            if (_app.Wildlife != null && clock != null)
            {
                clock.Hour = 10f;
                var cam = _app.Cam;
                var shots = new System.Collections.Generic.List<(Transform target, string name, float dist)>();
                if (_app.Wildlife.Flies.Count > 0) shots.Add((_app.Wildlife.Flies[0].Rig.Body, "wild_fly_male", 9f));
                if (_app.Wildlife.Flies.Count > 1) shots.Add((_app.Wildlife.Flies[1].Rig.Body, "wild_fly", 9f));
                shots.Add((_app.Wildlife.Spider.transform, "spider", 18f));
                if (_app.Wildlife.Ants.Count > 0) shots.Add((_app.Wildlife.Ants[0].transform, "ant", 12f));
                foreach (var (target, name, dist) in shots)
                {
                    cam.Target = target;
                    cam.Distance = dist;
                    cam.Pitch = 25f;
                    cam.Yaw = target.eulerAngles.y + 120f;
                    yield return Wait(1.5f);
                    Shot("life_" + name);
                }
                cam.Target = _app.Rig.Body;
                _app.Wildlife.SwoopBird();
                cam.Distance = 250f;
                cam.Pitch = 10f;
                yield return Wait(1.3f);
                Shot("life_bird");
            }
            Quit();
        }

        // ------------------------------------------------------------------ life in the orchard

        IEnumerator Nature()
        {
            yield return WaitBrain();
            var n = _app.Nervous;
            var clock = _app.World.Clock;
            var cam = _app.Cam;
            clock.Hour = 8.5f;
            _app.Hud.ShowNeurons = true;

            // 1. free life: whatever the fly chooses to do
            for (int i = 0; i < 24; i++)
            {
                yield return Wait(0.5f);
                Status("free");
                if (i == 4) Shot("01_free_close");
                if (i == 10) { cam.Distance = 60f; }
                if (i == 14) Shot("01b_free_mid");
            }
            cam.Distance = 14f;

            // 2. hunger: the fly should find fermenting fruit by smell and the brain should make it feed
            n.Physiology.Energy = 0.2f;
            n.Physiology.Crop = 0f;
            bool fed = false;
            for (int i = 0; i < 180 && !fed; i++)
            {
                yield return Wait(0.5f);
                Status("hungry");
                if (i == 20) Shot("02a_foraging");
                if (_app.Motor.IsAirborne && i % 12 == 0) Shot($"02b_forage_flight_{i}");
                fed = n.Drive.Feed > 0.3f && _app.Motor.Mode == FlyMode.Feed;
            }
            Log(fed ? "FEEDING reached" : "feeding NOT reached");
            if (fed)
            {
                SideView(7f);
                yield return Wait(1.5f);
                Shot("02c_feeding");
                for (int i = 0; i < 30 && n.Drive.Feed > 0.1f; i++)
                {
                    yield return Wait(0.5f);
                    Status("meal");
                }
                Status("after meal");
            }

            // 3. dust on the eyes -> eye bristles -> aDN -> grooming
            _app.Tools.SprinkleDust(0, 8);
            SideView(5f);
            for (int i = 0; i < 14; i++)
            {
                yield return Wait(0.5f);
                Status($"dust({DustParticle.CountOn(_app.Rig.HeadVisual, -1) + DustParticle.CountOn(_app.Rig.HeadVisual, 1)})");
                if (i == 3) Shot("03_dust_grooming");
            }

            // 4. a jumping spider hunts the fly -> looming -> giant fiber -> escape flight -> landing
            yield return WaitUntilGrounded(10f);
            _app.Wildlife.SendSpider();
            OverView(40f);
            int takeoffs = _app.Motor.Takeoffs;
            for (int i = 0; i < 80 && _app.Motor.Takeoffs == takeoffs; i++)
            {
                yield return Wait(0.25f);
                if (i % 4 == 0) Status($"spider {_app.Wildlife.Spider.StateName}");
            }
            Log(_app.Motor.Takeoffs > takeoffs ? $"ESCAPE from spider (escape={_app.Motor.LastTakeoffWasEscape})" : "no escape from spider");
            for (int i = 0; i < 16; i++)
            {
                yield return Wait(0.2f);
                Status("escape flight");
                if (i == 3) Shot("04a_escape_flight");
            }
            yield return WaitUntilGrounded(15f);
            Status("landed");
            cam.Distance = 14f;
            yield return Wait(1f);
            Shot("04b_after_landing");

            // 5. a bird swoops over
            yield return Wait(3f);
            _app.Wildlife.SwoopBird();
            OverView(60f);
            for (int i = 0; i < 14; i++)
            {
                yield return Wait(0.25f);
                Status("bird");
                if (i == 5) Shot("05_bird");
            }
            yield return WaitUntilGrounded(15f);

            // 6. thirst: find water (dew, puddle) and drink
            n.Physiology.Water = 0.1f;
            bool drank = false;
            cam.Distance = 20f;
            for (int i = 0; i < 160 && !drank; i++)
            {
                yield return Wait(0.5f);
                Status("thirsty");
                drank = n.Mind.Current == Activity.Drink;
            }
            Log(drank ? "DRINKING reached" : "drinking NOT reached");
            if (drank)
            {
                SideView(7f);
                yield return Wait(1f);
                Shot("06_drinking");
            }

            // 7. dusk and night: roost and sleep
            n.Physiology.Water = 0.9f;
            n.Physiology.Energy = 0.8f;
            clock.Hour = 20.6f;
            cam.Distance = 30f;
            bool slept = false;
            for (int i = 0; i < 120 && !slept; i++)
            {
                yield return Wait(0.5f);
                Status("dusk");
                if (i == 6) Shot("07a_dusk");
                slept = n.Physiology.Asleep;
            }
            Log(slept ? "SLEEP reached" : "sleep NOT reached");
            SideView(8f);
            yield return Wait(2f);
            Shot("07b_night");

            // 8. morning: dew
            clock.Hour = 5.6f;
            for (int i = 0; i < 20; i++)
            {
                yield return Wait(0.5f);
                Status("dawn");
            }
            Shot("08_dawn");

            // 9. brain regression: LC16 optogenetics -> MDN -> backward walking
            _app.Hud.ApplyPreset("LC16 → MDN");
            for (int i = 0; i < 5; i++)
            {
                yield return Wait(0.5f);
                Status("LC16 opto");
            }
            _app.Hud.ClearManipulations();
            Log($"done: takeoffs={_app.Motor.Takeoffs}, landings={_app.Motor.Landings}, sugar={n.Physiology.SugarIntake:F2}, water={n.Physiology.WaterIntake:F2}");
            Quit();
        }

        IEnumerator WaitUntilGrounded(float max)
        {
            float t = 0;
            while ((_app.Motor.IsAirborne || _app.Motor.IsSettling) && t < max)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        // ------------------------------------------------------------------ laboratory regression (classic experiments)

        IEnumerator Lab()
        {
            yield return WaitBrain();
            _app.Nervous.Autonomy = false;
            yield return Wait(2);
            Shot("lab_01_overview");
            var root = _app.Rig.Root;
            _app.World.ClearDynamic();
            _app.World.SpawnFood(Taste.Sugar, root.position + root.forward * 1.0f, _app.Motor.SurfaceUp, 1.8f);
            _app.Nervous.Physiology.Energy = 0.3f;
            for (int i = 0; i < 8; i++)
            {
                yield return Wait(0.5f);
                Status("sugar");
            }
            SideView(7f);
            yield return Wait(0.6f);
            Shot("lab_02_feeding");
            _app.World.ClearDynamic();
            yield return Wait(2f);
            for (int k = 0; k < 5; k++)
            {
                _app.Tools.PuffAt(_app.Rig.HeadVisual.position + root.forward * 20f + Vector3.up * 5f);
                yield return Wait(0.5f);
                Status("puff");
            }
            Shot("lab_03_puff");
            _app.Tools.LaunchThreat(_app.Cam.transform.position);
            for (int i = 0; i < 10; i++)
            {
                yield return Wait(0.12f);
                Status("loom");
                if (i == 7) Shot("lab_04_escape");
            }
            yield return WaitUntilGrounded(10f);
            _app.Hud.ApplyPreset("LC16 → MDN");
            for (int i = 0; i < 5; i++)
            {
                yield return Wait(0.5f);
                Status("LC16 opto");
            }
            _app.Hud.ClearManipulations();
            Log($"lab done: takeoffs={_app.Motor.Takeoffs}");
            Quit();
        }

        // ------------------------------------------------------------------ helpers

        IEnumerator Wait(float seconds)
        {
            // world time, which follows the brain
            float t = 0;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        void SideView(float distance)
        {
            var cam = _app.Cam;
            cam.Distance = distance;
            cam.Yaw = _app.Rig.Root.eulerAngles.y - 70f;
            cam.Pitch = 14f;
        }

        void OverView(float distance)
        {
            var cam = _app.Cam;
            cam.Distance = distance;
            cam.Yaw = _app.Rig.Root.eulerAngles.y + 150f;
            cam.Pitch = 30f;
        }

        void Status(string tag)
        {
            var n = _app.Nervous;
            var b = _app.Brain;
            var s = n.Senses;
            var p = n.Physiology;
            var m = _app.Motor;
            var ci = CultureInfo.InvariantCulture;
            var clock = _app.World.Clock;
            string pos = m.Rig.Root.position.ToString("F0");
            if (!n.IsWired)
            {
                Log($"[{tag}] (brain not wired) {n.BehaviorName}");
                return;
            }
            Log(string.Format(ci,
                "[{0}] t={1} brain={2:F1}s cap={3:F1}x ts={4:F2} spk/s={5:F0} | {6} / {7} ({8}{9}) | phys E={10:F2} crop={11:F2} W={12:F2} H={13:F2} T={14:F2} sleepP={15:F2} fear={16:F2} asleep={17} | in: sugar={18:F0} water={19:F0} jo={20:F0}/{21:F0} eye={22:F0}/{23:F0} loom={24:F0}/{25:F0} lc16={26:F0} obj={27:F0}/{28:F0} odor={29:F2}/{30:F2} hum={31:F2} ego={32:F1} | out: MN9={33:F1} aDN={34:F1} GF={35:F0}/{36:F0} MDN={37:F1} DNa02={38:F1}/{39:F1} land={40:F0} | pos={41} up={42} air={43} v={44:F0} mode={45}",
                tag, clock != null ? clock.TimeText : "-", b.BrainTimeMs / 1000, b.Capacity, Time.timeScale, b.SpikesPerSecond,
                n.BehaviorName, FlyMind.Name(n.Mind.Current), n.ReasonFromBrain ? "BRAIN: " : "", n.Reason,
                p.Energy, p.Crop, p.Water, p.Hunger, p.Thirst, p.SleepPressure, p.Fear, p.Asleep,
                s.Sugar, s.Water, s.JoL, s.JoR, s.EyeBristleL, s.EyeBristleR, s.LoomL, s.LoomR, s.FrontalLoom, s.ObjectL, s.ObjectR, s.OdorL, s.OdorR, Mathf.Max(s.HumidityL, s.HumidityR), s.EgoLoom,
                n.Mn9.Rate, (n.ADN1.Rate + n.ADN2.Rate) * 0.5f, n.GfL.FastRate, n.GfR.FastRate, n.Mdn.Rate, n.Dna02L.Rate, n.Dna02R.Rate, n.LandingDN.FastRate,
                pos, m.SurfaceUp.ToString("F2"), m.IsAirborne, m.Speed, m.Mode));
        }

        void Shot(string name)
        {
            string path = Path.Combine(_dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log("screenshot " + path);
        }

        void Log(string line)
        {
            string text = $"{Time.realtimeSinceStartup - _start,7:F2} {line}";
            _log?.WriteLine(text);
            Debug.Log("[FlyAutotest] " + text);
        }

        void Quit()
        {
            StartCoroutine(QuitSoon());
        }

        IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(1f);
            _log?.Dispose();
            _log = null;
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
