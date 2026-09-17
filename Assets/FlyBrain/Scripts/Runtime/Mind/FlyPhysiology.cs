using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// Internal state of the fly's body on a compressed biological clock: energy reserves, crop (the storage
    /// stomach filled during a meal), water balance, sleep pressure and acute fear. Hunger and thirst act on the
    /// brain the way neuromodulation does in real flies, by changing the gain of gustatory receptor neurons
    /// (starved flies respond to far less sugar; sated flies stop responding) — the connectome itself has no such state.
    /// </summary>
    public sealed class FlyPhysiology
    {
        public float Energy = 0.55f;
        public float Crop = 0.0f;
        public float Water = 0.62f;
        public float SleepPressure = 0.15f;
        public float Fear;
        public bool Asleep;
        public float SleepDepth;

        /// <summary>Mature eggs ready to be laid (0..1); yeast in fermenting fruit provides the protein.</summary>
        public float Eggs = 0.45f;
        public int EggsLaid;
        public float SugarIntake, WaterIntake;   // totals (crop units)
        public float BioHoursAwake, BioHoursAsleep;

        /// <summary>0 sated .. 1 starving. A full crop suppresses hunger long before reserves rise.</summary>
        public float Hunger => Mathf.Clamp01((0.9f - Energy) / 0.75f - Crop * 1.3f);
        /// <summary>0 hydrated .. 1 dehydrated.</summary>
        public float Thirst => Mathf.Clamp01((0.85f - Water) / 0.7f - Crop * 0.3f);
        public bool Weak => Energy < 0.03f || Water < 0.015f;

        /// <summary>Sugar GRN gain (neuromodulation by hunger): sated ~0.2, hungry ~1.</summary>
        public float SugarGain => Mathf.Clamp(0.18f + 0.9f * Mathf.Pow(Hunger, 0.75f), 0.18f, 1.05f);
        public float WaterGain => Mathf.Clamp(0.25f + 0.85f * Thirst, 0.25f, 1.05f);

        /// <summary>Circadian sleep drive: high at night, a siesta after midday, low at dawn and dusk (crepuscular activity).</summary>
        public static float Circadian(float hour)
        {
            float night = Band(hour, 20.2f, 21.3f, 4.8f, 6.0f);
            float siesta = 0.45f * Band(hour, 11.5f, 12.5f, 14.5f, 15.5f);
            return Mathf.Max(night, siesta);
        }

        /// <summary>1 inside [b, c], ramps over [a, b] and [c, d]; handles wrapping around midnight.</summary>
        static float Band(float h, float a, float b, float c, float d)
        {
            if (a > c)
            {
                // wraps midnight: evening ramp a..b, full until c (next morning), ramp down c..d
                if (h >= b || h <= c) return 1f;
                if (h > a && h < b) return (h - a) / (b - a);
                if (h > c && h < d) return 1f - (h - c) / (d - c);
                return 0f;
            }
            if (h >= b && h <= c) return 1f;
            if (h > a && h < b) return (h - a) / (b - a);
            if (h > c && h < d) return 1f - (h - c) / (d - c);
            return 0f;
        }

        /// <summary>Activity peaks at dawn and dusk.</summary>
        public static float ActivityPeak(float hour) => Mathf.Max(Band(hour, 5.5f, 6.5f, 8.5f, 10f), Band(hour, 16.5f, 17.5f, 19.5f, 20.5f));

        public float SleepDrive(float hour) => Mathf.Clamp01(0.55f * SleepPressure + 0.75f * Circadian(hour) - 0.1f);

        /// <param name="dt">world seconds</param>
        /// <param name="bioScale">biological seconds per world second</param>
        /// <param name="walking">walking speed mm/s</param>
        public void Tick(float dt, float bioScale, float temperature, bool flying, float walking)
        {
            float h = dt * bioScale / 3600f;
            float temp = Mathf.Clamp(0.7f + 0.035f * (temperature - 18f), 0.5f, 1.3f);
            float activity = (flying ? 5f : 0f) + Mathf.Clamp01(walking / 15f) * 0.6f;

            // the crop empties into the gut; sugar becomes reserves, the water of the meal is absorbed
            float emptied = Crop * (1f - Mathf.Exp(-h * 1.1f));
            Crop -= emptied;
            Energy = Mathf.Clamp01(Energy + emptied * 0.75f);
            Water = Mathf.Clamp01(Water + emptied * 0.25f);

            Energy = Mathf.Clamp01(Energy - h * 0.021f * temp * (1f + activity));
            if (Energy > 0.35f) Eggs = Mathf.Clamp01(Eggs + h * 0.03f);
            Water = Mathf.Clamp01(Water - h * 0.04f * temp * (1f + activity * 0.6f));

            if (Asleep)
            {
                SleepPressure = Mathf.Max(0, SleepPressure - h / 6.5f);
                BioHoursAsleep += h;
                SleepDepth = Mathf.Clamp01(SleepDepth + dt * 0.02f);
            }
            else
            {
                SleepPressure = Mathf.Clamp01(SleepPressure + h / 15f * (1f + activity * 0.3f));
                BioHoursAwake += h;
                SleepDepth = 0;
            }
            Fear = Mathf.Max(0, Fear * Mathf.Exp(-dt / 10f) - dt * 0.002f);
        }

        /// <summary>A sip of liquid through the proboscis (world seconds of pumping at full rate).</summary>
        public void Ingest(Taste taste, float concentration, float amount)
        {
            if (taste == Taste.Sugar)
            {
                float room = Mathf.Clamp01(1f - Crop);
                float a = amount * room;
                Crop = Mathf.Clamp01(Crop + a);
                SugarIntake += a;
                Water = Mathf.Clamp01(Water + a * 0.15f);
                Eggs = Mathf.Clamp01(Eggs + a * 0.4f);
            }
            else if (taste == Taste.Water)
            {
                Water = Mathf.Clamp01(Water + amount * 1.6f);
                Crop = Mathf.Clamp01(Crop + amount * 0.15f);
                WaterIntake += amount;
            }
        }

        public void Startle(float amount) => Fear = Mathf.Clamp01(Mathf.Max(Fear, amount));
    }
}
