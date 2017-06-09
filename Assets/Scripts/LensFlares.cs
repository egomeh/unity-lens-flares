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
        public static readonly int _SampleScale = Shader.PropertyToID("_SampleScale");
        public static readonly int _BoolmTex = Shader.PropertyToID("_BloomTex");
        public static readonly int _Bloom_Settings = Shader.PropertyToID("_Bloom_Settings");
    }

    public Light light;

    public float threshold;
    public float softKnee;
    public float intensity = 1;

    [Range(1f, 6f)]
    public float stretch = 1f;
    public int diffusion = 6;


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
        m_Material = null;
    }


    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Determine the iteration count
        float logh = Mathf.Log(Screen.height, 2f) + diffusion - 10f;
        int logh_i = Mathf.FloorToInt(logh);
        int iterations = Mathf.Clamp(logh_i, 1, k_MaxPyramidSize);
        float sampleScale = 0.5f + logh - logh_i;
        material.SetFloat(Uniforms._SampleScale, sampleScale);

        // Prefiltering parameters
        float lthresh = Mathf.GammaToLinearSpace(this.threshold);
        float knee = lthresh * this.softKnee + 1e-5f;
        var threshold = new Vector4(lthresh, lthresh - knee, knee * 2f, 0.25f / knee);
        material.SetVector(Uniforms._Threshold, threshold);

        int tw = Screen.width / 2;
        int th = Screen.height / 2;

        RenderTexture[] pyramidDown = new RenderTexture[k_MaxPyramidSize];
        RenderTexture[] pyramidUp = new RenderTexture[k_MaxPyramidSize];

        // Downsample
        var last = source;
        for (int i = 0; i < iterations; i++)
        {
            pyramidDown[i] = RenderTexture.GetTemporary(tw, th, 0, source.format);
            pyramidUp[i] = RenderTexture.GetTemporary(tw, th, 0, source.format);
            Graphics.Blit(last, pyramidDown[i], material, i == 0 ? 0 : 1);

            last = pyramidDown[i];
            tw /= 2; th /= 2;
        }

        // Upsample
        last = pyramidDown[iterations - 1];
        for (int i = iterations - 2; i >= 0; i--)
        {
            material.SetTexture(Uniforms._BoolmTex, pyramidDown[i]);
            Graphics.Blit(last, pyramidUp[i], material, 2);
            last = pyramidUp[i];
        }

        Vector4 bloomSettings = new Vector4(
            sampleScale,
            Mathf.Exp((intensity / 10f) * 0.69314718055994530941723212145818f) - 1f,
            stretch,
            iterations
        );

        material.SetVector(Uniforms._Bloom_Settings, bloomSettings);
        material.SetTexture(Uniforms._BoolmTex, last);
        Graphics.Blit(source, destination, material, 3);

        for (int i = 0; i < iterations; i++)
        {
            RenderTexture.ReleaseTemporary(pyramidDown[i]);
            RenderTexture.ReleaseTemporary(pyramidUp[i]);
        }
    }
}
