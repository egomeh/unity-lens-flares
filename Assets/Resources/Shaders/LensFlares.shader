Shader "Hidden/Post FX/Lens Flare"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        CGINCLUDE
        #include "LensFlares.cginc"

        #define M_PI 3.1415926535897932384626433832795
        #define M_PI2 6.28318530718

        sampler2D _AperatureTexture;
        int _AperatureEdges;
        float _Smoothing;
        float _Intensity;

        float4 _LineColor;
        float4 _Axis;
        float4 _LightPositionIndicator;
        float4 _LightPositionColor;

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
            for (int i = 0; i < _AperatureEdges; ++i)
            {
                float angle = M_PI2 * (float)i / (float)_AperatureEdges;
                float2 axis = float2(cos(angle), sin(angle));
                distance = smax(distance, dot(axis, coord), -log(1. - (_Smoothing + .0001)));
            }
            return distance;
        }

        half4 FlareProjectionFragment(VaryingsDefault i) : SV_Target
        {
            float2 coord = i.texcoord * 2. - 1.;
            float d = PolygonShape(coord);
            d = smoothstep(0., 1., pow(d + .2, 48.));
            d = saturate(1. - d);
            // d *= _Intensity;
            return float4(1, 1, 1, d * _Intensity);
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
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragPrefilter13
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragDownsample13
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragUpsampleTent
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment FragAnamorphicBlooom
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex VertDefaultBlit
            #pragma fragment FlareProjectionFragment
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex VertDefault
            #pragma fragment DebugDrawLineFragment
            ENDCG
        }
    }
}
