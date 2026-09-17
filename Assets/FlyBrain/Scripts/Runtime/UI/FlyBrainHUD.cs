using System;
using System.Collections.Generic;
using System.Linq;
using FlyBrain.Brain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyBrain
{
    /// <summary>Immediate-mode interface: brain status, sensory and motor neuron activity, tools and optogenetics.</summary>
    public sealed class FlyBrainHUD : MonoBehaviour
    {
        public bool Visible = true;
        public bool ShowOpto = false;
        public bool ShowNeurons = false;
        public bool ShowHelp = true;

        FlyBrainApp _app;
        readonly List<Rect> _panels = new List<Rect>();
        float _scale = 1f;
        GUIStyle _box, _title, _label, _small, _bold, _button, _buttonOn, _field, _big, _warn, _brainTag, _vncTag;
        Texture2D _white;
        bool _stylesReady;

        // optogenetics
        sealed class Manipulation
        {
            public string Label;
            public int[] Indices;
            public float Rate;
            public bool Silence;
        }
        readonly List<Manipulation> _manips = new List<Manipulation>();
        readonly HashSet<int> _optoTouched = new HashSet<int>();
        string _search = "";
        string _lastSearch = null;
        List<KeyValuePair<string, int>> _allTypes;
        List<KeyValuePair<string, int>> _matches = new List<KeyValuePair<string, int>>();
        string _selectedType;
        int _selectedSide; // 0 both, 1 left, 2 right
        float _optoRate = 120f;
        Vector2 _optoScroll;
        public bool IsTyping { get; private set; }

        public void Init(FlyBrainApp app) => _app = app;

        public bool IsOverUI(Vector2 screenPos)
        {
            if (!Visible) return false;
            var gui = new Vector2(screenPos.x, Screen.height - screenPos.y) / _scale;
            foreach (var r in _panels) if (r.Contains(gui)) return true;
            return false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || IsTyping) return;
            if (kb.hKey.wasPressedThisFrame) Visible = !Visible;
            if (kb.bKey.wasPressedThisFrame) _app.BrainView.Visible = !_app.BrainView.Visible;
            if (kb.tabKey.wasPressedThisFrame) ShowOpto = !ShowOpto;
            if (kb.f1Key.wasPressedThisFrame) ShowHelp = !ShowHelp;
            if (kb.rKey.wasPressedThisFrame) _app.Brain.ResetActivity();
            if (kb.wKey.wasPressedThisFrame) _app.Nervous.Autonomy = !_app.Nervous.Autonomy;
            if (kb.nKey.wasPressedThisFrame) ShowNeurons = !ShowNeurons;
            if (kb.mKey.wasPressedThisFrame && _app.Audio != null) _app.Audio.Muted = !_app.Audio.Muted;
            if (kb.spaceKey.wasPressedThisFrame) TogglePause();
            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) _app.Brain.TargetTimeScale = Mathf.Min(2f, _app.Brain.TargetTimeScale * 1.5f);
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) _app.Brain.TargetTimeScale = Mathf.Max(0.05f, _app.Brain.TargetTimeScale / 1.5f);
            var clock = _app.World.Clock;
            if (clock != null)
            {
                if (kb.rightBracketKey.wasPressedThisFrame) clock.DayLengthSeconds = Mathf.Max(180f, clock.DayLengthSeconds / 2f);
                if (kb.leftBracketKey.wasPressedThisFrame) clock.DayLengthSeconds = Mathf.Min(86400f, clock.DayLengthSeconds * 2f);
                if (kb.kKey.wasPressedThisFrame) SkipHour(clock);
            }
            if (Time.realtimeSinceStartup > 30f && ShowHelp && _helpAutoClosed == false) { ShowHelp = false; _helpAutoClosed = true; }
        }
        bool _helpAutoClosed;

        void SkipHour(DayCycle clock)
        {
            clock.Hour += 1f;
            if (clock.Hour >= 24f)
            {
                clock.Hour -= 24f;
                clock.Day++;
            }
            // the body lives through the skipped hour too
            var bio = 3600f / Mathf.Max(1f, clock.BioTimeScale);
            _app.Nervous.Physiology.Tick(bio, clock.BioTimeScale, clock.Temperature, false, 0f);
        }

        void TogglePause()
        {
            var b = _app.Brain;
            b.Paused = !b.Paused;
            Time.timeScale = b.Paused ? 0f : Mathf.Max(0.05f, b.TargetTimeScale);
        }

        // ------------------------------------------------------------------ styles

        void Styles()
        {
            if (_stylesReady) return;
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.78f));
            bg.Apply();
            _box = new GUIStyle(GUI.skin.box) { normal = { background = bg }, padding = new RectOffset(12, 12, 10, 10) };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.45f) } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.9f, 0.92f, 0.95f) }, wordWrap = true };
            _small = new GUIStyle(_label) { fontSize = 12, normal = { textColor = new Color(0.7f, 0.74f, 0.8f) } };
            _bold = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
            _big = new GUIStyle(_label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 1f, 0.7f) } };
            _warn = new GUIStyle(_small) { normal = { textColor = new Color(1f, 0.6f, 0.4f) } };
            _brainTag = new GUIStyle(_small) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.45f, 1f, 0.6f) } };
            _vncTag = new GUIStyle(_small) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.75f, 0.35f) } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 13, wordWrap = false };
            _buttonOn = new GUIStyle(_button) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
            _stylesReady = true;
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (_app == null) return;
            Styles();
            _panels.Clear();
            _scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1));
            float w = Screen.width / _scale, h = Screen.height / _scale;
            IsTyping = GUI.GetNameOfFocusedControl() == "search";

            if (!Visible)
            {
                GUI.Label(new Rect(10, h - 26, 500, 22), "H — показать интерфейс · C — кинокамера", _small);
                return;
            }

            DrawStatus(new Rect(10, 10, 400, 250));
            DrawOrganism(new Rect(10, 268, 400, 300));
            if (ShowNeurons) DrawActivity(new Rect(10, 576, 400, h - 576 - 70));
            else GUI.Label(new Rect(14, 574, 400, 20), "N — активность сенсорных и нисходящих нейронов", _small);
            if (ShowOpto) DrawOpto(new Rect(w - 410, 10, 400, h * 0.54f));
            DrawTools(new Rect(420, h - 60, (_app.BrainView.Visible ? w * _app.BrainView.Viewport.x : w - 10) - 430, 50));
            if (_app.BrainView.Visible) DrawBrainViewFrame(w, h);
            if (ShowHelp) DrawHelp(new Rect(w * 0.5f - 320, 60, 640, 500));
        }

        Rect Panel(Rect r)
        {
            _panels.Add(r);
            GUI.Box(r, GUIContent.none, _box);
            return new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16);
        }

        void DrawStatus(Rect rect)
        {
            var r = Panel(rect);
            var b = _app.Brain;
            var n = _app.Nervous;
            GUILayout.BeginArea(r);
            GUILayout.Label("Мозг дрозофилы · FlyWire 783 · LIF", _title);
            var clock = _app.World.Clock;
            string where = _app.World.DisplayName + (clock != null ? $" · день {clock.Day}, {clock.TimeText} ({clock.PhaseName}, {clock.Temperature:F0}°C)" : "");
            GUILayout.Label(where, _small);
            if (!b.IsReady)
            {
                GUILayout.Label(b.Status, b.Error != null ? _warn : _label);
            }
            else
            {
                var brain = b.Brain;
                GUILayout.Label($"{brain.N:N0} нейронов · {b.SpikesPerSecond:N0} спайков/с · активных {brain.ActiveNeurons:N0} · мощность ×{b.Capacity:F1} · мир ×{Time.timeScale:F2}", _small);
            }
            GUILayout.Space(2);
            GUILayout.Label(n.BehaviorName, _big);
            GUILayout.BeginHorizontal();
            GUILayout.Label(n.ReasonFromBrain ? "МОЗГ:" : "ВНЕ МОДЕЛИ:", n.ReasonFromBrain ? _brainTag : _vncTag, GUILayout.Width(n.ReasonFromBrain ? 42 : 86));
            GUILayout.Label(n.Reason, _small);
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(n.Autonomy ? "Свобода: вкл (W)" : "Свобода: выкл (W)", n.Autonomy ? _buttonOn : _button)) n.Autonomy = !n.Autonomy;
            if (GUILayout.Button(b.Paused ? "Пуск (Space)" : "Пауза (Space)", _button)) TogglePause();
            if (GUILayout.Button("Сброс мозга (R)", _button)) b.ResetActivity();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_app.World.IsNature ? "Среда: сад → лаборатория" : "Среда: лаборатория → сад", _button)) _app.SwitchEnvironment(_app.World.IsNature);
            if (clock != null)
            {
                if (GUILayout.Button("−", _button, GUILayout.Width(24))) clock.DayLengthSeconds = Mathf.Min(86400f, clock.DayLengthSeconds * 2f);
                GUILayout.Label($"сутки = {FormatDuration(clock.DayLengthSeconds)}", _small, GUILayout.Width(110));
                if (GUILayout.Button("+", _button, GUILayout.Width(24))) clock.DayLengthSeconds = Mathf.Max(180f, clock.DayLengthSeconds / 2f);
                if (GUILayout.Button("+1 ч (K)", _button)) SkipHour(clock);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static string FormatDuration(float seconds) => seconds >= 3600f ? $"{seconds / 3600f:0.#} ч" : $"{seconds / 60f:0.#} мин";

        void DrawOrganism(Rect rect)
        {
            var r = Panel(rect);
            var n = _app.Nervous;
            var p = n.Physiology;
            var mind = n.Mind;
            GUILayout.BeginArea(r);
            GUILayout.Label("Организм и мотивация (вне коннектома)", _title);
            StateRow("Энергия (запасы)", p.Energy, new Color(1f, 0.75f, 0.25f), $"{p.Energy:P0}");
            StateRow("Зоб (недавняя еда)", p.Crop, new Color(0.95f, 0.55f, 0.2f), $"{p.Crop:P0}");
            StateRow("Вода", p.Water, new Color(0.35f, 0.7f, 1f), $"{p.Water:P0}");
            StateRow("Голод → чувствит. к сахару", p.Hunger, new Color(1f, 0.45f, 0.3f), $"×{p.SugarGain:F2}");
            float hour = _app.World.Clock != null ? _app.World.Clock.Hour : 12f;
            StateRow("Сонливость (давление + ритм)", p.SleepDrive(hour), new Color(0.55f, 0.55f, 1f), p.Asleep ? "спит" : $"{p.SleepDrive(hour):P0}");
            StateRow("Страх", p.Fear, new Color(1f, 0.3f, 0.35f), $"{p.Fear:P0}");
            GUILayout.Space(2);
            string plan = n.Autonomy ? $"Намерение: {FlyMind.Name(mind.Current).ToLowerInvariant()}{(mind.Detail.Length > 0 ? " — " + mind.Detail : "")}" : "Свобода выключена: только рефлексы мозга";
            GUILayout.Label(plan, _small);
            var s = n.Senses;
            GUILayout.Label($"Запах брожения L/R {s.OdorL:F2}/{s.OdorR:F2} · влажность {Mathf.Max(s.HumidityL, s.HumidityR):F2} · ветер {s.WindSpeed:F0} мм/с · свет {s.Light:P0}", _small);
            GUILayout.Label($"Съедено {p.SugarIntake:F2} · выпито {p.WaterIntake:F2} · полётов {_app.Motor.Takeoffs} · яиц отложено {p.EggsLaid}{(mind.FoodMemory.HasValue ? " · помнит, где ела" : "")}", _small);
            GUILayout.FlexibleSpace();
            DrawEthogram(GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true)), n.Ethogram);
            GUILayout.EndArea();
        }

        void StateRow(string label, float value, Color color, string text)
        {
            var row = GUILayoutUtility.GetRect(10, 18, GUILayout.ExpandWidth(true));
            float labelW = row.width * 0.5f;
            GUI.Label(new Rect(row.x, row.y, labelW, row.height), label, _small);
            float x = row.x + labelW + 4, bw = row.width - labelW - 62;
            DrawBar(new Rect(x, row.y + 4, bw, 10), value, color);
            GUI.Label(new Rect(x + bw + 4, row.y, 60, row.height), text, _small);
        }

        void DrawEthogram(Rect r, Ethogram e)
        {
            float now = Time.time;
            float span = e.Window;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.06f);
            GUI.DrawTexture(new Rect(r.x, r.y + 12, r.width, 14), _white);
            foreach (var seg in e.Segments)
            {
                float x0 = r.x + r.width * Mathf.Clamp01(1 - (now - seg.Start) / span);
                float x1 = r.x + r.width * Mathf.Clamp01(1 - (now - seg.End) / span);
                if (x1 - x0 < 0.5f) x1 = x0 + 0.5f;
                GUI.color = seg.Color;
                GUI.DrawTexture(new Rect(x0, r.y + 12, x1 - x0, 14), _white);
            }
            GUI.color = old;
            GUI.Label(new Rect(r.x, r.y - 4, r.width, 16), $"Этограмма: последние {span / 60f:F0} мин (ходьба, еда, питьё, чистка, полёт, отдых, сон)", _small);
        }

        void DrawActivity(Rect rect)
        {
            var r = Panel(rect);
            var n = _app.Nervous;
            GUILayout.BeginArea(r);
            GUILayout.Label("Сенсоры → мозг (Гц на нейрон)", _title);
            if (!n.IsWired)
            {
                GUILayout.Label("ожидание мозга…", _small);
                GUILayout.EndArea();
                return;
            }
            var sens = new Color(0.35f, 0.85f, 1f);
            Bar(n.Sugar, sens, 160);
            Bar(n.Water, sens, 160);
            Bar(n.Bitter, sens, 160);
            BarPair("JO антенн (ветер)", n.JoL, n.JoR, sens, 150);
            BarPair("Щетинки глаз", n.EyeBristleL, n.EyeBristleR, sens, 160);
            BarPair("LC4/LPLC2 (надвигание)", n.LoomL, n.LoomR, sens, 160);
            Bar(n.Lc16, sens, 160);
            BarPair("LC10a (объект)", n.ObjectL, n.ObjectR, sens, 110);

            GUILayout.Space(4);
            GUILayout.Label("Мозг → тело (нисходящие и моторные)", _title);
            var mot = new Color(1f, 0.6f, 0.25f);
            Bar(n.Mn9, mot, 60);
            Bar(n.IngestionMN, mot, 60);
            BarPair("aDN1 / aDN2 (груминг)", n.ADN1, n.ADN2, mot, 40);
            BarPair("Giant Fiber L / R", n.GfL, n.GfR, mot, 120, fast: true);
            Bar(n.AlarmDN, mot, 120);
            Bar(n.Mdn, mot, 60);
            BarPair("DNa02 L / R (поворот)", n.Dna02L, n.Dna02R, mot, 80);
            BarPair("DNa04 L / R (полёт)", n.Dna04L, n.Dna04R, mot, 80);
            Bar(n.LandingDN, mot, 120);
            BarPair("P9 (DNp09) L / R", n.P9L, n.P9R, mot, 80);
            GUILayout.EndArea();
        }

        void Bar(NeuronPopulation p, Color color, float max)
        {
            if (p == null) return;
            BarRow(p.Label + $" ({p.Indices.Length})", p.Rate, -1, color, max);
        }

        void BarPair(string label, NeuronPopulation a, NeuronPopulation b, Color color, float max, bool fast = false)
        {
            if (a == null || b == null) return;
            BarRow(label, fast ? a.FastRate : a.Rate, fast ? b.FastRate : b.Rate, color, max);
        }

        void BarRow(string label, float v1, float v2, Color color, float max)
        {
            var row = GUILayoutUtility.GetRect(10, 18, GUILayout.ExpandWidth(true));
            float labelW = row.width * 0.52f;
            GUI.Label(new Rect(row.x, row.y, labelW, row.height), label, _small);
            float x = row.x + labelW + 4, bw = row.width - labelW - 60;
            if (v2 < 0)
                DrawBar(new Rect(x, row.y + 4, bw, 11), v1 / max, color);
            else
            {
                DrawBar(new Rect(x, row.y + 2, bw, 7), v1 / max, color);
                DrawBar(new Rect(x, row.y + 11, bw, 7), v2 / max, color * 0.85f);
            }
            string txt = v2 < 0 ? $"{v1:F0}" : $"{v1:F0}/{v2:F0}";
            GUI.Label(new Rect(x + bw + 4, row.y, 60, row.height), txt, _small);
        }

        void DrawBar(Rect r, float t, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.08f);
            GUI.DrawTexture(r, _white);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), _white);
            GUI.color = old;
        }

        void DrawTools(Rect rect)
        {
            var r = Panel(rect);
            var tools = _app.Tools;
            GUILayout.BeginArea(r);
            GUILayout.BeginHorizontal();
            foreach (var (tool, key, label) in InteractionTools.Catalog)
            {
                if ((tool == Tool.Spider || tool == Tool.Bird) && _app.Wildlife == null) continue;
                if (GUILayout.Button($"{key} {label}", tools.Current == tool ? _buttonOn : _button, GUILayout.Height(30))) tools.Select(tool);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            if (tools.Current != Tool.None)
                GUI.Label(new Rect(rect.x, rect.y - 22, rect.width, 20), "ЛКМ — применить «" + InteractionTools.Catalog.First(c => c.tool == tools.Current).label + "», Esc — отмена", _small);
        }

        void DrawBrainViewFrame(float w, float h)
        {
            var v = _app.BrainView.Viewport;
            var r = new Rect(v.x * w, (1 - v.y - v.height) * h, v.width * w, v.height * h);
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width, 20), "Активность всех нейронов (B)", _small);
            float y = r.yMax - 18;
            string[] names = { "", "оптич.", "центр.", "сенсор.", "зрит. проекц.", "восход.", "нисход.", "", "", "мотор." };
            float x = r.x + 8;
            foreach (int k in new[] { 1, 2, 3, 4, 6, 9 })
            {
                var old = GUI.color;
                GUI.color = BrainActivityView.Palette[k];
                GUI.DrawTexture(new Rect(x, y + 5, 8, 8), _white);
                GUI.color = old;
                GUI.Label(new Rect(x + 10, y, 90, 18), names[k], _small);
                x += 18 + names[k].Length * 6.5f;
            }
        }

        void DrawHelp(Rect rect)
        {
            var r = Panel(rect);
            GUILayout.BeginArea(r);
            GUILayout.Label("Что здесь происходит", _title);
            GUILayout.Label("Муха живёт в саду под яблоней среди опавших бродящих фруктов. Её рефлексы — модель всего мозга Drosophila (philshiu/Drosophila_brain_model): 138 639 нейронов и 15 млн связей коннектома FlyWire, leaky integrate-and-fire в реальном времени.", _label);
            GUILayout.Label("Мозг решает: есть ли (сахарные GRN → MN9), чиститься ли (пыль, ветер → aDN), спасаться ли (надвигание → Giant Fiber), пятиться, замирать, поворачивать к движущимся мухам и муравьям, выпускать ноги перед посадкой.", _label);
            GUILayout.Label("Чего в коннектоме нет, делает тело: голод, жажда, сон и суточный ритм (они меняют чувствительность вкусовых нейронов), поиск еды по запаху против ветра, поиск воды, память мест, ходьба по любым поверхностям и полёт. Такие решения помечены «ВНЕ МОДЕЛИ».", _label);
            GUILayout.Label("Муха свободна (W выключает свободу). Вокруг живут другие мухи, муравьи, паук-скакун; иногда пролетает дрозд. Роса утром, жара днём, сон ночью.", _label);
            GUILayout.Label("Инструменты 1–9: капли, воздух, пыль, угроза, точка, паук, птица. Камера: ПКМ — вращение, колесо — зум, СКМ — сдвиг, F — следовать, C — кинокамера. [ ] — скорость суток, K — +1 час, N — нейроны, B — вид мозга, Tab — оптогенетика, M — звук, Space — пауза, R — сброс мозга, Del — убрать объекты, H — скрыть UI.", _small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Понятно (F1)", _button, GUILayout.Height(28))) ShowHelp = false;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ optogenetics

        void DrawOpto(Rect rect)
        {
            var r = Panel(rect);
            var b = _app.Brain;
            GUILayout.BeginArea(r);
            GUILayout.Label("Оптогенетика (Tab)", _title);
            if (!b.IsReady)
            {
                GUILayout.Label("ожидание мозга…", _small);
                GUILayout.EndArea();
                return;
            }
            _optoScroll = GUILayout.BeginScrollView(_optoScroll);

            GUILayout.Label("Опыты из статьи и связи мозг → поведение:", _small);
            var presets = Presets();
            for (int i = 0; i < presets.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + 2, presets.Count); k++)
                    if (GUILayout.Button(presets[k].label, _button, GUILayout.Width((r.width - 30) / 2))) presets[k].apply();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("Любой тип клеток FlyWire:", _small);
            GUI.SetNextControlName("search");
            _search = GUILayout.TextField(_search, _field);
            if (_search != _lastSearch) UpdateMatches();
            foreach (var m in _matches)
                if (GUILayout.Button($"{m.Key}  ({m.Value})", m.Key == _selectedType ? _buttonOn : _button)) _selectedType = m.Key;

            if (_selectedType != null)
            {
                GUILayout.BeginHorizontal();
                string[] sides = { "Обе стороны", "Левая", "Правая" };
                for (int s = 0; s < 3; s++)
                    if (GUILayout.Button(sides[s], _selectedSide == s ? _buttonOn : _button)) _selectedSide = s;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{_optoRate:F0} Гц", _label, GUILayout.Width(60));
                _optoRate = GUILayout.HorizontalSlider(_optoRate, 5f, 250f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                var side = _selectedSide == 1 ? Side.Left : _selectedSide == 2 ? Side.Right : Side.Unknown;
                string sideLabel = _selectedSide == 1 ? " L" : _selectedSide == 2 ? " R" : "";
                if (GUILayout.Button("Активировать", _button))
                    Add(new Manipulation { Label = $"{_selectedType}{sideLabel} {_optoRate:F0} Гц", Indices = b.Catalog.FindByType(side, _selectedType), Rate = _optoRate });
                if (GUILayout.Button("Заглушить", _button))
                    Add(new Manipulation { Label = $"{_selectedType}{sideLabel} заглушён", Indices = b.Catalog.FindByType(side, _selectedType), Silence = true });
                GUILayout.EndHorizontal();
            }

            if (_manips.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("Активные манипуляции:", _small);
                for (int i = 0; i < _manips.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{_manips[i].Label} · {_manips[i].Indices.Length} нейр.", _label);
                    if (GUILayout.Button("x", _button, GUILayout.Width(28)))
                    {
                        _manips.RemoveAt(i);
                        RebuildOpto();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Снять все", _button))
                {
                    _manips.Clear();
                    RebuildOpto();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void UpdateMatches()
        {
            _lastSearch = _search;
            _allTypes ??= _app.Brain.Catalog.AllCellTypes();
            var q = _search.Trim();
            _matches = q.Length == 0
                ? new List<KeyValuePair<string, int>>()
                : _allTypes.Where(t => t.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                           .OrderBy(t => t.Key.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(t => t.Key.Length)
                           .Take(10).ToList();
        }

        List<(string label, Action apply)> Presets()
        {
            var c = _app.Brain.Catalog;
            var L = Side.Left;
            var A = Side.Unknown;
            Action Act(string label, Func<int[]> idx, float hz) => () => Add(new Manipulation { Label = label, Indices = idx(), Rate = hz });
            return new List<(string, Action)>
            {
                ("Сахар GRN → хоботок", Act("Сахар GRN 100 Гц", () => c.Group("paper_sugar_GRN_a").Concat(c.Group("paper_sugar_GRN_b")).ToArray(), 100)),
                ("Горечь + сахар", () => { Act("Сахар GRN 100 Гц", () => c.Group("paper_sugar_GRN_a").Concat(c.Group("paper_sugar_GRN_b")).ToArray(), 100)(); Act("Горькое GRN 150 Гц", () => c.FindByType(A, "LB1a,LB1d", "LB1b", "LB1c"), 150)(); }),
                ("JON антенн → груминг", Act("JON 150 Гц", () => c.Group("paper_JON_CE").Concat(c.Group("paper_JON_F")).Concat(c.Group("paper_JON_D_m")).ToArray(), 150)),
                ("aDN1+aDN2 → груминг", Act("aDN1+aDN2 100 Гц", () => c.FindByType(A, "DNg62", "DNge078"), 100)),
                ("LC4 левый глаз → взлёт", Act("LC4 L 120 Гц", () => c.FindByType(L, "LC4"), 120)),
                ("Giant Fiber → взлёт", Act("DNp01 150 Гц", () => c.FindByType(A, "DNp01"), 150)),
                ("LC16 → MDN → назад", Act("LC16 150 Гц", () => c.FindByType(A, "LC16"), 150)),
                ("MDN → назад", Act("MDN 100 Гц", () => c.FindByType(A, "MDN"), 100)),
                ("DNa02 левый → поворот", Act("DNa02 L 120 Гц", () => c.FindByType(L, "DNa02"), 120)),
                ("LC10a левый → к объекту", Act("LC10a L 80 Гц", () => c.FindByType(L, "LC10a"), 80)),
                ("P9 (DNp09) → вперёд", Act("DNp09 100 Гц", () => c.FindByType(A, "DNp09"), 100)),
                ("Заглушить MN9", () => Add(new Manipulation { Label = "MN9 заглушён", Indices = c.FindByType(A, "CB0701"), Silence = true })),
                ("(!) ORN DM1 10 Гц", Act("ORN DM1 10 Гц (эпилептиформная активность модели)", () => c.FindByType(A, "ORN_DM1"), 10)),
            };
        }

        void Add(Manipulation m)
        {
            if (m.Indices == null || m.Indices.Length == 0) return;
            _manips.Add(m);
            RebuildOpto();
        }

        void RebuildOpto()
        {
            var b = _app.Brain;
            b.SetOptoRate(_optoTouched, 0);
            b.SetOptoSilenced(_optoTouched, false);
            _optoTouched.Clear();
            var rates = new Dictionary<int, float>();
            foreach (var m in _manips)
                foreach (var i in m.Indices)
                {
                    _optoTouched.Add(i);
                    if (m.Silence) continue;
                    rates[i] = Mathf.Max(rates.TryGetValue(i, out var r) ? r : 0, m.Rate);
                }
            foreach (var kv in rates) b.SetOptoRate(new[] { kv.Key }, kv.Value);
            b.SetOptoSilenced(_manips.Where(m => m.Silence).SelectMany(m => m.Indices), true);
        }

        /// <summary>Used by the automated test: apply a preset by (partial) label.</summary>
        public bool ApplyPreset(string labelContains)
        {
            var p = Presets().FirstOrDefault(x => x.label.Contains(labelContains));
            if (p.apply == null) return false;
            p.apply();
            return true;
        }

        public void ClearManipulations()
        {
            _manips.Clear();
            RebuildOpto();
        }

        public static string ModeName(FlyMode m) => m switch
        {
            FlyMode.Walk => "Ходьба",
            FlyMode.Feed => "Питание: хоботок выдвинут",
            FlyMode.Groom => "Груминг антенн",
            FlyMode.Freeze => "Тревога: замирание",
            FlyMode.Backward => "Отступает назад",
            FlyMode.Flight => "Полёт",
            FlyMode.Rest => "Отдых",
            FlyMode.Sleep => "Сон",
            FlyMode.Drink => "Пьёт",
            _ => m.ToString(),
        };
    }
}
