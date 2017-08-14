#include "UnityCG.cginc"

#define HALF_MAX 65504.0

#define M_PI 3.1415926535897932384626433832795
#define M_PI2 6.28318530718

sampler2D _MainTex;
float4 _MainTex_TexelSize;
float4 _MainTex_ST;

float4 _Threshold;
float _SampleScale;

sampler2D _BloomTex;
float4 _Bloom_Settings;

float4 _FlareOffsetAndScale;
float4x4 _FlareTransform;

float _AngleToLight;
float4 _Axis;
float _Aperture;

struct AttributesDefault
{
    float3 vertex : POSITION;
    float4 uv : TEXCOORD0;
};

struct VaryingsDefault
{
    float4 vertex : SV_POSITION;
    float2 texcoord : TEXCOORD0;
};

VaryingsDefault VertDefault(AttributesDefault v)
{
    VaryingsDefault o;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.texcoord = v.uv;
    return o;
}

VaryingsDefault VertDefaultBlit(AttributesDefault v)
{
    VaryingsDefault o;

    o.vertex = float4(v.vertex, 1.);

    o.vertex = mul(_FlareTransform, o.vertex);

    o.vertex.y *= -_ProjectionParams.x;

    o.texcoord = v.uv;

    return o;
}

float smin(float a, float b, float k)
{
    float diff = b - a;
    float h = saturate(0.5 + 0.5 * diff / k);
    return b - h * (diff + k * (1.0f - h));
}

float smax(float a, float b, float k)
{
    float diff = a - b;
    float h = saturate(0.5 + 0.5 * diff / k);
    return b + h * (diff + k * (1.0f - h));
}

float PolygonShape(float2 coord, int edges, float smoothing)
{
    float distance = 0.;
    for (int i = 0; i < edges; ++i)
    {
        float angle = M_PI2 * (float)i / (float)edges;
        float2 axis = float2(cos(angle), sin(angle));

        distance = smax(distance, dot(axis, coord), smoothing);
    }

    float circle = saturate(length(coord));

    return lerp(distance, circle, smoothing);
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
