﻿Shader "Hidden/Post FX/Lens Flare"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend Off
        CGINCLUDE
        #pragma multi_compile __ BLUR_PASS_VERTICAL
        #pragma target 3.0

        #include "LensFlares.cginc"

        sampler2D _ApertureTexture;
        sampler2D _ApertureFFTTexture;
        sampler2D _FlareTexture;

        sampler2D _Real;
        sampler2D _Imaginary;

        sampler2D _ApertureFFT;

        float _VisibilitySamples;

        sampler2D _TransmittanceResponse;
        float4 _LightColor;

        int _ApertureEdges;
        float _Smoothing;

        float4 _FlareColor;

        float _ApertureScale;
        float _IntensityMultiplier;

        float2 _BlurDirection;

        half4 DrawApertureSDFFragment(VaryingsDefault i) : SV_Target
        {
            float2 coord = i.texcoord * 2. - 1.;

            float polygon = PolygonShape(coord, _ApertureEdges, _Smoothing);

            polygon = smoothstep(0., 1., pow(polygon + .2, 48.));
            polygon = saturate(1. - polygon);

            return step(.8, polygon);
        }

        half4 FlareProjectionFragment(VaryingsDefault i) : SV_Target
        {
            float d = tex2D(_ApertureTexture, i.texcoord).r;
            return d * _FlareColor * Visibility();
        }

        float4 ComposeOverlayFragment(VaryingsDefault i) : SV_Target
        {
            return tex2D(_MainTex, i.texcoord);
        }

        float4 GaussianBlurFragment5tap(VaryingsDefault i) : SV_Target
        {
            float2 offset = _MainTex_TexelSize.xy * _BlurDirection;

            float4 blurred = 0.;

            blurred += tex2D(_MainTex, i.texcoord + offset * -4.) * .091638;
            blurred += tex2D(_MainTex, i.texcoord + offset *  4.) * .091638;

            blurred += tex2D(_MainTex, i.texcoord + offset * -3.) * .105358;
            blurred += tex2D(_MainTex, i.texcoord + offset *  3.) * .105358;

            blurred += tex2D(_MainTex, i.texcoord + offset * -2.) * .116402;
            blurred += tex2D(_MainTex, i.texcoord + offset *  2.) * .116402;

            blurred += tex2D(_MainTex, i.texcoord + offset * -1.) * .123572;
            blurred += tex2D(_MainTex, i.texcoord + offset *  1.) * .123572;

            blurred += tex2D(_MainTex, i.texcoord) * 0.126063;

            return blurred;
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

            float d1 = tex2D(_ApertureFFTTexture, texcoordRed).r;
            float d2 = tex2D(_ApertureFFTTexture, texcoordGreen).r;
            float d3 = tex2D(_ApertureFFTTexture, texcoordBlue).r;
            float d = length(float3(d1, d2, d3));

            return max(0., float4(d1, d2, d3, d)) * Visibility();
        }

        float4 EdgeFadeFragment(VaryingsDefault i) : SV_Target
        {
            float4 color = tex2D(_MainTex, i.texcoord);

            float2 coord = i.texcoord * 2. - 1.;

            float distanceFromCenter = length(coord);
            return color * (1. - exp((distanceFromCenter - .99)));
        }

        float4 CenterScaleFadePowerSpectrumFragment(VaryingsDefault i) : SV_Target
        {
            // Center the power spectrum value
            float2 coord = frac(i.texcoord + float2(.5, .5));

            float r1 = tex2D(_Real, coord).r;
            float i1 = tex2D(_Imaginary, coord).r;

            float powerSpectrumIntensity = r1 * r1 + i1 * i1;

            // Scale down the power spectrum
            powerSpectrumIntensity *= 1e-4;

            // Fade out the edges
            coord = i.texcoord * 2. - 1.;

            float fadeFactor = (1. - exp((length(coord) - .99)));

            return powerSpectrumIntensity * fadeFactor;
        }
        ENDCG

        Pass // 0
        {
            Blend One One
            CGPROGRAM
            #pragma vertex VertFlareGPUProjection
            #pragma fragment FlareProjectionFragment
            ENDCG
        }

        Pass // 1
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment DrawApertureSDFFragment
            ENDCG
        }

        Pass // 2
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment GaussianBlurFragment5tap
            ENDCG
        }

        Pass // 3
        {
            Blend One One
            CGPROGRAM
            #pragma vertex VertStarburst
            #pragma fragment StarburstFragment
            ENDCG
        }

        Pass // 4
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment CenterScaleFadePowerSpectrumFragment
            ENDCG
        }

        Pass // 5
        {
            Blend One One
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment ComposeOverlayFragment
            ENDCG
        }
    }
}
