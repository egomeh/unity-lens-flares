using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[ImageEffectAllowedInSceneView]
public class LensFlaresMatrixMethod  : MonoBehaviour
{
    public static class GraphicsUtils
    {
        public static void Destroy(UnityEngine.Object obj)
        {
            if (obj != null)
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(obj);
                else
                    UnityEngine.Object.DestroyImmediate(obj);
#else
                Destroy(obj);
#endif
            }
        }
    }

    static class Uniforms
    {
        public static readonly int _Threshold = Shader.PropertyToID("_Threshold");
        public static readonly int _SampleScale = Shader.PropertyToID("_SampleScale");
        public static readonly int _BoolmTex = Shader.PropertyToID("_BloomTex");
        public static readonly int _Bloom_Settings = Shader.PropertyToID("_Bloom_Settings");
    }

    [Serializable]
    public class Lens
    {
        [Range(0f, 1f)]
        public float position =  .5f;

        [Range(-1f, 1f)]
        public float curvature = 0f;

        [Range(1f, 2f)]
        public float refractiveIndex = 1.1f;
    }


    public Light light;

    [Range(0f, 1f)]
    public float aparatureLocation = .5f;

    public Lens[] lensSystem;


    const int k_MaxPyramidSize = 16;

    private Shader m_Shader;
    private Shader shader
    {
        get
        {
            if (!m_Shader)
            {
                m_Shader = Shader.Find("Hidden/Post FX/Lens Flare");
            }

            return m_Shader;
        }
    }

    private Material m_Material;
    private Material material
    {
        get
        {
            if (!m_Material)
            {
                if (shader == null || !shader.isSupported)
                {
                    return null;
                }

                m_Material = new Material(shader);
            }

            return m_Material;
        }
    }

    Mesh m_Quad;
    Mesh quad
    {
        get
        {
            if (!m_Quad)
            {
                m_Quad = new Mesh();
            }

            return m_Quad;
        }
    }

    static Texture2D m_WhiteTexture;
    public static Texture2D whiteTexture
    {
        get
        {
            if (m_WhiteTexture == null)
            {
                m_WhiteTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                m_WhiteTexture.SetPixel(0, 0, Color.white);
                m_WhiteTexture.Apply();
            }

            return m_WhiteTexture;
        }
    }
    
    void OnDisable()
    {
        GraphicsUtils.Destroy(m_Material);
        GraphicsUtils.Destroy(m_Quad);

        m_Quad = null;
        m_Material = null;
    }

    void OnPostRender()
    {
        // Draw flares in screen as quads!
        float[,] opticalSystem = new float[2,2];
    }

    Texture2D RenderateRegularPolygonAparatureTexture(int sides, int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.ARGB32, true);
        return texture;
    }
}
