using System;

namespace FlyBrain
{
    /// <summary>Thread-safe 2D improved Perlin noise (Ken Perlin 2002), output roughly in [0, 1] like Mathf.PerlinNoise.</summary>
    public static class Perlin
    {
        static readonly int[] P = BuildPermutation();

        static int[] BuildPermutation()
        {
            var rng = new Random(1337);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }
            var perm = new int[512];
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
            return perm;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        static float Grad(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        public static float Noise(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255;
            yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = P[P[xi] + yi], ab = P[P[xi] + yi + 1], ba = P[P[xi + 1] + yi], bb = P[P[xi + 1] + yi + 1];
            float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
            float n = Lerp(x1, x2, v);
            return 0.5f + n * 0.72f;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
