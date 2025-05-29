Shader "Custom/ParticleShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        CGPROGRAM
        #pragma surface ConfigSurface Standard fullforwardshadows
        #pragma target 3.0
        #pragma instancing_options procedural:ConfigProcedural
        
        #if defined (UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<float3> _Positions;
            float particleRadius;
        #endif

        float4 _Color;

        struct Input {
            float3 worldPos;
        };

        // ConfigProcedural execute per vertex
        void ConfigProcedural() {
        #if defined (UNITY_PROCEDURAL_INSTANCING_ENABLED)
            float3 position = _Positions[unity_InstanceID];
            unity_ObjectToWorld = 0.0;
            unity_ObjectToWorld._m03_m13_m23_m33 = float4(position, 1.0);
            unity_ObjectToWorld._m00_m11_m22 = particleRadius * 2;

            if (unity_InstanceID == 2834){
                _Color = float4(1.0, 0, 0, 1.0);
            }

        #endif
        }

        void ConfigSurface(Input input, inout SurfaceOutputStandard surface) {
            surface.Albedo = _Color.rgb;
        }

        ENDCG
    }

    FallBack "Diffuse"
}
