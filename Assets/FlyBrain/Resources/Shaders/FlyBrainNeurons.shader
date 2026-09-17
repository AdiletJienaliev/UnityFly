Shader "FlyBrain/Neurons"
{
    // Every neuron of the connectome as a screen-space disc; activity drives brightness and size.
    Properties
    {
        _PointSize ("Point Size (px)", Float) = 2.5
        _BaseBrightness ("Base Brightness", Float) = 0.035
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            StructuredBuffer<float4> _Neurons;   // xyz position, w = palette index
            StructuredBuffer<float> _Activity;   // 0..1
            StructuredBuffer<float4> _Palette;
            float4x4 _BrainMatrix;
            float _PointSize, _BaseBrightness;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            static const float2 kCorners[6] = { float2(-1,-1), float2(1,-1), float2(1,1), float2(-1,-1), float2(1,1), float2(-1,1) };

            v2f vert (uint id : SV_VertexID)
            {
                uint index = id / 6;
                float2 corner = kCorners[id % 6];
                float4 n = _Neurons[index];
                float a = _Activity[index];
                float4 world = mul(_BrainMatrix, float4(n.xyz, 1));
                float4 clip = mul(UNITY_MATRIX_VP, world);
                float size = _PointSize * (1.0 + 1.5 * a);
                clip.xy += corner * size / _ScreenParams.xy * clip.w;
                v2f o;
                o.pos = clip;
                o.uv = corner;
                fixed4 baseCol = _Palette[(uint)n.w];
                fixed3 hot = lerp(baseCol.rgb, fixed3(1.0, 0.95, 0.7), 0.6);
                o.color = fixed4(baseCol.rgb * _BaseBrightness + hot * a * 1.2, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half r = dot(i.uv, i.uv);
                clip(1.0 - r);
                return i.color * (1.0 - r * 0.6);
            }
            ENDCG
        }
    }
}
