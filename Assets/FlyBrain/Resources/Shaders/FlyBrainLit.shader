Shader "FlyBrain/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _SpecColor2 ("Specular", Color) = (0.2,0.2,0.2,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _RimColor ("Rim Color", Color) = (0,0,0,0)
        _RimPower ("Rim Power", Range(0.5,8)) = 3
        _EmissionColor ("Emission", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows addshadow
        #pragma target 3.5

        sampler2D _MainTex;
        fixed4 _Color, _SpecColor2, _RimColor, _EmissionColor;
        half _Glossiness, _RimPower;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
        };

        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Specular = _SpecColor2.rgb;
            o.Smoothness = _Glossiness * (0.6 + 0.4 * c.a);
            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            o.Emission = _RimColor.rgb * pow(rim, _RimPower) + _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
