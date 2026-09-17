Shader "FlyBrain/Wing"
{
    // Thin membrane: double sided, vein texture in alpha, thin-film iridescence at grazing angles
    Properties
    {
        _MainTex ("Veins (A = membrane)", 2D) = "white" {}
        _Color ("Tint", Color) = (0.85,0.88,0.9,0.25)
        _VeinColor ("Vein Color", Color) = (0.25,0.2,0.15,0.9)
        _Iridescence ("Iridescence", Range(0,2)) = 0.8
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            fixed4 _Color, _VeinColor;
            half _Iridescence;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(WorldSpaceViewDir(v.vertex));
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.normal) * (facing > 0 ? 1 : -1);
                half ndv = saturate(dot(n, i.viewDir));
                fixed4 tex = tex2D(_MainTex, i.uv);
                // thin-film interference approximated by a view-dependent rainbow
                half phase = (1.0 - ndv) * 6.0 + i.uv.x * 2.0;
                fixed3 film = 0.5 + 0.5 * cos(6.2831 * (phase + fixed3(0.0, 0.33, 0.67)));
                half light = 0.55 + 0.45 * abs(dot(n, _WorldSpaceLightPos0.xyz));
                fixed3 col = lerp(_Color.rgb, film, _Iridescence * (1.0 - ndv) * 0.5) * light;
                half vein = 1.0 - tex.a;
                col = lerp(col, _VeinColor.rgb * light, vein);
                half alpha = lerp(_Color.a + (1.0 - ndv) * 0.25, _VeinColor.a, vein);
                return fixed4(col, alpha * tex.r);
            }
            ENDCG
        }
    }
}
