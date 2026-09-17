Shader "FlyBrain/Nature"
{
    // Soil, bark, fruit, stones, leaves: albedo texture x vertex color, world-space detail texture,
    // vertex alpha = wetness (smoothness), optional two-sided translucency for leaves
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _DetailTex ("Detail (world space)", 2D) = "grey" {}
        _DetailScale ("Detail Scale (1/mm)", Float) = 0.05
        _DetailStrength ("Detail Strength", Range(0,1)) = 0.5
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
        _SpecColor2 ("Specular", Color) = (0.06,0.06,0.06,1)
        _Translucency ("Translucency", Range(0,1)) = 0
        _TwoSided ("Two Sided (0/2)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        Cull [_TwoSided]

        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows addshadow vertex:vert
        #pragma target 3.5

        sampler2D _MainTex, _DetailTex;
        fixed4 _Color, _SpecColor2;
        half _Glossiness, _DetailScale, _DetailStrength, _Translucency;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
            float3 wNormal;
            float facing : VFACE;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wNormal = UnityObjectToWorldNormal(v.normal);
        }

        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            c.rgb *= IN.color.rgb;
            // triplanar detail so tiny grains are visible at fly scale
            float3 n = abs(normalize(IN.wNormal));
            n = n / (n.x + n.y + n.z);
            float3 p = IN.worldPos * _DetailScale;
            half dx = tex2D(_DetailTex, p.zy).r;
            half dy = tex2D(_DetailTex, p.xz).r;
            half dz = tex2D(_DetailTex, p.xy).r;
            half detail = dx * n.x + dy * n.y + dz * n.z;
            c.rgb *= lerp(1.0, detail * 2.0, _DetailStrength);
            if (IN.facing < 0) o.Normal = float3(0, 0, -1);
            o.Albedo = c.rgb;
            o.Specular = _SpecColor2.rgb * (0.4 + IN.color.a);
            o.Smoothness = saturate(_Glossiness * (0.3 + IN.color.a) + IN.color.a * 0.35) * (0.75 + 0.25 * c.a);
            o.Emission = c.rgb * _Translucency * 0.15 * unity_AmbientSky.rgb * 4;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
