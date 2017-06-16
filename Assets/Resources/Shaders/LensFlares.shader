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

        float DistanceToPlane2D(float2 intersection, float2 normal, float p)
        {
            return dot(intersection - p, normalize(normal));
        }

        half4 FlareProjectionFragment(VaryingsDefault i) : SV_Target
        {
            float2 uvs = i.texcoord * 2. - 1.;
            float d = smoothstep(1., 0., length(uvs));
            return saturate(d);
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
    }
}
