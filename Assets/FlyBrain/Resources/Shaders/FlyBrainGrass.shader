Shader "FlyBrain/Grass"
{
    // Baked grass blades: uv.y = 0 at the root .. 1 at the tip, uv2.xyz = root position (sway phase),
    // vertex color = blade tint. Swayed by the global wind (_FlyWind: dir.xz, strength (m/s), gust).
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Sway ("Sway (mm per m/s)", Float) = 14
        _Glossiness ("Smoothness", Range(0,1)) = 0.35
        _Translucency ("Translucency", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf StandardSpecular vertex:vert addshadow
        #pragma target 3.5

        fixed4 _Color;
        half _Sway, _Glossiness, _Translucency;
        float4 _FlyWind;

        struct Input
        {
            float4 color : COLOR;
            float tip;
            float facing : VFACE;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float h = v.texcoord.y;
            float3 root = v.texcoord1.xyz;
            float height = max(v.texcoord.x, 1.0);
            float2 dir = _FlyWind.xy;
            float strength = _FlyWind.z;
            float t = _Time.y;
            float phase = dot(root.xz, float2(0.0031, 0.0027));
            float wave = sin(t * 1.3 - dot(root.xz, dir) * 0.004 + phase * 3) * 0.5 + 0.5;
            float flutter = sin(t * 7.1 + phase * 40) * 0.08;
            float bend = (strength * (0.35 + 0.65 * wave * _FlyWind.w) + flutter) * _Sway * h * h * (height / 100.0);
            v.vertex.xz += dir * bend;
            v.vertex.y -= abs(bend) * h * 0.25;
            o.tip = h;
        }

        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            fixed3 c = IN.color.rgb * _Color.rgb;
            c *= lerp(0.45, 1.08, saturate(IN.tip * 1.4));
            if (IN.facing < 0) o.Normal = float3(0, 0, -1);
            o.Albedo = c;
            o.Specular = fixed3(0.05, 0.06, 0.04);
            o.Smoothness = _Glossiness;
            o.Emission = c * _Translucency * 0.2 * unity_AmbientSky.rgb * 3 * IN.tip;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
