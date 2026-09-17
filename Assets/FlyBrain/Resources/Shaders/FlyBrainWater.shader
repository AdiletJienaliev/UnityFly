Shader "FlyBrain/Water"
{
    // Still puddle: dark transparent water with sky reflection (fresnel) and wind ripples
    Properties
    {
        _Color ("Deep Color", Color) = (0.06,0.055,0.04,0.82)
        _ReflectColor ("Reflection", Color) = (0.75,0.8,0.85,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.9
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
        LOD 200

        CGPROGRAM
        #pragma surface surf StandardSpecular alpha:fade vertex:vert
        #pragma target 3.5

        fixed4 _Color, _ReflectColor;
        half _Glossiness;
        float4 _FlyWind;

        struct Input
        {
            float3 viewDir;
            float3 worldPos;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
        }

        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            float t = _Time.y;
            float2 p = IN.worldPos.xz;
            float amp = 0.012 + 0.03 * saturate(_FlyWind.z * _FlyWind.w);
            float2 n = float2(sin(p.x * 0.35 + t * 2.1 + sin(p.y * 0.2)) + sin(p.y * 0.5 - t * 1.7),
                              cos(p.y * 0.33 + t * 1.9) + sin(p.x * 0.45 + p.y * 0.2 - t * 2.3)) * amp;
            o.Normal = normalize(float3(n.x, n.y, 1));
            half f = pow(1.0 - saturate(dot(normalize(IN.viewDir), o.Normal)), 4);
            o.Albedo = _Color.rgb;
            o.Specular = fixed3(0.02, 0.02, 0.02) + f * 0.06;
            o.Smoothness = _Glossiness;
            o.Emission = _ReflectColor.rgb * unity_AmbientSky.rgb * (0.03 + f * 0.22);
            o.Alpha = saturate(_Color.a + f * 0.1);
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
