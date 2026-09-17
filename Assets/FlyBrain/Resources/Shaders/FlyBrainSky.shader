Shader "FlyBrain/Sky"
{
    // Procedural sky: day/dusk/night gradients, sun disc and glow, moon, stars and drifting clouds
    Properties
    {
        _SunDir ("Sun Direction", Vector) = (0,0.5,-0.8,0)
        _MoonDir ("Moon Direction", Vector) = (0,-0.5,0.8,0)
        _SunColor ("Sun Color", Color) = (1,0.95,0.85,1)
        _Day ("Daylight", Range(0,1)) = 1
        _Dusk ("Dusk", Range(0,1)) = 0
        _CloudOffset ("Cloud Offset", Vector) = (0,0,0,0)
        _CloudCover ("Cloud Cover", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _SunDir, _MoonDir, _CloudOffset;
            fixed4 _SunColor;
            half _Day, _Dusk, _CloudCover;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    v += a * vnoise(p);
                    p = p * 2.03 + float2(17.1, 3.7);
                    a *= 0.5;
                }
                return v;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float3 sunDir = normalize(_SunDir.xyz);
                float3 moonDir = normalize(_MoonDir.xyz);

                float3 dayZenith = float3(0.16, 0.34, 0.72);
                float3 dayHorizon = float3(0.7, 0.78, 0.86);
                float3 duskZenith = float3(0.2, 0.22, 0.42);
                float3 duskHorizon = float3(0.95, 0.6, 0.38);
                float3 nightZenith = float3(0.004, 0.007, 0.018);
                float3 nightHorizon = float3(0.03, 0.04, 0.07);

                float h = saturate(up);
                float3 zenith = lerp(nightZenith, dayZenith, _Day);
                float3 horizon = lerp(nightHorizon, dayHorizon, _Day);
                // twilight colors concentrate towards the sun
                float towardsSun = saturate(dot(normalize(float3(d.x, 0, d.z) + 1e-4), normalize(float3(sunDir.x, 0, sunDir.z) + 1e-4)) * 0.5 + 0.5);
                float duskAmt = _Dusk * lerp(0.35, 1.0, towardsSun) * max(_Day, 0.25);
                zenith = lerp(zenith, duskZenith, duskAmt * 0.5);
                horizon = lerp(horizon, duskHorizon, duskAmt * 0.75);
                float3 col = lerp(horizon, zenith, pow(h, 0.45));
                if (up < 0) col = horizon * lerp(1.0, 0.7, saturate(-up * 3));

                // sun
                float sd = dot(d, sunDir);
                float sunVis = saturate(sunDir.y * 10 + 1);
                col += _SunColor.rgb * (pow(saturate(sd), 6) * 0.25 + pow(saturate(sd), 64) * 0.6) * sunVis * max(_Day, _Dusk);
                col += _SunColor.rgb * smoothstep(0.99955, 0.99975, sd) * 20 * sunVis;

                // stars and moon
                float night = saturate(1 - _Day * 1.4);
                if (night > 0.001 && up > 0)
                {
                    float3 cell = floor(d * 220);
                    float star = hash31(cell);
                    float bright = smoothstep(0.9965, 1.0, star);
                    float twinkle = 0.6 + 0.4 * sin(_Time.y * (2 + star * 5) + star * 100);
                    col += bright * twinkle * night * saturate(up * 4) * float3(0.9, 0.95, 1.0) * 1.5;
                    float md = dot(d, moonDir);
                    col += smoothstep(0.9993, 0.9996, md) * night * float3(0.9, 0.92, 1.0) * 1.2;
                    col += pow(saturate(md), 200) * night * float3(0.15, 0.18, 0.25);
                }

                // clouds on a virtual plane
                if (up > 0.01)
                {
                    float2 uv = d.xz / (up + 0.12) * 0.9 + _CloudOffset.xy;
                    float c = fbm(uv * 1.6);
                    float cover = smoothstep(1.0 - _CloudCover, 1.05 - _CloudCover * 0.4, c);
                    float3 lit = lerp(float3(0.06, 0.07, 0.1), lerp(float3(1, 0.98, 0.95), _SunColor.rgb * 1.1, _Dusk * 0.7), max(_Day, _Dusk * 0.6));
                    float shade = fbm(uv * 1.6 + sunDir.xz * 0.08);
                    lit *= lerp(1.0, 0.72, saturate((shade - c) * 4 + 0.3));
                    col = lerp(col, lit, cover * saturate(up * 6) * 0.9);
                }
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
