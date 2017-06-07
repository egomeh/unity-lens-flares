using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[ImageEffectAllowedInSceneView]
public class LensFlares : MonoBehaviour
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
    }

    public Light light;

    public float threshold;
    public float softKnee;

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

    const int k_MaxPyramidBlurLevel = 16;
    readonly RenderTexture[] m_BlurBuffer1 = new RenderTexture[k_MaxPyramidBlurLevel];
    readonly RenderTexture[] m_BlurBuffer2 = new RenderTexture[k_MaxPyramidBlurLevel];

    void OnDisable()
    {
        GraphicsUtils.Destroy(m_Material);
        m_Material = null;
    }

    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Prefiltering parameters
        float lthresh = Mathf.GammaToLinearSpace(this.threshold);
        float knee = lthresh * this.softKnee + 1e-5f;
        var threshold = new Vector4(lthresh, lthresh - knee, knee * 2f, 0.25f / knee);
        material.SetVector(Uniforms._Threshold, threshold);

        int tw = Screen.width / 2;
        int th = Screen.height / 2;

        var prefilter = RenderTexture.GetTemporary(tw, th, 0, source.format);
        Graphics.Blit(source, prefilter, material, 0);

        Graphics.Blit(prefilter, destination);
    }
}
