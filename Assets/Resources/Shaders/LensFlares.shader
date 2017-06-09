Shader "Hidden/Post FX/Lens Flare"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "LensFlares.cginc"
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
    }
}
