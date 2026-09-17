Shader "FlyBrain/Transparent"
{
    // Glossy transparent material for liquid drops and glass
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.4)
        _SpecColor2 ("Specular", Color) = (0.6,0.6,0.6,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.95
        _FresnelColor ("Fresnel", Color) = (1,1,1,0.6)
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 200

        CGPROGRAM
        #pragma surface surf StandardSpecular alpha:fade
        #pragma target 3.5

        fixed4 _Color, _SpecColor2, _FresnelColor;
        half _Glossiness, _FresnelPower;

        struct Input { float3 viewDir; };

        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            half f = pow(1.0 - saturate(dot(normalize(IN.viewDir), o.Normal)), _FresnelPower);
            o.Albedo = _Color.rgb;
            o.Specular = _SpecColor2.rgb;
            o.Smoothness = _Glossiness;
            o.Emission = _FresnelColor.rgb * f * _FresnelColor.a;
            o.Alpha = saturate(_Color.a + f * _FresnelColor.a);
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
