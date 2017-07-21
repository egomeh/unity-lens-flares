﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[ImageEffectAllowedInSceneView]
public class LensFlaresMatrixMethod : MonoBehaviour
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
        public static readonly int _FlareCanvas = Shader.PropertyToID("_FlareCanvas");

        // Transform that scales and translates the flare onto the screen
        public static readonly int _FlareTransform = Shader.PropertyToID("_FlareTransform");

        // A signed distance field the represents the 
        public static readonly int _ApertureTexture = Shader.PropertyToID("_ApertureTexture");
        public static readonly int _FlareTexture = Shader.PropertyToID("_FlareTexture");
        public static readonly int _ApertureEdges = Shader.PropertyToID("_ApertureEdges");
        public static readonly int _Smoothing = Shader.PropertyToID("_Smoothing");
        public static readonly int _Intensity = Shader.PropertyToID("_Intensity");
        public static readonly int _FlareColor = Shader.PropertyToID("_FlareColor");

        public static readonly int _ApertureScale = Shader.PropertyToID("_ApertureScale");

        public static readonly int _AngleToLight = Shader.PropertyToID("_AngleToLight");
        public static readonly int _LightColor = Shader.PropertyToID("_LightColor");
        public static readonly int _LightWavelength = Shader.PropertyToID("_LightWavelength");
        public static readonly int _Axis = Shader.PropertyToID("_Axis");
        public static readonly int _SystemEntranceToAperture = Shader.PropertyToID("_SystemEntranceToAperture");

        public static readonly int _GhostDataBuffer = Shader.PropertyToID("_GhostDataBuffer");
        public static readonly int _ApertureHeight = Shader.PropertyToID("_ApertureHeight");
    }

    enum FlareShaderPasses
    {
        DrawGhost = 0,
        DrawApertureShape = 1,
        CenterPowerSpectrum = 2,
        GaussianBlur = 3,
        ScaleFourierTransform = 4,
        DrawStarburst = 5,
        EdgeFade = 6,
        ComposeOverlay = 7,
    }

    class Ghost
    {
        Matrix4x4 m_EntranceToAperture;
        Matrix4x4 m_ApertureToSensor;

        float m_N1, m_N2;

        public Ghost(Matrix4x4 entranceToAperture, Matrix4x4 apertureToSensor, float n1, float n2)
        {
            m_EntranceToAperture = entranceToAperture;
            m_ApertureToSensor = apertureToSensor;
            m_N1 = n1;
            m_N2 = n2;
        }

        public Matrix4x4 ma
        {
            get { return m_EntranceToAperture; }
        }

        public Matrix4x4 ms
        {
            get { return m_ApertureToSensor; }
        }

        public float n1
        {
            get { return m_N1; }
        }

        public float n2
        {
            get { return m_N2; }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GhostGPUData
    {
        // Matrix that maps rays from entrance pupil to aperture
        public Matrix4x4 entranceToAperture;

        // Matrix that maps rays from aperture to the sensor plane
        public Matrix4x4 apertureToSensor;

        // x: Refractive index of the element that lies before the reflection
        // y: Refractive index of the element that the flare reflection occurs against
        public Vector4 refractiveIncidences;
    }

    class LensSystem
    {
        Matrix4x4 m_EntranceToAperture;
        Matrix4x4 m_ApertureToSensor;

        public LensSystem(Matrix4x4 entranceToAperture, Matrix4x4 apertureToSensor)
        {
            m_EntranceToAperture = entranceToAperture;
            m_ApertureToSensor = apertureToSensor;
        }

        public Matrix4x4 entranceToAperture
        {
            get { return m_EntranceToAperture; }
        }

        public Matrix4x4 apertureToSensor
        {
            get { return m_ApertureToSensor; }
        }
    }

    [Serializable]
    public class Lens
    {
        public float distanceToNext = .5f;

        public bool flat = true;

        public float radius = 0f;

        public bool air = true;
        public float refractiveIndex = 1.1f;

        public float GetRefractiveIndex()
        {
            if (air)
            {
                return kRefractiveIndexAir;
            }
            return refractiveIndex;
        }
    }

    const float kRefractiveIndexAir = 1.000293f;

    const float kWavelengthRed = 700f;
    const float kWavelengthGreen = 510f;
    const float kWavelengthBlue = 450f;

    // Resolution of the aperture and aperture transform is 1024x1024
    // TODO: Maybe a better resolution would be better?
    const int kApertureResolution = 1024;

    const CameraEvent kEventHook = CameraEvent.AfterImageEffects;

    // Compute shader to compute the FFT of the aperture
    public ComputeShader starburstShader;

    public Light mainLight;

    public float distanceToFirstInterface = 1f;

    [Range(1, 8)]
    public int flareBufferDivision = 1;

    public Lens[] interfacesBeforeAperature;

    [Range(1f, 32f)]
    public float aperture = 1f;

    public float distanceToNextInterface = 10f;

    [Range(4, 10)]
    public int aperatureEdges;

    [Range(0f, 1f)]
    public float smoothing;

    [Range(0f, 1f)]
    public float starburstBaseSize = .2f;

    public Lens[] interfacesAfterAperature;

    [Range(300f, 1000f)]
    public float antiReflectiveCoatingWavelength = 450;

    public bool preferInstanced = false;

    Camera m_Camera;

    Camera _camera
    {
        get
        {
            if (m_Camera == null)
            {
                m_Camera = GetComponent<Camera>();
            }
            return m_Camera;
        }
    }

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

    Material material
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

    Shader m_ShaderInstanced;

    Shader shaderInstanced
    {
        get
        {
            if (!m_ShaderInstanced)
            {
                m_ShaderInstanced = Shader.Find("Hidden/Post FX/Lens Flare - Instanced");
            }

            return m_ShaderInstanced;
        }
    }


    Material m_MaterialInstanced;

    Material materialInstanced
    {
        get
        {
            if (!m_MaterialInstanced)
            {
                if (shaderInstanced == null || !shaderInstanced.isSupported)
                {
                    return null;
                }

                m_MaterialInstanced = new Material(shaderInstanced);
                m_MaterialInstanced.enableInstancing = true;
            }

            return m_MaterialInstanced;
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

                Vector3[] vertices = new Vector3[4]
                {
                    new Vector3(1.0f, 1.0f, 0.0f),
                    new Vector3(-1.0f, 1.0f, 0.0f),
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                };

                Vector2[] uvs = new Vector2[4]
                {
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f)
                };

                int[] indices = new int[6] { 0, 1, 2, 2, 3, 0 };

                m_Quad = new Mesh();
                m_Quad.vertices = vertices;
                m_Quad.uv = uvs;
                m_Quad.triangles = indices;
            }

            return m_Quad;
        }
    }

    RenderTexture m_apertureTexture;

    RenderTexture apertureTexture
    {
        get
        {
            if (!m_apertureTexture)
            {
                Prepare();
            }

            return m_apertureTexture;
        }
    }

    // A Fourier transform of the aperture shape
    // Used to make the `star-burst` flare placed on the light itself
    RenderTexture m_ApertureFourierTransform;

    RenderTexture apertureFourierTransform
    {
        get
        {
            if (!m_ApertureFourierTransform)
            {
                Prepare();
            }

            return m_ApertureFourierTransform;
        }
    }

    LensSystem m_LensSystem;

    LensSystem lensSystem
    {
        get
        {
            if (m_LensSystem == null)
            {
                Prepare();
            }

            return m_LensSystem;
        }
    }

    List<Ghost> m_FlareGhosts = null;

    List<Ghost> flareGhosts
    {
        get
        {
            if (m_FlareGhosts == null)
            {
                Prepare();
            }

            return m_FlareGhosts;
        }
    }

    ComputeBuffer m_GhostDataBuffer;

    ComputeBuffer ghostDataBuffer
    {
        get
        {
            if (m_GhostDataBuffer == null)
            {
                Prepare();
            }

            return m_GhostDataBuffer;
        }
    }

    CommandBuffer m_CommandBuffer;

    public void Clean()
    {
        GraphicsUtils.Destroy(m_Material);
        GraphicsUtils.Destroy(m_Quad);
        GraphicsUtils.Destroy(m_apertureTexture);
        GraphicsUtils.Destroy(m_ApertureFourierTransform);

        if (m_GhostDataBuffer != null)
        {
            m_GhostDataBuffer.Dispose();
            m_GhostDataBuffer = null;
        }

        if (m_CommandBuffer != null)
        {
            _camera.RemoveCommandBuffer(kEventHook, m_CommandBuffer);
            m_CommandBuffer.Dispose();
        }

        m_Quad = null;
        m_Material = null;
        m_apertureTexture = null;
        m_ApertureFourierTransform = null;
        m_FlareGhosts = null;
        m_LensSystem = null;
        m_CommandBuffer = null;
        m_GhostDataBuffer = null;
    }

    void OnDisable()
    {
        Clean();
    }

    void OnValidate()
    {
        Clean();
    }

    static float Reflectance(float wavelength, float coating, float angle, float n1, float n2, float n3)
    {
        // Apply Snell's law to get the other angles
        float angle2 = Mathf.Asin(n1 * Mathf.Asin(angle) / n2);
        float angle3 = Mathf.Asin(n1 * Mathf.Asin(angle) / n3);

        float cos1 = Mathf.Cos(angle);
        float cos2 = Mathf.Cos(angle2);
        float cos3 = Mathf.Cos(angle3);

        float beta = (2.0f * Mathf.PI) / wavelength * n2 * coating * cos2;

        // Compute the Fresnel terms for the first and second interfaces for both s and p polarized
        // light
        float r12p = (n2 * cos1 - n1 * cos2) / (n2 * cos1 + n1 * cos2);
        float r12p2 = r12p * r12p;

        float r23p = (n3 * cos2 - n2 * cos3) / (n3 * cos2 + n2 * cos3);
        float r23p2 = r23p * r23p;

        float rp = (r12p2 + r23p2 + 2.0f * r12p * r23p * Mathf.Cos(2.0f * beta)) /
            (1.0f + r12p2 * r23p2 + 2.0f * r12p * r23p * Mathf.Cos(2.0f * beta));

        float r12s = (n1 * cos1 - n2 * cos2) / (n1 * cos1 + n2 * cos2);
        float r12s2 = r12s * r12s;

        float r23s = (n2 * cos2 - n3 * cos3) / (n2 * cos2 + n3 * cos3);
        float r23s2 = r23s * r23s;

        float rs = (r12s2 + r23s2 + 2.0f * r12s * r23s * Mathf.Cos(2.0f * beta)) /
            (1.0f + r12s2 * r23s2 + 2.0f * r12s * r23s * Mathf.Cos(2.0f * beta));

        return (rs + rp) * .5f;
    }

    void Prepare()
    {
        // Precomputed steps are:
        // step 1: Define transforms for the light passing though the lens system
        // step 2: Find all ghosts projected on the sensor plane (light that reflects twice)
        // step 3: For each ghost: Compute matrices that maps light from entrance pupil to
        //          aperture, and from aperture to sensor plane
        // step 4: Render a texture that holds a signed distance field of the aperture shape
        // step 5: Compute a Fourier transform of the aperture shape
        // Step 6: **EXPERIMENTAL - SO FAR NOT SUCCESSFUL** Compute a texture that maps angle to the light source
        //          To an RGB value that describes how much light of each wavelength
        //          that passes through the lens at the given angle.

        // Steps to be carried out when rendering are:
        // For each light that should cause flares:
        //      Find the angle to- and the screen space position of the light
        //      for each ghost:
        //          For the angle of incoming light, map aperture bounds to lens entrance.
        //          Map the found points on the entrance to the sensor plane.
        //          Intensity of ghost: ratio between surface area of the ghost on the entrance pupil
        //                              and the surface area of the whole entrance pupil.
        //          Color of the ghost: Wavelengths for red, green and blue light (700nm, 510nm, 450nm)
        //                              are tested against a anti-reflective coating at a semi-random (angle to light but clipped)
        //                              angle, to compute the amount of light that is reflected between interfaces.
        //      Draw the ghost with the computed color and intensity additively on the screen.
        // Draw the star-burst texture on position of the light in screen space.


        // *** Step 1 ***

        // Concatenate the whole lens system, including aperture into
        // a single list of elements.

        // Compute transform matrices for each optical interface.
        // These are:
        //      - Translation / inverse translation
        //      - Refraction / inverse refraction
        //      - Reflection / inverse reflection
        // These transforms describe how light changes angle
        // and distance from the optical axis when passing though interfaces.
        // The inverse transforms are *NOT* the inverse matrices, but transforms
        // that describes how the light changes when passing an element in the opposite
        // direction.

        int totalInterfaces = interfacesBeforeAperature.Length + interfacesAfterAperature.Length + 1;

        // The inverse translation has the same effect as the forward
        // so only the forward is kept.
        Matrix4x4[] Translations = new Matrix4x4[totalInterfaces];

        Matrix4x4[] Reflections = new Matrix4x4[totalInterfaces];
        Matrix4x4[] ReflectionsInverse = new Matrix4x4[totalInterfaces];

        Matrix4x4[] Refractions = new Matrix4x4[totalInterfaces];
        Matrix4x4[] RefractionsInverse = new Matrix4x4[totalInterfaces];

        Lens[] interfaces = new Lens[totalInterfaces];
        interfacesBeforeAperature.CopyTo(interfaces, 0);
        interfacesAfterAperature.CopyTo(interfaces, interfacesBeforeAperature.Length + 1);

        // Make the aperture a part of the lens system.
        // represented as a flat interface with air filling the 
        // gap to the next interface.
        interfaces[interfacesBeforeAperature.Length] = new Lens()
        {
            distanceToNext = distanceToNextInterface,
            air = true,
            refractiveIndex = kRefractiveIndexAir,
            flat = true,
        };

        // T0 is the translation from the entrance of the camera
        // to the first optical element
        Matrix4x4 T0 = Matrix4x4.identity;
        T0[0, 1] = distanceToFirstInterface;

        // Go though each interface and build the transforms
        float previousRefractiveIndex = kRefractiveIndexAir;
        for (int i = 0; i < totalInterfaces; ++i)
        {
            Matrix4x4 T = Matrix4x4.identity;
            T[0, 1] = interfaces[i].distanceToNext;

            Matrix4x4 R = Matrix4x4.identity;
            Matrix4x4 RInv = Matrix4x4.identity;

            Matrix4x4 L = Matrix4x4.identity;
            Matrix4x4 LInv = Matrix4x4.identity;

            float refractiveIndex = interfaces[i].GetRefractiveIndex();

            if (interfaces[i].flat)
            {
                R[1, 1] = previousRefractiveIndex / refractiveIndex;
                RInv[1, 1] = refractiveIndex / previousRefractiveIndex;
                // L is identity for flat interfaces
            }
            else
            {
                float radius = interfaces[i].radius;
                R[1, 0] = (previousRefractiveIndex - refractiveIndex) / (radius * refractiveIndex);
                R[1, 1] = previousRefractiveIndex / refractiveIndex;

                RInv[1, 0] = (refractiveIndex - previousRefractiveIndex) / (-radius * previousRefractiveIndex);
                RInv[1, 1] = refractiveIndex / previousRefractiveIndex;

                L[1, 0] = 2f / radius;
                LInv[1, 0] = 2f / -radius;
            }

            Translations[i] = T;
            Refractions[i] = R;
            RefractionsInverse[i] = RInv;
            Reflections[i] = L;
            ReflectionsInverse[i] = LInv;

            previousRefractiveIndex = refractiveIndex;
        }

        // Record the transform that maps rays from entrance to aperture
        // and from aperture to sensor plane
        Matrix4x4 systemEntranceToAperture = T0;
        Matrix4x4 systemApertureToSensor = Translations[interfacesBeforeAperature.Length];

        for (int i = 0; i < interfacesBeforeAperature.Length; ++i)
        {
            systemEntranceToAperture = Translations[i] * Refractions[i] * systemEntranceToAperture;
        }

        for (int i = interfacesBeforeAperature.Length + 1; i < totalInterfaces; ++i)
        {
            systemApertureToSensor = Translations[i] * Refractions[i] * systemApertureToSensor;
        }

        m_LensSystem = new LensSystem(systemEntranceToAperture, systemApertureToSensor);

        // *** Step 2 and 3 ***

        // A list of ghosts generated by light paths that has reflected
        // exactly twice
        m_FlareGhosts = new List<Ghost>();

        // refract until the j th interface.
        // reflect on j th interface.
        // refract back to i th interface.
        // reflect again on the i th interface.
        // refract throughout the rest of the system to the sensor plane.


        // Split in two passes.
        // One is for reflections that occur before aperture.
        // The other for reflections that occur after the aperture.
        // Situations where a ray passes the aperture more than once are ignored.

        // The index of the interface that corresponds to the aperture
        int apertureIndex = interfacesBeforeAperature.Length;

        // First pass is all even reflections form entrance to aperture
        for (int i = 0; i < interfacesBeforeAperature.Length - 1; ++i)
        {
            for (int j = i + 1; j < apertureIndex; ++j)
            {
                // Debug.Log("r -> " + j + " <- " + i);

                Matrix4x4 entranceToAperture = T0;

                // Refract on all interfaces up to j th interface
                for (int k = 0; k < j; ++k)
                {
                    entranceToAperture = Translations[k] * Refractions[k] * entranceToAperture;
                }

                // reflect from j th interface
                entranceToAperture = Reflections[j] * entranceToAperture;

                // refract in reverse order from j th interface back to the i th interface
                for (int k = j - 1; k > i; --k)
                {
                    entranceToAperture = RefractionsInverse[k] * Translations[k] * entranceToAperture;
                }

                // reflect on the i th interface
                entranceToAperture = Translations[i] * ReflectionsInverse[i] * Translations[i] * entranceToAperture;

                // refract to the aperture
                for (int k = i + 1; k < interfacesBeforeAperature.Length; ++k)
                {
                    entranceToAperture = Translations[k] * Refractions[k] * entranceToAperture;
                }

                Matrix4x4 aperatureToSensor = Translations[apertureIndex] * Refractions[apertureIndex];

                for (int l = 0; l < interfacesAfterAperature.Length; ++l)
                {
                    int index = apertureIndex + 1 + l;
                    aperatureToSensor = Translations[index] * Refractions[index] * aperatureToSensor;
                }

                float n1 = 0f;

                if (i == 0)
                {
                    n1 = kRefractiveIndexAir;
                }
                else
                {
                    n1 = interfaces[i - 1].air ? kRefractiveIndexAir : interfaces[i - 1].refractiveIndex;
                }

                float n2 = interfaces[i].air ? kRefractiveIndexAir : interfaces[i].refractiveIndex;

                flareGhosts.Add(new Ghost(entranceToAperture, aperatureToSensor, n1, n2));
            }
        }

        // Second pass is all reflections that occur between aperture and sensor plane
        for (int i = apertureIndex + 1; i < interfacesAfterAperature.Length - 1; ++i)
        {
            for (int j = i + 1; j < interfacesAfterAperature.Length; ++j)
            {
                // Debug.Log("r -> " + j + " <- " + i);

                Matrix4x4 entranceToAperture = T0;

                // Refract all the way to the aperture
                for (int k = 0; k < interfacesBeforeAperature.Length; ++k)
                {
                    entranceToAperture = Translations[k] * Refractions[k] * entranceToAperture;
                }

                Matrix4x4 aperatureToSensor = Translations[apertureIndex] * Refractions[apertureIndex];

                // Refract from aperture to j th interface
                for (int k = apertureIndex + 1; k < j; ++k)
                {
                    aperatureToSensor = Translations[k] * Refractions[k] * aperatureToSensor;
                }

                // Reflect on the j th interface
                aperatureToSensor = Reflections[j] * aperatureToSensor;

                // refract in reverse order from the j th interface back to the i th interface
                for (int k = j - 1; k > i; --k)
                {
                    aperatureToSensor = RefractionsInverse[k] * Translations[k] * aperatureToSensor;
                }

                // reflect on the i th interface
                aperatureToSensor = Translations[i] * ReflectionsInverse[i] * Translations[i] * aperatureToSensor;

                // refract to the sensor plane
                for (int k = i + 1; k < totalInterfaces; ++k)
                {
                    aperatureToSensor = Translations[k] * Refractions[k] * aperatureToSensor;
                }

                float n1 = interfaces[i - 1].air ? kRefractiveIndexAir : interfaces[i - 1].refractiveIndex;
                float n2 = interfaces[i].air ? kRefractiveIndexAir : interfaces[i].refractiveIndex;
                flareGhosts.Add(new Ghost(entranceToAperture, aperatureToSensor, n1, n2));
            }
        }

        // *** Step 4 ***

        // If the aperture texture is not yet released destroy it first
        if (m_apertureTexture)
        {
            m_apertureTexture.Release();
            GraphicsUtils.Destroy(m_apertureTexture);
            m_apertureTexture = null;
        }

        m_apertureTexture = new RenderTexture(kApertureResolution, kApertureResolution, 0, RenderTextureFormat.ARGB32);
        RenderTexture temporary = new RenderTexture(kApertureResolution, kApertureResolution, 0, RenderTextureFormat.ARGB32);

        m_apertureTexture.filterMode = FilterMode.Bilinear;
        temporary.filterMode = FilterMode.Bilinear;

        temporary.Create();
        m_apertureTexture.Create();

        // Draw the aperture shape as a signed distance field
        material.SetInt(Uniforms._ApertureEdges, aperatureEdges);
        material.SetFloat(Uniforms._Smoothing, smoothing);
        material.SetFloat(Uniforms._ApertureScale, 1f);

        Graphics.Blit(null, m_apertureTexture, material, (int)FlareShaderPasses.DrawApertureShape);

        // *** Step 5 ***

        if (m_ApertureFourierTransform)
        {
            m_ApertureFourierTransform.Release();
            GraphicsUtils.Destroy(m_ApertureFourierTransform);
            m_ApertureFourierTransform = null;
        }

        // Create the RenderTexture that the star-burst will be placed on
        m_ApertureFourierTransform = new RenderTexture(kApertureResolution, kApertureResolution, 0, RenderTextureFormat.RFloat);
        m_ApertureFourierTransform.Create();

        // Create Temporary RenderTextures
        // The first 4 are used to compute the FFT of the aperture
        // index 0 and 1 are used for the target real and imaginary values for the row pass of the FFT
        // index 2 and 3 are used for the target real and imaginary of values for the column pass of the FFT
        // index 4 is first used for a temporary scaled version of the aperture
        // later index 4 is used as a `pingpong` buffer to do a Gaussian blur on the FFT texture
        const int fftTextureCount = 5;
        RenderTexture[] fftTextures = new RenderTexture[fftTextureCount];

        for (int i = 0; i < fftTextureCount; ++i)
        {
            fftTextures[i] = new RenderTexture(kApertureResolution, kApertureResolution, 0, RenderTextureFormat.RFloat);
            fftTextures[i].enableRandomWrite = true;
            fftTextures[i].Create();
        }

        material.SetInt(Uniforms._ApertureEdges, aperatureEdges);
        material.SetFloat(Uniforms._Smoothing, smoothing);
        material.SetFloat(Uniforms._ApertureScale, 2f);

        Graphics.Blit(null, fftTextures[4], material, (int)FlareShaderPasses.DrawApertureShape);

        int kernel = starburstShader.FindKernel("ButterflySLM");

        int butterflyCount = (int)(Mathf.Log(kApertureResolution, 2f) / Mathf.Log(2f, 2f));

        starburstShader.SetInt("_ButterflyCount", butterflyCount);

        // starburstShader.SetTexture(kernel, "TextureSourceR", fftTextures[4]);
        starburstShader.SetTexture(kernel, "TextureSourceR", fftTextures[4]);

        starburstShader.SetTexture(kernel, "TextureTargetR", fftTextures[0]);
        starburstShader.SetTexture(kernel, "TextureTargetI", fftTextures[1]);
        starburstShader.SetInt("_RowPass", 1);
        starburstShader.Dispatch(kernel, 1, kApertureResolution, 1);

        starburstShader.SetTexture(kernel, "TextureSourceR", fftTextures[0]);
        starburstShader.SetTexture(kernel, "TextureSourceI", fftTextures[1]);
        starburstShader.SetTexture(kernel, "TextureTargetR", fftTextures[2]);
        starburstShader.SetTexture(kernel, "TextureTargetI", fftTextures[3]);
        starburstShader.SetInt("_RowPass", 0);
        starburstShader.Dispatch(kernel, 1, kApertureResolution, 1);

        material.SetTexture("_Real", fftTextures[2]);
        material.SetTexture("_Imaginary", fftTextures[3]);
        Graphics.Blit(null, m_ApertureFourierTransform, material, (int)FlareShaderPasses.CenterPowerSpectrum);

        // Blur the Fourier transform of the aperture slightly
        material.EnableKeyword("BLUR_PASS_VERTICAL");
        Graphics.Blit(m_ApertureFourierTransform, fftTextures[4], material, (int)FlareShaderPasses.GaussianBlur);

        material.DisableKeyword("BLUR_PASS_VERTICAL");
        Graphics.Blit(fftTextures[4], m_ApertureFourierTransform, material, (int)FlareShaderPasses.GaussianBlur);

        // Tone map the Fourier transform, as the values are likely much higher than 0..1
        Graphics.Blit(m_ApertureFourierTransform, fftTextures[4], material, (int)FlareShaderPasses.ScaleFourierTransform);

        // Tone the edges down
        // TODO: find a non-hack way of computing the scale of the FFT
        // Perhaps send the full-scale FFT and scale in fragment shader?
        Graphics.Blit(fftTextures[4], m_ApertureFourierTransform, material, (int)FlareShaderPasses.EdgeFade);

        // Blur the aperture texture
        // But maybe get heavier blur rather than run the same blur many times
        for (int i = 0; i < 8; ++i)
        {
            material.EnableKeyword("BLUR_PASS_VERTICAL");
            Graphics.Blit(m_apertureTexture, temporary, material, (int)FlareShaderPasses.GaussianBlur);

            material.DisableKeyword("BLUR_PASS_VERTICAL");
            Graphics.Blit(temporary, m_apertureTexture, material, (int)FlareShaderPasses.GaussianBlur);
        }

        for (int i = 0; i < fftTextureCount; ++i)
        {
            fftTextures[i].Release();
        }

        temporary.Release();
        temporary = null;

        if (m_GhostDataBuffer != null)
        {
            m_GhostDataBuffer.Dispose();
        }

        // If instancing is preferred and supported
        // Put all the needed data for the ghosts in a structured buffer
        if (preferInstanced && SystemInfo.supportsInstancing)
        {
            m_GhostDataBuffer = new ComputeBuffer(m_FlareGhosts.Count, Marshal.SizeOf(typeof(GhostGPUData)));

            GhostGPUData[] ghostData = new GhostGPUData[m_FlareGhosts.Count];

            for (int i = 0; i < flareGhosts.Count; ++i)
            {
                ghostData[i].entranceToAperture = flareGhosts[i].ma;
                ghostData[i].apertureToSensor = flareGhosts[i].ms;
                ghostData[i].refractiveIncidences = new Vector4(flareGhosts[i].n1, flareGhosts[i].n2, 0f, 0f);
            }

            m_GhostDataBuffer.SetData(ghostData);
        }
    }

    void OnPreRender()
    {
        if (m_CommandBuffer == null)
        {
            m_CommandBuffer = new CommandBuffer();
            m_CommandBuffer.name = "Lens flares";

            _camera.AddCommandBuffer(kEventHook, m_CommandBuffer);
        }

        // Make sure the command buffer is empty
        m_CommandBuffer.Clear();

        // Check if the light is visible, if not, don't fill command buffer for the light
        if (mainLight.type == LightType.Directional)
        {
            RaycastHit hit;

            if (Physics.Raycast(_camera.transform.position, -mainLight.transform.forward, out hit, _camera.farClipPlane))
            {
                return;
            }
        }
        else
        {
            RaycastHit hit;

            Vector3 cameraToLight = mainLight.transform.position - _camera.transform.position;
            if (Physics.Raycast(_camera.transform.position, cameraToLight, out hit, cameraToLight.magnitude))
            {
                return;
            }
        }

        const float k_FilmHeight = 24f;
        const float k_SensorSize = 43.2f;

        float fov = _camera.fieldOfView * Mathf.Deg2Rad;
        float focalLenghth = .5f * k_FilmHeight / Mathf.Tan(.5f * fov);

        float apertureHeight = focalLenghth / aperture;

        // Get the angle to the light source

        // Light position in NDC
        Vector3 lightPositionScreenSpace = Vector3.up;

        // The angle between the light direction and the camera forward direction
        float angleToLight = 0f;

        if (mainLight.type == LightType.Point)
        {
            Vector3 directionToLight = mainLight.transform.position - _camera.transform.position;
            angleToLight = Vector3.Angle(directionToLight.normalized, _camera.transform.forward);

            lightPositionScreenSpace = (_camera.projectionMatrix * _camera.worldToCameraMatrix).MultiplyPoint(mainLight.transform.position);
        }
        else
        {
            angleToLight = Vector3.Angle(-mainLight.transform.forward, _camera.transform.forward);

            Vector3 distantPoint = _camera.transform.position + mainLight.transform.forward * _camera.farClipPlane;
            lightPositionScreenSpace = (_camera.projectionMatrix * _camera.worldToCameraMatrix).MultiplyPoint(distantPoint);
        }

        // Axis in screen space that intersects the center of the screen and the light projected
        // in screen in NDC
        Vector2 axis = new Vector2(lightPositionScreenSpace.x, lightPositionScreenSpace.y);
        axis.Normalize();
        axis.y = -axis.y;

        angleToLight *= Mathf.Deg2Rad;

        int canvasWidth = Screen.width / flareBufferDivision;
        int canvasHeight = Screen.height / flareBufferDivision;

        m_CommandBuffer.GetTemporaryRT(Uniforms._FlareCanvas, canvasWidth, canvasHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        RenderTargetIdentifier screenBufferIdentifier = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);

        m_CommandBuffer.BeginSample("Lens Flares");

        // Set the flare canvas as render target and clear it.
        m_CommandBuffer.SetRenderTarget(Uniforms._FlareCanvas);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.black);

        // Offer both instanced drawing as well as per-ghost draw call
        // But only take the instancing path if instancing is actually supported
        // TODO: Eventually, this branching should be fully automatic
        if (preferInstanced && SystemInfo.supportsInstancing)
        {
            // Set the ghost data
            m_CommandBuffer.SetGlobalBuffer(Uniforms._GhostDataBuffer, ghostDataBuffer);

            // Set the axis on which the ghost should be drawn
            m_CommandBuffer.SetGlobalVector(Uniforms._Axis, axis);
            m_CommandBuffer.SetGlobalFloat(Uniforms._ApertureHeight, apertureHeight);
            m_CommandBuffer.SetGlobalTexture(Uniforms._ApertureTexture, apertureTexture);
            m_CommandBuffer.SetGlobalMatrix(Uniforms._SystemEntranceToAperture, lensSystem.entranceToAperture);
            m_CommandBuffer.SetGlobalVector(Uniforms._LightWavelength, new Vector4(kWavelengthRed, kWavelengthGreen, kWavelengthBlue, antiReflectiveCoatingWavelength));
            m_CommandBuffer.SetGlobalColor(Uniforms._LightColor, mainLight.color);

            Matrix4x4[] matrices = new Matrix4x4[flareGhosts.Count];
            for (int i = 0; i < matrices.Length; ++i)
            {
                matrices[i] = Matrix4x4.identity;
            }

            m_CommandBuffer.DrawMeshInstanced(quad, 0, materialInstanced, 0, matrices, flareGhosts.Count);
        }
        else
        {
            // Drop a draw call per ghost
            foreach (Ghost ghost in flareGhosts)
            {
                // Aperture projected onto the entrance
                float H_e1 = (apertureHeight - ghost.ma.m01 * angleToLight) / ghost.ma.m00;
                float H_e2 = (-apertureHeight - ghost.ma.m01 * angleToLight) / ghost.ma.m00;

                Matrix4x4 msma = ghost.ms * ghost.ma;

                // Map to sensor plane
                float H_p1 = (msma * new Vector4(H_e1, angleToLight, 0f, 0f)).x;
                float H_p2 = (msma * new Vector4(H_e2, angleToLight, 0f, 0f)).x;

                // Project on to image circle
                H_p1 /= k_SensorSize / 2f;
                H_p2 /= k_SensorSize / 2f;

                // Center: How far off of the optical axis the flare is
                // Radius: The size of the quad
                float center = (H_p1 + H_p2) / 2f;
                float radius = Mathf.Abs(H_p1 - H_p2) / 2f;

                // Transform given to the vertex shader to place the quad on screen.
                Matrix4x4 flareTansform = Matrix4x4.Scale(new Vector3(radius, radius * _camera.aspect, 1f));
                flareTansform = Matrix4x4.Translate(new Vector3(axis.x, axis.y, 0f) * center) * flareTansform;

                // entrancePupil = H_a / system.M_a[0][0];
                // Intensity = Square(H_e1 - H_e2) / Square(2 * entrancePupil);
                // Intensity /= Square(2 * Radius);
                // TODO: Check that intensity makes sense
                // TODO: Check that parameters are given correctly
                float entrancePupil = apertureHeight / lensSystem.entranceToAperture.m00;
                float intensity = Mathf.Pow(H_e1 - H_e2, 2f) / Mathf.Pow(2f * entrancePupil, 2f);
                intensity = intensity / Mathf.Pow(2f * radius, 2f);

                intensity = intensity * Mathf.Clamp01(1f - angleToLight);
                // intensity = Mathf.Clamp01(intensity);
                // intensity = .05f;

                // This should be clipped to avoid the critical angle of the lens system.
                // TODO: Find tighter bound for clipping
                float angle = Mathf.Min(.4f, angleToLight);

                // TODO: Figure out what exactly this means.
                float d = antiReflectiveCoatingWavelength / 4.0f / ghost.n1;

                // Check how much red, green and blue light is reflected at the first interface the ray reflects at
                // TODO: Figure out if this is correct, and if the passed parameters are correctly chosen
                Color flareColor = new Color(0, 0, 0, 0);
                flareColor.r = Reflectance(kWavelengthRed, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
                flareColor.g = Reflectance(kWavelengthGreen, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
                flareColor.b = Reflectance(kWavelengthBlue, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
                flareColor *= intensity;

                // Prepare shader
                m_CommandBuffer.SetGlobalFloat(Uniforms._Intensity, intensity);
                m_CommandBuffer.SetGlobalMatrix(Uniforms._FlareTransform, flareTansform);
                m_CommandBuffer.SetGlobalTexture(Uniforms._ApertureTexture, apertureTexture);
                m_CommandBuffer.SetGlobalColor(Uniforms._FlareColor, flareColor);

                // Render quad with the aperture shape to the flare texture
                m_CommandBuffer.DrawMesh(quad, Matrix4x4.identity, material, 0, (int)FlareShaderPasses.DrawGhost);
            }
        }

        // Draw the star-burst
        Vector3 toLight = new Vector3(lightPositionScreenSpace.x, -lightPositionScreenSpace.y, 0f);

        float starburstScale = starburstBaseSize;
        Matrix4x4 starburstTansform = Matrix4x4.Scale(new Vector3(starburstScale, starburstScale * _camera.aspect, 1f));
        starburstTansform = Matrix4x4.Translate(toLight) * starburstTansform;

        m_CommandBuffer.SetGlobalFloat(Uniforms._Intensity, 1f);
        m_CommandBuffer.SetGlobalFloat(Uniforms._AngleToLight, angleToLight);
        m_CommandBuffer.SetGlobalVector(Uniforms._LightColor, mainLight.color);
        m_CommandBuffer.SetGlobalMatrix(Uniforms._FlareTransform, starburstTansform);
        m_CommandBuffer.SetGlobalTexture(Uniforms._ApertureTexture, apertureFourierTransform);

        m_CommandBuffer.DrawMesh(quad, Matrix4x4.identity, material, 0, (int)FlareShaderPasses.DrawStarburst);

        m_CommandBuffer.SetGlobalTexture(Uniforms._FlareTexture, Uniforms._FlareCanvas);

        m_CommandBuffer.Blit(Uniforms._FlareCanvas, screenBufferIdentifier, material, (int)FlareShaderPasses.ComposeOverlay);

        // End and release temporaries
        m_CommandBuffer.ReleaseTemporaryRT(Uniforms._FlareCanvas);
        m_CommandBuffer.EndSample("Lens Flares");
    }
}
