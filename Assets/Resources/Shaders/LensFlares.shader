Shader "Hidden/Post FX/Lens Flare"
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

        #include "LensFlares.cginc"

        sampler2D _ApertureTexture;
        sampler2D _ApertureFTTexture;
        sampler2D _FlareTexture;

        sampler2D _Real;
        sampler2D _Imaginary;

        sampler2D _ApertureFFT;

        sampler2D _TransmittanceResponse;
        float _AngleToLight;
        float4 _LightColor;

        int _ApertureEdges;
        float _Smoothing;

        float4 _FlareColor;
        float4 _LineColor;
        float4 _Axis;
        float4 _LightPositionIndicator;
        float4 _LightPositionColor;

        float _ApertureScale;

        // polynomial smooth min
        // from: http://www.iquilezles.org/www/articles/smin/smin.htm
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

        half4 PolygonShape(float2 coord)
        {
            float distance = 0.;
            for (int i = 0; i < _ApertureEdges; ++i)
            {
                float angle = M_PI2 * (float)i / (float)_ApertureEdges;
                float2 axis = float2(cos(angle), sin(angle));
                // distance = smax(distance, dot(axis, coord), -log(1. - (_Smoothing + .0001)));
                distance = smax(distance, dot(axis, coord), _Smoothing);
            }

            float apothem = cos(M_PI / _ApertureEdges);
            float radius = apothem / (2.  * sin(M_PI / _ApertureEdges));
            float circle = saturate(length(coord));

            return lerp(distance, circle, _Smoothing);
        }

        float rand(float3 co)
        {
            return frac(sin( dot(co.xyz ,float3(12.9898,78.233,45.5432) )) * 43758.5453);
        }

        half4 DrawApertureSDFFragment(VaryingsDefault i) : SV_Target
        {
            float2 coord = i.texcoord * 2. - 1.;
            coord *= _ApertureScale;
            float polygon = PolygonShape(coord);

            polygon = smoothstep(0., 1., pow(polygon + .2, 48.));
            polygon = saturate(1. - polygon);

            return step(.8, polygon);
        }

        half4 FlareProjectionFragment(VaryingsDefault i) : SV_Target
        {
            float d = tex2D(_ApertureTexture, i.texcoord).r;
            return d * _FlareColor;
        }

        float4 DebugDrawLineFragment(VaryingsDefault i) : SV_Target
        {
            half4 sceneColor = tex2D(_MainTex, i.texcoord);

            float aspect = _ScreenParams.x / _ScreenParams.y;

            float2 coord = i.texcoord * 2. - 1.;
            float2 w = normalize(float2(-_Axis.y, _Axis.x));
            //w.x *= aspect;
            float d = abs(dot(w, coord));
            float l = saturate(1. - d);
            l = smoothstep(0., 1., l - .5);
            float overlay = step(.49, l) * _LineColor;

            float distanceToLight = length((coord - _LightPositionIndicator.xy) * float2(aspect, 1.));
            distanceToLight = step(.95, saturate(1. - distanceToLight));

            return sceneColor + saturate(overlay) * _LineColor + distanceToLight * _LightPositionColor;
        }

        float4 ComposeFragment(VaryingsDefault i) : SV_Target
        {
            float4 sceneColor = tex2D(_MainTex, i.texcoord);
            float4 flareColor = tex2D(_FlareTexture, i.texcoord);

            return sceneColor + flareColor;
        }

        float4 ComposeOverlayFragment(VaryingsDefault i) : SV_Target
        {
            return tex2D(_FlareTexture, i.texcoord);
        }

        float4 ClearFragment(VaryingsDefault i) : SV_Target
        {
            return 0.;
        }

        float4 CenterFFTPowerSpectrum(VaryingsDefault i) : SV_Target
        {
            float2 coord = frac(i.texcoord + float2(.5, .5));

            float r1 = tex2D(_Real, coord).r;
            float i1 = tex2D(_Imaginary, coord).r;

            return r1 * r1 + i1 * i1;
        }

        float4 ApertureSideBySide(VaryingsDefault i) : SV_Target
        {
            float2 scale = float2(2., 1.);
            float fft = tex2D(_ApertureFTTexture, i.texcoord * scale).r;
            float aperture = tex2D(_ApertureTexture, i.texcoord * scale - float2(1., 0.)).r;

            return lerp(fft, aperture, step(.5, i.texcoord.x));
        }

        float4 GaussianBlurFragment5tap(VaryingsDefault i) : SV_Target
        {
        #if defined(BLUR_PASS_VERTICAL)
            float2 offset = float2(0., _MainTex_TexelSize.y);
        #else
            float2 offset = float2(_MainTex_TexelSize.x, 0.);
        #endif

            float4 blurred = 0.;

            blurred += tex2D(_MainTex, i.texcoord + offset * -4.) * 0.091638;
            blurred += tex2D(_MainTex, i.texcoord + offset * 4.) * 0.091638;

            blurred += tex2D(_MainTex, i.texcoord + offset * -3.) * 0.105358;
            blurred += tex2D(_MainTex, i.texcoord + offset * 3.) * 0.105358;

            blurred += tex2D(_MainTex, i.texcoord + offset * -2.) * 0.116402;
            blurred += tex2D(_MainTex, i.texcoord + offset * 2.) * 0.116402;

            blurred += tex2D(_MainTex, i.texcoord + offset * -1.) * 0.123572;
            blurred += tex2D(_MainTex, i.texcoord + offset * 1.) * 0.123572;

            blurred += tex2D(_MainTex, i.texcoord) * 0.126063;

            return blurred;
        }

        float4 ToneMapFragment(VaryingsDefault i) : SV_Target
        {
            float exposure = .0002;
            float gamma = 0.6;

            float intensity = tex2D(_MainTex, i.texcoord).r;


            // Exposure tone mapping
            float toneMapped = 1. - exp(-intensity * exposure);
            // Gamma correction 
            toneMapped = pow(toneMapped, 1. / gamma);

            return toneMapped;
        }

        float4 StarburstFragment(VaryingsDefault i) : SV_Target
        {
            float2 coord = i.texcoord * 2. - 1.;

            float2 coordRed = coord / _LightColor.r;
            float2 coordGreen = coord / _LightColor.g;
            float2 coordBlue = coord / _LightColor.b;

            float2 texcoordRed = coordRed * .5 + .5;
            float2 texcoordGreen = coordGreen  * .5 + .5;
            float2 texcoordBlue = coordBlue * .5 + .5;

            float d1 = tex2D(_ApertureTexture, texcoordRed).r;
            float d2 = tex2D(_ApertureTexture, texcoordGreen).r;
            float d3 = tex2D(_ApertureTexture, texcoordBlue).r;
            float d = length(float3(d1, d2, d3));
            //float4 transmittance = tex2D(_TransmittanceResponse, float2(_AngleToLight, 0.));
            return float4(d1, d2, d3, d);
        }

        float4 EdgeFadeFragment(VaryingsDefault i) : SV_Target
        {
            float4 color = tex2D(_MainTex, i.texcoord);

            float2 coord = i.texcoord * 2. - 1.;
            coord *= 1.2;

            float distanceFromCenter = length(coord);
            return color * (1. - exp((distanceFromCenter - .95)));
        }

        ENDCG

        Pass // 0
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragPrefilter13
            ENDCG
        }

        Pass // 1
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragDownsample13
            ENDCG
        }

        Pass // 2
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragUpsampleTent
            ENDCG
        }

        Pass // 3
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragAnamorphicBlooom
            ENDCG
        }

        Pass // 4
        {
            CGPROGRAM
            #pragma vertex VertDefaultBlit
            #pragma fragment FlareProjectionFragment
            ENDCG
        }

        Pass // 5
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment DebugDrawLineFragment
            ENDCG
        }

        Pass // 6
        {
            Blend Off
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment ComposeFragment
            ENDCG
        }

        Pass // 7
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment ClearFragment
            ENDCG
        }

        Pass // 8
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment DrawApertureSDFFragment
            ENDCG
        }

        Pass // 9
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment CenterFFTPowerSpectrum
            ENDCG
        }

        Pass // 10
        {
            Blend Off
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment ApertureSideBySide
            ENDCG
        }

        Pass // 11
        {
            Blend Off
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment GaussianBlurFragment5tap
            ENDCG
        }

        Pass // 12
        {
            Blend Off
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment ToneMapFragment
            ENDCG
        }

        Pass // 13
        {
            CGPROGRAM
            #pragma vertex VertDefaultBlit
            #pragma fragment StarburstFragment
            ENDCG
        }

        Pass // 14
        {
            Blend Off
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment EdgeFadeFragment
            ENDCG
        }

        Pass // 15
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment ComposeOverlayFragment
            ENDCG
        }
    }
}
