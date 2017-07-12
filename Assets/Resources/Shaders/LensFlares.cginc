#include "UnityCG.cginc"

#define HALF_MAX        65504.0

sampler2D _MainTex;
float4 _MainTex_TexelSize;
float4 _MainTex_ST;

float4 _Threshold;
float _SampleScale;

sampler2D _BloomTex;
float4 _Bloom_Settings;

float4 _FlareOffsetAndScale;
float4x4 _FlareTransform;

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

    // o.vertex.x *= _FlareOffsetAndScale.w;
    // o.vertex.xy = o.vertex.xy * _FlareOffsetAndScale.z;
    // o.vertex.xy += _FlareOffsetAndScale.xy;

    o.texcoord = v.uv;

    return o;
}

half4 EncodeHDR(float3 rgb)
{
#if USE_RGBM
    rgb *= 1.0 / 8.0;
    float m = max(max(rgb.r, rgb.g), max(rgb.b, 1e-6));
    m = ceil(m * 255.0) / 255.0;
    return half4(rgb / m, m);
#else
    return half4(rgb, 0.0);
#endif
}

float3 DecodeHDR(half4 rgba)
{
#if USE_RGBM
    return rgba.rgb * rgba.a * 8.0;
#else
    return rgba.rgb;
#endif
}

float Max3(float a, float b, float c)
{
    return max(max(a, b), c);
}

//
// Quadratic color thresholding
// curve = (threshold - knee, knee * 2, 0.25 / knee)
//
half3 QuadraticThreshold(half3 color, half threshold, half3 curve)
{
    // Pixel brightness
    half br = Max3(color.r, color.g, color.b);

    // Under-threshold part: quadratic curve
    half rq = clamp(br - curve.x, 0.0, curve.y);
    rq = curve.z * rq * rq;

    // Combine and apply the brightness response curve.
    color *= max(rq, br - threshold) / max(br, 1e-5);

    return color;
}

// Better, temporally stable box filtering
// [Jimenez14] http://goo.gl/eomGso
// . . . . . . .
// . A . B . C .
// . . D . E . .
// . F . G . H .
// . . I . J . .
// . K . L . M .
// . . . . . . .
half3 DownsampleBox13Tap(sampler2D tex, float2 uv, float2 texelSize)
{
    half3 A = tex2D(tex, uv + texelSize * float2(-1.0, -1.0)).rgb;
    half3 B = tex2D(tex, uv + texelSize * float2(0.0, -1.0)).rgb;
    half3 C = tex2D(tex, uv + texelSize * float2(1.0, -1.0)).rgb;
    half3 D = tex2D(tex, uv + texelSize * float2(-0.5, -0.5)).rgb;
    half3 E = tex2D(tex, uv + texelSize * float2(0.5, -0.5)).rgb;
    half3 F = tex2D(tex, uv + texelSize * float2(-1.0, 0.0)).rgb;
    half3 G = tex2D(tex, uv).rgb;
    half3 H = tex2D(tex, uv + texelSize * float2(1.0, 0.0)).rgb;
    half3 I = tex2D(tex, uv + texelSize * float2(-0.5, 0.5)).rgb;
    half3 J = tex2D(tex, uv + texelSize * float2(0.5, 0.5)).rgb;
    half3 K = tex2D(tex, uv + texelSize * float2(-1.0, 1.0)).rgb;
    half3 L = tex2D(tex, uv + texelSize * float2(0.0, 1.0)).rgb;
    half3 M = tex2D(tex, uv + texelSize * float2(1.0, 1.0)).rgb;

    half2 div = (1.0 / 4.0) * half2(0.5, 0.125);

    half3 o = (D + E + I + J) * div.x;
    o += (A + B + G + F) * div.y;
    o += (B + C + H + G) * div.y;
    o += (F + G + L + K) * div.y;
    o += (G + H + M + L) * div.y;

    return o;
}

// 9-tap bilinear upsampler (tent filter)
half3 UpsampleTent(sampler2D tex, float2 uv, float2 texelSize, float sampleScale)
{
    float4 d = texelSize.xyxy * float4(1.0, 1.0, -1.0, 0.0) * sampleScale;

    half3 s;
    s = tex2D(tex, uv - d.xy).rgb;
    s += tex2D(tex, uv - d.wy).rgb * 2.0;
    s += tex2D(tex, uv - d.zy).rgb;

    s += tex2D(tex, uv + d.zw).rgb * 2.0;
    s += tex2D(tex, uv).rgb * 4.0;
    s += tex2D(tex, uv + d.xw).rgb * 2.0;

    s += tex2D(tex, uv + d.zy).rgb;
    s += tex2D(tex, uv + d.wy).rgb * 2.0;
    s += tex2D(tex, uv + d.xy).rgb;

    return s * (1.0 / 16.0);
}

half3 UpsampleTentH(sampler2D tex, float2 uv, float2 texelSize, float sampleScale)
{
    float2 d = texelSize * float2(1.0, 0.) * sampleScale * _Bloom_Settings.z;

    half3 s;
    s = tex2D(tex, uv - d).rgb;
    s = max(s, tex2D(tex, uv).rgb);
    s = max(s, tex2D(tex, uv + d).rgb);

    return s;
}

half4 Combine(half3 bloom, float2 uv)
{
    half3 color = tex2D(_BloomTex, uv).rgb;
    return half4(bloom + color, 1.0);
}

// Clamp HDR value within a safe range
half3 SafeHDR(half3 c)
{
    return min(c, HALF_MAX);
}

half4 SafeHDR(half4 c)
{
    return min(c, HALF_MAX);
}

half4 Prefilter(half3 color, float2 uv)
{
    color = QuadraticThreshold(color, _Threshold.x, _Threshold.yzw);
    return half4(color, 1.0);
}

half4 FragPrefilter13(VaryingsDefault i) : SV_Target
{
    half3 color = DownsampleBox13Tap(_MainTex, i.texcoord, _MainTex_TexelSize.xy);
    return Prefilter(SafeHDR(color), i.texcoord);
}

half4 FragDownsample13(VaryingsDefault i) : SV_Target
{
    half3 color = DownsampleBox13Tap(_MainTex, i.texcoord, _MainTex_TexelSize.xy);
    return half4(color, 1.0);
}

half4 FragUpsampleTent(VaryingsDefault i) : SV_Target
{
    // half3 bloom = UpsampleTent(_MainTex, i.texcoord, _MainTex_TexelSize.xy, _SampleScale);
    half3 bloom = UpsampleTent(_MainTex, i.texcoord, _MainTex_TexelSize.xy, _SampleScale);
    return Combine(bloom, i.texcoord);
}

half4 FragAnamorphicBlooom(VaryingsDefault i) : SV_Target
{
    half4 sceneColor = tex2D(_MainTex, i.texcoord);
    float2 bloomSample = i.texcoord;

    half4 bloomColor = tex2D(_BloomTex, bloomSample);
    bloomColor *= _Bloom_Settings.y;
    return sceneColor + bloomColor;
}
