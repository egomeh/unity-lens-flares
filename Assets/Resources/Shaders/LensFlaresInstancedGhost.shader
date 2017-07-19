Shader "Hidden/Post FX/Lens Flare - Instanced"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One One
        CGINCLUDE
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"

        #define M_PI 3.1415926535897932384626433832795
        #define M_PI2 6.28318530718
        #define EPSILON 1.0e-4

        float _AngleToLight;
        float4 _Axis;
        float _Aperture;
        float4 _LightWavelength;
        float4 _LightColor;

        float4x4 _SystemEntranceToAperture;

        sampler2D _ApertureTexture;

        struct AttributesInstancedDraw
        {
            float3 vertex : POSITION;
            float4 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct VaryingsGhostInstancedDraw
        {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
            float3 color : COLOR0;
            float4 debug : TEXCOORD1;
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

        float3 RgbToHsv(float3 c)
        {
            float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
            float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
            float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
            float d = q.x - min(q.w, q.y);
            float e = EPSILON;
            return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
        }

        float3 HsvToRgb(float3 c)
        {
            float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
            float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
            return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
        }

        float Reflectance(float wavelength, float coatingThickness, float angle, float n1, float n2, float n3)
        {
            // Apply Snell's law to get the other angles
            float angle2 = asin(n1 * asin(angle) / n2);
            float angle3 = asin(n1 * asin(angle) / n3);
 
            float cos1 = cos(angle);
            float cos2 = cos(angle2);
            float cos3 = cos(angle3);
 
            float beta = (2. * M_PI) / wavelength * n2 * coatingThickness * cos2;
 
            // Compute the Fresnel terms for the first and second interfaces for both s and p polarized
            // light
            float r12p = (n2 * cos1 - n1 * cos2) / (n2 * cos1 + n1 * cos2);
            float r12p2 = r12p * r12p;
 
            float r23p = (n3 * cos2 - n2 * cos3) / (n3 * cos2 + n2 * cos3);
            float r23p2 = r23p * r23p;
 
            float rp = (r12p2 + r23p2 + 2. * r12p * r23p * cos(2. * beta)) /
                  (1. + r12p2 * r23p2 + 2. * r12p * r23p * cos(2. * beta));
 
            float r12s = (n1 * cos1 - n2 * cos2) / (n1 * cos1 + n2 * cos2);
            float r12s2 = r12s * r12s;
 
            float r23s = (n2 * cos2 - n3 * cos3) / (n2 * cos2 + n3 * cos3);
            float r23s2 = r23s * r23s;
 
            float rs = (r12s2 + r23s2 + 2. * r12s * r23s * cos(2. * beta)) /
                  (1. + r12s2 * r23s2 + 2. * r12s * r23s * cos(2. * beta));
 
            return (rs + rp) * .5;
        }

        VaryingsGhostInstancedDraw VertGhostInstancedDraw(AttributesInstancedDraw v)
        {
            VaryingsGhostInstancedDraw o;

            // Setup instancing specifics
            UNITY_SETUP_INSTANCE_ID(v);

        #if defined(INSTANCING_ON)
            uint instanceID = unity_InstanceID;
        #else
            uint instanceID = 0;
        #endif

            // Get the data for the current ghost from the compute buffer
            GhostData currentGhost = _GhostDataBuffer[instanceID];

            // Extract the matrices that maps rays from entrance through the lens
            float4x4 ma = currentGhost.entranceToAperture;
            float4x4 ms = currentGhost.apertureToSensor;

            float H_e1 = (1. / _Aperture - ma._m01 * _AngleToLight) / ma._m00;
            float H_e2 = (-1. / _Aperture - ma._m01 * _AngleToLight) / ma._m00;

            float4x4 msma = mul(ms, ma);

            float H_p1 = mul(msma, float4(H_e1, _AngleToLight, 0., 0.)).x;
            float H_p2 = mul(msma, float4(H_e2, _AngleToLight, 0., 0.)).x;

            float sensorSize = 43.3;
            H_p1 /= sensorSize / 2.;
            H_p2 /= sensorSize / 2.;

            float center = (H_p1 + H_p2) / 2.;
            float radius = abs(H_p1 - H_p2) / 2.;

            float aspect = _ScreenParams.x / _ScreenParams.y;

            float2 scale = float2(radius, radius * aspect);

            float2 ghostOffset = _Axis.xy * center;
            ghostOffset.y *= aspect;

            // TODO: These could be 3x3, but for now, map to C# 1:1
            matrix ghostScale =
            {
                scale.x,     0., 0., 0.,
                0.,     scale.y, 0., 0.,
                0.,          0., 1., 0.,
                0.,          0., 0., 1.
            };

            matrix ghostTranslation =
            {
                1., 0., 0., ghostOffset.x,
                0., 1., 0., ghostOffset.y,
                0., 0., 1.,            0.,
                0., 0., 0.,            1.
            };

            matrix ghostTransform = mul(ghostTranslation, ghostScale);

            // Why is this line needed? UNITY_MATRIX_M should be identity!?!?!?
            // RenderDoc also says that it's identity???
            o.vertex = mul(UNITY_MATRIX_M, float4(v.vertex, 1.));

            o.vertex = mul(ghostTransform, o.vertex);

            o.texcoord = v.uv;

            float n1 = currentGhost.refractiveIncidences.x;
            float n2 = currentGhost.refractiveIncidences.y;

            // TODO: Figure out what exactly this means.
            float d = _LightWavelength.w / 4.0 / n1;

            // TODO: this is very likely to be wrong, sad!
            float angle = max(min(.4, _AngleToLight), .0);

            // TODO: Make the shader version of reflectance code vector based instead of 3 calls
            float3 reflectedRGB = 0.;
            reflectedRGB.r = Reflectance(_LightWavelength.r, d, angle, n1, max(sqrt(n1 * n2), 1.38), n2);
            reflectedRGB.g = Reflectance(_LightWavelength.g, d, angle, n1, max(sqrt(n1 * n2), 1.38), n2);
            reflectedRGB.b = Reflectance(_LightWavelength.b, d, angle, n1, max(sqrt(n1 * n2), 1.38), n2);

            // normalize the color in order to keep RGB -> HSV conversion stable
            reflectedRGB = normalize(reflectedRGB);

            float3 flareColor = reflectedRGB * _LightColor.rgb;

            float3 hsv = RgbToHsv(flareColor);

            o.debug = float4(hsv, 1.);

            hsv.z = .05;

            o.color = HsvToRgb(hsv);

            return o;
        }

        float4 GhostFragmentInstanced(VaryingsGhostInstancedDraw i) : SV_Target
        {
            float aperture = tex2D(_ApertureTexture, i.texcoord).r;
            return aperture * float4(i.color, 1.);
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
