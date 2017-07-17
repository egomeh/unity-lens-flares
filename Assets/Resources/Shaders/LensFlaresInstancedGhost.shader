Shader "Hidden/Post FX/Lens Flare - Instanced"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One One
        CGINCLUDE
        #pragma multi_compile __ BLUR_PASS_VERTICAL
        #pragma multi_compile_instancing

        #include "UnityCG.cginc"

        float _AngleToLight;
        float4 _Axis;
        float _Aperture;

        struct AttributesInstancedDraw
        {
            float3 vertex : POSITION;
            float4 uv : TEXCOORD0;
            uint iid : SV_InstanceID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct VaryingsGhostInstancedDraw
        {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        struct GhostData
        {
            // Matrix that maps rays from entrance pupil to aperture
            float4x4 entranceToAperture;

            // Matrix that maps rays from aperture to the sensor plane
            float4x4 apertureToSensor;

            // x: Refractive index of the element that lies before the reflection
            // y: Refractive index of the element that the flare reflection occurs against
            float4 refractiveIncidences;
        };

        StructuredBuffer<GhostData> _GhostDataBuffer;

        VaryingsGhostInstancedDraw VertGhostInstancedDraw(AttributesInstancedDraw v)
        {
            VaryingsGhostInstancedDraw o;

            UNITY_SETUP_INSTANCE_ID(v);

            GhostData currentGhost = _GhostDataBuffer[v.iid];

            float4x4 ma = currentGhost.entranceToAperture;
            float4x4 ms = currentGhost.apertureToSensor;

            float H_e1 = (1. / _Aperture - ma._m01 * _AngleToLight) / ma._m00;
            float H_e2 = (-1. / _Aperture - ma._m01 * _AngleToLight) / ma._m00;

            float4x4 msma = ms * ma;

            float H_p1 = mul(msma, float4(H_e1, _AngleToLight, 0., 0.)).x;
            float H_p2 = mul(msma, float4(H_e2, _AngleToLight, 0., 0.)).x;

            float sensorSize = 43.3;
            H_p1 /= sensorSize / 2.;
            H_p2 /= sensorSize / 2.;

            float center = (H_p1 + H_p2) / 2.;
            float radius = abs(H_p1 - H_p2) / 2.;

            float aspect = _ScreenParams.x / _ScreenParams.y;

            float4x4 ghostScale =
            {
                radius,              0., 0., 0.,
                0.,     radius * aspect, 0., 0.,
                0.,                  0., 1., 0.,
                0.,                  0., 0., 1.
            };

            float2 ghostOffset = _Axis.xy * center;
            ghostOffset.y *= aspect;

            float4x4 ghostTranslation =
            {
                1., 0., 0., ghostOffset.x,
                0., 1., 0., ghostOffset.y,
                0., 0., 1., 0.,
                0., 0., 0., 1.
            };

            float4x4 ghostTransform = mul(ghostScale, ghostTranslation);

            o.vertex = float4(v.vertex, 1.);
            o.vertex = mul(ghostTransform, o.vertex);

            o.texcoord = v.uv;

            return o;
        }

        float4 GhostFragmentInstanced(VaryingsGhostInstancedDraw i) : SV_Target
        {
            return 1.;
        }
        ENDCG

        Pass // 0
        {
            CGPROGRAM
            #pragma vertex VertGhostInstancedDraw
            #pragma fragment GhostFragmentInstanced
            ENDCG
        }
    }
}
