using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// Procedurally synthesized sound: the ~220 Hz flight tone of fruit fly wings, wind in the grass,
    /// birdsong (strongest at dawn) and crickets at night. No audio files are used.
    /// </summary>
    public sealed class FlyAudio : MonoBehaviour
    {
        const int Rate = 44100;
        FlyBrainApp _app;
        AudioSource _buzz, _wind, _crickets, _birds;
        float _birdTimer = 3f;
        static AudioClip _buzzClip, _windClip, _cricketClip, _flapClip;
        static AudioClip[] _birdClips;
        public bool Muted;

        public void Init(FlyBrainApp app)
        {
            _app = app;
            _buzz = AttachBuzz(app.Rig.Body.gameObject, 0.8f);
            _wind = Source2D(gameObject, WindClip(), 0f);
            if (app.World.IsNature)
            {
                _crickets = Source2D(gameObject, CricketClip(), 0f);
                _birds = gameObject.AddComponent<AudioSource>();
                _birds.spatialBlend = 0f;
                _birds.playOnAwake = false;
            }
        }

        public static AudioSource AttachBuzz(GameObject go, float maxVolume)
        {
            var src = go.AddComponent<AudioSource>();
            src.clip = BuzzClip();
            src.loop = true;
            src.volume = 0f;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 25f;
            src.maxDistance = 4000f;
            src.dopplerLevel = 0.3f;
            src.pitch = Random.Range(0.94f, 1.06f);
            src.Play();
            return src;
        }

        static AudioSource Source2D(GameObject go, AudioClip clip, float volume)
        {
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = volume;
            src.spatialBlend = 0f;
            src.Play();
            return src;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            AudioListener.volume = Muted ? 0f : 1f;
            var motor = _app.Motor;
            float target = motor.IsAirborne ? 0.8f : 0f;
            _buzz.volume = Mathf.MoveTowards(_buzz.volume, target, dt * 4f);
            _buzz.pitch = 0.97f + Mathf.Clamp01(motor.Speed / 600f) * 0.08f + (motor.LastTakeoffWasEscape && motor.FlightTime < 1f ? 0.08f : 0f);
            // the brain sets world time: sounds slow down with it
            float ts = Mathf.Clamp(Time.timeScale, 0.3f, 1.5f);

            var wind = WindField.Instance;
            float strength = wind != null ? wind.Strength * wind.Gust / 2000f : 0f;
            float camHeight = _app.Cam != null ? Mathf.Clamp01(_app.Cam.transform.position.y / 400f) : 0f;
            _wind.volume = Mathf.Lerp(_wind.volume, Mathf.Clamp01(0.08f + strength * (0.35f + 0.5f * camHeight)) * 0.5f, dt * 2f);
            _wind.pitch = 0.8f + strength * 0.3f;

            var clock = _app.World.Clock;
            if (clock == null) return;
            float night = 1f - clock.Daylight;
            if (_crickets != null)
            {
                float cricketHours = clock.Hour > 19.5f || clock.Hour < 5.5f ? 1f : 0f;
                _crickets.volume = Mathf.Lerp(_crickets.volume, 0.22f * cricketHours * Mathf.Clamp01(night * 1.5f) * Mathf.Clamp01((clock.Temperature - 13f) / 6f), dt);
                _crickets.pitch = 0.9f + 0.02f * (clock.Temperature - 18f); // crickets chirp faster when warm
            }
            if (_birds != null)
            {
                _birdTimer -= Time.deltaTime;
                float chorus = Mathf.Clamp01(1f - Mathf.Abs(clock.Hour - 6.2f) / 1.5f); // dawn chorus
                if (_birdTimer <= 0 && clock.Daylight > 0.15f)
                {
                    var clips = BirdClips();
                    _birds.pitch = Random.Range(0.85f, 1.15f) * ts;
                    _birds.panStereo = Random.Range(-0.8f, 0.8f);
                    _birds.PlayOneShot(clips[Random.Range(0, clips.Length)], Random.Range(0.05f, 0.18f) * (1f + chorus * 1.5f));
                    _birdTimer = Random.Range(1.5f, 9f) / (1f + chorus * 3f);
                }
            }
        }

        // ------------------------------------------------------------------ synthesis

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Wing beat tone: 220 Hz with harmonics (exact loop).</summary>
        public static AudioClip BuzzClip()
        {
            if (_buzzClip != null) return _buzzClip;
            int n = Rate; // 1 s: integer frequencies loop seamlessly
            var d = new float[n];
            var rng = new System.Random(3);
            float[] amp = { 1f, 0.55f, 0.42f, 0.2f, 0.16f, 0.08f, 0.05f };
            float[] phase = new float[amp.Length];
            for (int k = 0; k < amp.Length; k++) phase[k] = (float)rng.NextDouble() * 6.28f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float v = 0;
                for (int k = 0; k < amp.Length; k++) v += amp[k] * Mathf.Sin(2 * Mathf.PI * 220 * (k + 1) * t + phase[k]);
                // slight flutter at 4 Hz and 11 Hz (integer rates keep the loop seamless)
                v *= 1f + 0.12f * Mathf.Sin(2 * Mathf.PI * 4 * t) + 0.06f * Mathf.Sin(2 * Mathf.PI * 11 * t);
                d[i] = v * 0.22f;
            }
            return _buzzClip = Make("wingbeat", d);
        }

        /// <summary>Soft wind: brown noise with slow swells, crossfaded into a loop.</summary>
        public static AudioClip WindClip()
        {
            if (_windClip != null) return _windClip;
            int n = Rate * 8;
            var raw = new float[n + Rate];
            var rng = new System.Random(7);
            float b = 0, b2 = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2 - 1;
                b = b * 0.985f + white * 0.015f;
                b2 = b2 * 0.9f + b * 0.1f;
                float t = (float)i / Rate;
                float swell = 0.6f + 0.4f * Mathf.Sin(t * 0.8f) * Mathf.Sin(t * 0.33f + 1);
                raw[i] = b2 * swell * 9f;
            }
            var d = new float[n];
            int fade = Rate;
            for (int i = 0; i < n; i++)
            {
                float v = raw[i];
                if (i < fade)
                {
                    float w = (float)i / fade;
                    v = raw[i] * w + raw[n + i] * (1 - w);
                }
                d[i] = Mathf.Clamp(v, -1, 1) * 0.5f;
            }
            return _windClip = Make("wind", d);
        }

        /// <summary>Field crickets: chirps of four pulses at ~4.8 kHz, two individuals.</summary>
        public static AudioClip CricketClip()
        {
            if (_cricketClip != null) return _cricketClip;
            int n = Rate * 4;
            var d = new float[n];
            void Cricket(float carrier, float period, float offset, float gain)
            {
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float local = Mathf.Repeat(t - offset, period);
                    float env = 0;
                    for (int p = 0; p < 4; p++)
                    {
                        float pt = local - p * 0.028f;
                        if (pt > 0 && pt < 0.018f) env = Mathf.Sin(pt / 0.018f * Mathf.PI);
                    }
                    d[i] += Mathf.Sin(2 * Mathf.PI * carrier * t) * env * gain;
                }
            }
            Cricket(4800f, 0.5f, 0f, 0.35f);
            Cricket(4350f, 0.8f, 0.21f, 0.2f);
            return _cricketClip = Make("crickets", d);
        }

        /// <summary>Song-bird phrases: sequences of frequency sweeps and trills.</summary>
        public static AudioClip[] BirdClips()
        {
            if (_birdClips != null) return _birdClips;
            var rng = new System.Random(11);
            _birdClips = new AudioClip[7];
            for (int c = 0; c < _birdClips.Length; c++)
            {
                int notes = rng.Next(3, 9);
                var list = new System.Collections.Generic.List<float>();
                double phase = 0;
                for (int k = 0; k < notes; k++)
                {
                    float dur = 0.05f + (float)rng.NextDouble() * 0.16f;
                    float f0 = 1800f + (float)rng.NextDouble() * 3500f;
                    float f1 = f0 + ((float)rng.NextDouble() - 0.5f) * 2600f;
                    bool trill = rng.NextDouble() < 0.3;
                    int len = (int)(dur * Rate);
                    for (int i = 0; i < len; i++)
                    {
                        float u = (float)i / len;
                        float f = Mathf.Lerp(f0, f1, u) * (trill ? 1f + 0.08f * Mathf.Sin(u * 60f) : 1f);
                        phase += 2 * System.Math.PI * f / Rate;
                        float env = Mathf.Sin(u * Mathf.PI);
                        list.Add((float)System.Math.Sin(phase) * env * env * 0.5f);
                    }
                    int gap = (int)((0.02f + (float)rng.NextDouble() * 0.12f) * Rate);
                    for (int i = 0; i < gap; i++) list.Add(0);
                }
                _birdClips[c] = Make("bird" + c, list.ToArray());
            }
            return _birdClips;
        }

        /// <summary>Wing flaps and air rush of a passing bird.</summary>
        public static AudioClip WingFlaps()
        {
            if (_flapClip != null) return _flapClip;
            int n = Rate * 2;
            var d = new float[n];
            var rng = new System.Random(5);
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float white = (float)rng.NextDouble() * 2 - 1;
                lp = lp * 0.9f + white * 0.1f;
                float flap = Mathf.Pow(Mathf.Max(0, Mathf.Sin(2 * Mathf.PI * 7 * t)), 6);
                d[i] = lp * (0.25f + 1.8f * flap) * 0.9f;
            }
            return _flapClip = Make("flaps", d);
        }
    }
}
