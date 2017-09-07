using System;
using System.Collections.Generic;
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
                UnityEngine.Object.Destroy(obj);
#endif
            }
        }
    }

    static class Uniforms
    {
        // Texture identifiers - Used during rendering of the flares
        public static readonly int _FlareCanvas = Shader.PropertyToID("_FlareCanvas");
        public static readonly int _ApertureTexture = Shader.PropertyToID("_ApertureTexture");
        public static readonly int _ApertureFFTTexture = Shader.PropertyToID("_ApertureFFTTexture");
        public static readonly int _StarburstTransform = Shader.PropertyToID("_StarburstTransform");

        // Uniforms related to drawing individual flares
        public static readonly int _FlareColor = Shader.PropertyToID("_FlareColor");
        public static readonly int _LightColor = Shader.PropertyToID("_LightColor");
        public static readonly int _Axis = Shader.PropertyToID("_Axis");
        public static readonly int _SystemEntranceToAperture = Shader.PropertyToID("_SystemEntranceToAperture");
        public static readonly int _ApertureHeight = Shader.PropertyToID("_ApertureHeight");
        public static readonly int _CenterRadiusLightOffset = Shader.PropertyToID("_CenterRadiusLightOffset");
        public static readonly int _EntranceCenterRadius = Shader.PropertyToID("_EntranceCenterRadius");

        // Occlusion related uniforms
        public static readonly int _VisibilityBuffer = Shader.PropertyToID("_VisibilityBuffer");
        public static readonly int _OcclusionFactor = Shader.PropertyToID("_OcclusionFactor");

        // Parameters used when drawing the Aperture SDF texture
        public static readonly int _ApertureEdges = Shader.PropertyToID("_ApertureEdges");
        public static readonly int _Smoothing = Shader.PropertyToID("_Smoothing");

        // Parameters used during FFT computation
        public static readonly int _PassParameters = Shader.PropertyToID("_PassParameters");

        // Decides which direction a Gaussian blur pass occurs in
        public static readonly int _BlurDirection = Shader.PropertyToID("_BlurDirection");

        // Texture identifiers - Used during prepare phase for computing the FFT
        public static readonly int _TextureSourceR = Shader.PropertyToID("TextureSourceR");
        public static readonly int _TextureSourceI = Shader.PropertyToID("TextureSourceI");
        public static readonly int _TextureTargetR = Shader.PropertyToID("TextureTargetR");
        public static readonly int _TextureTargetI = Shader.PropertyToID("TextureTargetI");
        public static readonly int _Real = Shader.PropertyToID("_Real");
        public static readonly int _Imaginary = Shader.PropertyToID("_Imaginary");
    }

    enum FlareShaderPasses
    {
        DrawGhost = 0,
        DrawApertureShape = 2,
        GaussianBlur = 3,
        DrawStarburst = 4,
        CenterScaleFade = 5,
        ComposeOverlay = 6,
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

    [Serializable]
    public class LightSettings
    {
        public Light light;

        public float occlusionDiskSize;

        public float occlusionTimeDelay;

        internal float occlusionTimer;

        public LightSettings()
        {
            light = null;
            occlusionDiskSize = 0.01f;
            occlusionTimeDelay = 0.2f;
            occlusionTimer = 1f;
        }
    }

    const float kRefractiveIndexAir = 1.000293f;

    const float kWavelengthRed = 700f;
    const float kWavelengthGreen = 510f;
    const float kWavelengthBlue = 450f;

    // Resolution of the aperture and aperture transform is 1024x1024
    // TODO: Maybe a better resolution would be better?
    const int kApertureResolution = 256;

    const CameraEvent kEventHook = CameraEvent.AfterImageEffects;

    // Compute shader to compute the FFT of the aperture
    ComputeShader m_FourierTransformShader;

    ComputeShader fourierTransformShader
    {
        get
        {
            if (!m_FourierTransformShader)
            {
                m_FourierTransformShader = Resources.Load<ComputeShader>("Shaders/FFT");
            }

            return m_FourierTransformShader;
        }
    }

    // Shader to check if pixels in the depth buffer exceed certain value
    ComputeShader m_OcclusionQueryShader;

    ComputeShader occlusionQueryShader
    {
        get
        {
            if (!m_OcclusionQueryShader)
            {
                m_OcclusionQueryShader = Resources.Load<ComputeShader>("Shaders/Occlusionquery");
            }

            return m_OcclusionQueryShader;
        }
    }

    public Lens[] interfacesBeforeAperature;

    public float distanceToNextInterface = 10f;

    public Lens[] interfacesAfterAperature;

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

    Shader m_Shader;

    Shader shader
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

    Material m_Material;

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

    RenderTexture m_ApertureTexture;
    RenderTexture apertureTexture
    {
        get
        {
            if (!m_ApertureTexture)
            {
                Prepare();
            }

            return m_ApertureTexture;
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

    List<Ghost> m_FlareGhosts;

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

    ComputeBuffer m_VisibilityBuffer;

    ComputeBuffer visibilityBuffer
    {
        get
        {
            if (m_VisibilityBuffer == null)
            {
                m_VisibilityBuffer = new ComputeBuffer(settings.lights.Length * 2, sizeof(uint));
            }

            return m_VisibilityBuffer;
        }
    }

    CommandBuffer m_CommandBuffer;

    [Serializable]
    public struct Settings
    {
        [Range(1f, 32f)]
        public float aperture;

        [Range(1, 8)]
        public int flareBufferDivision;

        [Range(5, 9)]
        public int apertureEdges;

        [Range(0f, 1f)]
        public float smoothing;

        [Range(0f, 1f)]
        public float starburstBaseSize;

        [Range(300f, 1000f)]
        public float antiReflectiveCoatingWavelength;

        public float entranceHeight;

        public bool entranceClipping;

        public bool disallowComputeShaders;

        public LightSettings[] lights;

        public static Settings defaultSettings
        {
            get
            {
                return new Settings
                {
                    aperture = 1f,
                    flareBufferDivision = 1,
                    apertureEdges = 5,
                    smoothing = 0f,
                    starburstBaseSize = .2f,
                    antiReflectiveCoatingWavelength = 450f,
                    entranceClipping = false,
                    disallowComputeShaders = false,
                    entranceHeight = 10f,
                    lights = {},
                };
            }
        }
    }

    bool m_SettingsDirty;

    [SerializeField]
    Settings m_Settings = Settings.defaultSettings;
    public Settings settings
    {
        get { return m_Settings; }
        set
        {
            m_Settings = value;
            m_SettingsDirty = true;
        }
    }

    bool useComputeShaders
    {
        get { return SystemInfo.supportsComputeShaders && !settings.disallowComputeShaders; }
    }

    public void Clean()
    {
        GraphicsUtils.Destroy(m_Material);
        GraphicsUtils.Destroy(m_MaterialInstanced);
        GraphicsUtils.Destroy(m_Quad);
        GraphicsUtils.Destroy(m_ApertureTexture);
        GraphicsUtils.Destroy(m_ApertureFourierTransform);

        if (m_CommandBuffer != null)
        {
            _camera.RemoveCommandBuffer(kEventHook, m_CommandBuffer);
            m_CommandBuffer.Dispose();
            m_CommandBuffer = null;
        }

        m_Quad = null;
        m_Material = null;
        m_MaterialInstanced = null;
        m_ApertureTexture = null;
        m_ApertureFourierTransform = null;
        m_FlareGhosts = null;
        m_LensSystem = null;
        m_CommandBuffer = null;
    }

    void OnEnable()
    {
        _camera.depthTextureMode |= DepthTextureMode.Depth;
        m_SettingsDirty = true;
    }

    void OnDisable()
    {
        m_SettingsDirty = true;
        Clean();
    }

    void OnValidate()
    {
        m_SettingsDirty = true;

        foreach (LightSettings lightSetting in settings.lights)
        {
            lightSetting.occlusionDiskSize = Mathf.Max(0.01f, lightSetting.occlusionDiskSize);
            lightSetting.occlusionTimeDelay = Mathf.Max(0.01f, lightSetting.occlusionTimeDelay);
        }
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

    public void Prepare()
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

        // For entrance clipping, T[0, 1] should be the distance from entrance to the first interface
        T0[0, 1] = 0f;

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
        if (m_ApertureTexture)
        {
            m_ApertureTexture.Release();
            GraphicsUtils.Destroy(m_ApertureTexture);
            m_ApertureTexture = null;
        }

        m_ApertureTexture = new RenderTexture(kApertureResolution, kApertureResolution, 0, RenderTextureFormat.ARGB32);
        RenderTexture temporary = new RenderTexture(kApertureResolution, kApertureResolution, 0, RenderTextureFormat.ARGB32);

        m_ApertureTexture.filterMode = FilterMode.Bilinear;
        m_ApertureTexture.useMipMap = true;
        temporary.filterMode = FilterMode.Bilinear;

        temporary.Create();
        m_ApertureTexture.Create();

        // Draw the aperture shape as a signed distance field
        material.SetInt(Uniforms._ApertureEdges, settings.apertureEdges);
        material.SetFloat(Uniforms._Smoothing, settings.smoothing);

        Graphics.Blit(null, m_ApertureTexture, material, (int)FlareShaderPasses.DrawApertureShape);

        // *** Step 5 ***

        if (m_ApertureFourierTransform)
        {
            m_ApertureFourierTransform.Release();
            GraphicsUtils.Destroy(m_ApertureFourierTransform);
            m_ApertureFourierTransform = null;
        }

        if (useComputeShaders)
        {
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

            int starburstKernel = fourierTransformShader.FindKernel("ButterflySLM");

            int butterflyCount = (int)(Mathf.Log(kApertureResolution, 2f) / Mathf.Log(2f, 2f));

            fourierTransformShader.SetInts(Uniforms._PassParameters, butterflyCount, 1, 0, 0);
        
            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureSourceR, m_ApertureTexture);
            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureSourceI, fftTextures[3]);
            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureTargetR, fftTextures[0]);
            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureTargetI, fftTextures[1]);
            fourierTransformShader.Dispatch(starburstKernel, 1, kApertureResolution, 1);

            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureSourceR, fftTextures[0]);
            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureSourceI, fftTextures[1]);
            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureTargetR, fftTextures[2]);
            fourierTransformShader.SetTexture(starburstKernel, Uniforms._TextureTargetI, fftTextures[3]);

            fourierTransformShader.SetInts(Uniforms._PassParameters, new int[4] {butterflyCount, 0, 0, 0});
            fourierTransformShader.Dispatch(starburstKernel, 1, kApertureResolution, 1);

            material.SetTexture(Uniforms._Real, fftTextures[2]);
            material.SetTexture(Uniforms._Imaginary, fftTextures[3]);
            Graphics.Blit(null, m_ApertureFourierTransform, material, (int)FlareShaderPasses.CenterScaleFade);

             // Blur the Fourier transform of the aperture slightly
            material.SetVector(Uniforms._BlurDirection, new Vector2(1f, 0f));
            Graphics.Blit(m_ApertureFourierTransform, fftTextures[4], material, (int)FlareShaderPasses.GaussianBlur);

            material.SetVector(Uniforms._BlurDirection, new Vector2(0f, 1f));
            Graphics.Blit(fftTextures[4], m_ApertureFourierTransform, material, (int)FlareShaderPasses.GaussianBlur);

            for (int i = 0; i < fftTextureCount; ++i)
            {
                fftTextures[i].Release();
                fftTextures[i] = null;
            }
        }

        // Blur the aperture texture
        // But maybe get heavier blur rather than run the same blur many times
        material.SetVector(Uniforms._BlurDirection, new Vector2(1f, 0f));
        Graphics.Blit(m_ApertureTexture, temporary, material, (int)FlareShaderPasses.GaussianBlur);

        material.SetVector(Uniforms._BlurDirection, new Vector2(0f, 1f));
        Graphics.Blit(temporary, m_ApertureTexture, material, (int)FlareShaderPasses.GaussianBlur);

        temporary.Release();

        if (m_VisibilityBuffer != null)
        {
            if (m_VisibilityBuffer.count != settings.lights.Length * 2)
            {
                m_VisibilityBuffer.Dispose();
                m_VisibilityBuffer = null;
            }
        }

        m_SettingsDirty = false;
    }

    [ExecuteInEditMode]
    void OnPreRender()
    {
        if (m_SettingsDirty)
        {
            Prepare();
        }

        if (m_CommandBuffer == null)
        {
            m_CommandBuffer = new CommandBuffer();
            m_CommandBuffer.name = "Lens flares";

            _camera.AddCommandBuffer(kEventHook, m_CommandBuffer);
        }

        // Make sure the command buffer is empty
        m_CommandBuffer.Clear();

        m_CommandBuffer.BeginSample("Lens Flares");

        // Prepare common values for all lights
        const float k_FilmHeight = 24f;
        const float k_SensorSize = 43.2f;

        float fov = _camera.fieldOfView * Mathf.Deg2Rad;
        float focalLenghth = .5f * k_FilmHeight / Mathf.Tan(.5f * fov);

        float apertureHeight = focalLenghth / settings.aperture;

        int canvasWidth = Screen.width / (settings.flareBufferDivision + 1);
        int canvasHeight = Screen.height / (settings.flareBufferDivision + 1);

        m_CommandBuffer.GetTemporaryRT(Uniforms._FlareCanvas, canvasWidth, canvasHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        RenderTargetIdentifier screenBufferIdentifier = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);

        // Set the flare canvas as render target and clear it.
        m_CommandBuffer.SetRenderTarget(Uniforms._FlareCanvas);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.black);

        int drawFlarePass = (int)FlareShaderPasses.DrawGhost;
        if (settings.entranceClipping)
        {
            drawFlarePass += 1;
        }

        if (useComputeShaders)
        {
            material.EnableKeyword("COMPUTE_OCCLUSION_QUERY");

            visibilityBuffer.SetData(new uint[2 * settings.lights.Length]);

            material.SetTexture(Uniforms._ApertureFFTTexture, apertureFourierTransform);
            material.SetBuffer(Uniforms._VisibilityBuffer, visibilityBuffer);
        }
        else
        {
            material.DisableKeyword("COMPUTE_OCCLUSION_QUERY");
        }

        for (int i = 0; i < settings.lights.Length; ++i)
        {
            Light light = settings.lights[i].light;
            float occlusionDiskSize = settings.lights[i].occlusionDiskSize;

            if (!light)
            {
                continue;
            }

            // Light position in NDC
            Vector4 lightPositionScreenSpace = new Vector4(0f, 0f, 0f, 0f);

            // The angle between the light direction and the camera forward direction
            float angleToLight;

            float lightDepth = 0f;
            float lightAttenuation = 1f;
            float uvSampleRadius;
            Vector3 directionToLight;
            float distanceToLight;
            if (light.type == LightType.Point)
            {
                directionToLight = (light.transform.position - _camera.transform.position);
                distanceToLight = directionToLight.magnitude;

                angleToLight = Vector3.Angle(directionToLight.normalized, _camera.transform.forward);

                Vector3 lightPosition = new Vector4(light.transform.position.x, light.transform.position.y, light.transform.position.z, 1f);

                Matrix4x4 viewProjection = (_camera.projectionMatrix * _camera.worldToCameraMatrix);

                lightPositionScreenSpace = viewProjection.MultiplyPoint(lightPosition);

                float a = _camera.farClipPlane / (_camera.farClipPlane - _camera.nearClipPlane);
                float b = _camera.farClipPlane * _camera.nearClipPlane / (_camera.nearClipPlane - _camera.farClipPlane);
                lightDepth = 1f - (a + b / directionToLight.magnitude);

                float w = lightPosition.x * viewProjection.m30 + lightPosition.y * viewProjection.m31 + lightPosition.z * viewProjection.m32 + viewProjection.m33;

                uvSampleRadius = occlusionDiskSize / w;

                float normalizedDistanceToLight = Mathf.Clamp01(directionToLight.magnitude / light.range);
                lightAttenuation = 1f / (1f + 25f * normalizedDistanceToLight * normalizedDistanceToLight);
            }
            else if (light.type == LightType.Spot)
            {
                directionToLight = (light.transform.position - _camera.transform.position);
                distanceToLight = directionToLight.magnitude;

                angleToLight = Vector3.Angle(directionToLight.normalized, _camera.transform.forward);
                float angleToLightDirection = Vector3.Angle(-light.transform.forward, directionToLight);

                Vector3 lightPosition = new Vector4(light.transform.position.x, light.transform.position.y, light.transform.position.z, 1f);

                Matrix4x4 viewProjection = (_camera.projectionMatrix * _camera.worldToCameraMatrix);

                lightPositionScreenSpace = viewProjection.MultiplyPoint(lightPosition);

                float a = _camera.farClipPlane / (_camera.farClipPlane - _camera.nearClipPlane);
                float b = _camera.farClipPlane * _camera.nearClipPlane / (_camera.nearClipPlane - _camera.farClipPlane);
                lightDepth = 1f - (a + b / directionToLight.magnitude);

                float w = lightPosition.x * viewProjection.m30 + lightPosition.y * viewProjection.m31 + lightPosition.z * viewProjection.m32 + viewProjection.m33;

                uvSampleRadius = occlusionDiskSize / w;

                float normalizedDistanceToLight = Mathf.Clamp01(directionToLight.magnitude / light.range);
                lightAttenuation = 1f / (1f + 25f * normalizedDistanceToLight * normalizedDistanceToLight);

                float coneEdgeProximity = Mathf.SmoothStep(0f, 1f, (light.spotAngle * .5f - angleToLightDirection) + .5f);
                lightAttenuation *= coneEdgeProximity;
            }
            else if (light.type == LightType.Directional)
            {
                directionToLight = -light.transform.forward.normalized;
                distanceToLight = _camera.farClipPlane;
                angleToLight = Vector3.Angle(-light.transform.forward, _camera.transform.forward);

                Vector3 distantPoint = _camera.transform.position + light.transform.forward * _camera.farClipPlane;
                lightPositionScreenSpace = (_camera.projectionMatrix * _camera.worldToCameraMatrix).MultiplyPoint(distantPoint);

                uvSampleRadius = occlusionDiskSize;
                lightAttenuation = 1f;
            }
            else
            {
                // If the light is not point, directional or spot light, skip it.
                continue;
            }

            // If the camera is looking away from the light, or the light is too dim skip it.
            if (angleToLight > 90f)
            {
                continue;
            }

            // Axis in screen space that intersects the center of the screen and the light projected.
            // in screen in NDC
            Vector2 axis = new Vector2(lightPositionScreenSpace.x, lightPositionScreenSpace.y);
            axis.Normalize();
            axis.y = -axis.y;

            angleToLight *= Mathf.Deg2Rad;

            if (useComputeShaders)
            {
                // Resolve occlusion
                int occlusionQueryKernel = occlusionQueryShader.FindKernel("OcclusionQuery");

                float sampleRadiusPixels = Mathf.Max(uvSampleRadius * Screen.width, 7f);

                int occlusionSamples = 2 * Mathf.CeilToInt(Mathf.Pow(2f, Mathf.Ceil(Mathf.Log(sampleRadiusPixels) / Mathf.Log(2))));

                // x, y: light in 0-1 UV coordinates, z: light depth
                Vector4 lightParams = new Vector4(lightPositionScreenSpace.x, lightPositionScreenSpace.y, lightDepth, 0f);

                // x: sample radius, y: number of samples in each dimension, z: The offset into the occlusion buffer
                Vector4 occlusionSamplingParams = new Vector4(uvSampleRadius, occlusionSamples, i * 2, 0f);

                // x: width, y: height, z: width / height
                Vector4 dimensionParams = new Vector4(Screen.width, Screen.height, _camera.aspect, 0f);

                // m_CommandBuffer.SetComputeTextureParam(occlusionQueryShader, occlusionQueryKernel, "_DebugTexture", debugTexture);
                m_CommandBuffer.SetComputeBufferParam(occlusionQueryShader, occlusionQueryKernel, "_Visibility", visibilityBuffer);

                m_CommandBuffer.SetComputeTextureParam(occlusionQueryShader, occlusionQueryKernel, "_CameraDepthTexture", BuiltinRenderTextureType.ResolvedDepth);
                m_CommandBuffer.SetComputeTextureParam(occlusionQueryShader, occlusionQueryKernel, "_FlareTexture", Uniforms._FlareCanvas);
                m_CommandBuffer.SetComputeVectorParam(occlusionQueryShader, "_DepthTextureDimensions", dimensionParams);
                m_CommandBuffer.SetComputeVectorParam(occlusionQueryShader, "_LightPosition", lightParams);
                m_CommandBuffer.SetComputeVectorParam(occlusionQueryShader, "_SamplingParams", occlusionSamplingParams);

                m_CommandBuffer.DispatchCompute(occlusionQueryShader, occlusionQueryKernel, occlusionSamples / 8, occlusionSamples / 8, 1);
            }
            else
            {
                // The how much to change the occlusion timer
                float occlusionTimerDelta = Time.deltaTime / settings.lights[i].occlusionTimeDelay;

                // Check if the light is occluded by an obstacle
                if (Physics.Raycast(transform.position, directionToLight, distanceToLight))
                {
                    settings.lights[i].occlusionTimer -= occlusionTimerDelta;
                }
                else
                {
                    settings.lights[i].occlusionTimer += occlusionTimerDelta;
                }

                // Make sure the timer stays in [0;1] range
                settings.lights[i].occlusionTimer = Mathf.Clamp01(settings.lights[i].occlusionTimer);

                m_CommandBuffer.SetGlobalFloat(Uniforms._OcclusionFactor, settings.lights[i].occlusionTimer);
            }

            // This should be clipped to avoid the critical angle of the lens system.
            // TODO: Find tighter bound for clipping
            float angle = Mathf.Min(.4f, angleToLight);

            // Set all uniforms that are shared among all flares.
            material.SetTexture(Uniforms._ApertureTexture, apertureTexture);
            material.SetMatrix(Uniforms._SystemEntranceToAperture, lensSystem.entranceToAperture);

            Color lightColor = light.color;
            lightColor.a = light.intensity * lightAttenuation;
            m_CommandBuffer.SetGlobalColor(Uniforms._LightColor, lightColor);
            m_CommandBuffer.SetGlobalFloat(Uniforms._ApertureHeight, apertureHeight);
            m_CommandBuffer.SetGlobalVector(Uniforms._Axis, axis);

            float entrancePupil = apertureHeight / lensSystem.entranceToAperture.m00;

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

                // entrancePupil = H_a / system.M_a[0][0];
                // Intensity = Square(H_e1 - H_e2) / Square(2 * entrancePupil);
                // Intensity /= Square(2 * Radius);
                // TODO: Check that intensity makes sense
                // TODO: Check that parameters are given correctly
                float intensity = Mathf.Pow(H_e1 - H_e2, 2f) / Mathf.Pow(2f * entrancePupil, 2f);

                // This line should be physically correct, but intensities reach far too high values. 
                // intensity = intensity / Mathf.Pow(2f * radius, 2f);

                // This line is gives better intensity ranges, but does not appear correct in a physical context.
                intensity = intensity / Mathf.Pow(2f * radius * settings.aperture, 2f);

                intensity = Mathf.Min(intensity * Mathf.Clamp01(1f - angleToLight), 100f);

                // TODO: Figure out what exactly this means.
                float d = settings.antiReflectiveCoatingWavelength / 4f / ghost.n1;

                // Check how much red, green and blue light is reflected at the first interface the ray reflects at
                // TODO: Figure out if this is correct, and if the passed parameters are correctly chosen
                Color flareColor = new Color(0, 0, 0, 0);
                flareColor.r = Reflectance(kWavelengthRed, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
                flareColor.g = Reflectance(kWavelengthGreen, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
                flareColor.b = Reflectance(kWavelengthBlue, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
                flareColor *= intensity * light.color * light.intensity * lightAttenuation;

                // If the ghost contributes less than a certain threshold, do not draw it
                if (flareColor.maxColorComponent < 2e-3f)
                {
                    continue;
                }

                // If the ghost is smaller than 1e-3 it's not likely to be seen
                if (radius < 1e-3)
                {
                    continue;
                }

                if (settings.entranceClipping)
                {
                    float E_a1 = (ghost.ma * new Vector4(settings.entranceHeight, angleToLight)).x;
                    float E_a2 = (ghost.ma * new Vector4(-settings.entranceHeight, angleToLight)).x;

                    float cnterEntrance = ((E_a1 + E_a2) * 0.5f) / settings.entranceHeight - center;
                    float radiusEntance = (Mathf.Abs(E_a1 - E_a2) * 0.5f) / apertureHeight;

                    Vector4 entranceCenterRadius = new Vector4(cnterEntrance, radiusEntance, 0f, 0f);

                    m_CommandBuffer.SetGlobalVector(Uniforms._EntranceCenterRadius, entranceCenterRadius);
                }

                // Prepare shader
                m_CommandBuffer.SetGlobalColor(Uniforms._FlareColor, flareColor);
                m_CommandBuffer.SetGlobalVector(Uniforms._CenterRadiusLightOffset, new Vector4(center, radius, i * 2, 0f));

                // Render quad with the aperture shape to the flare texture
                m_CommandBuffer.DrawMesh(quad, Matrix4x4.identity, material, 0, drawFlarePass);
            }
            
            // If the system supports compute shaders, the FFT texture was generated and can be drawn
            // If not, the FFT texture is not available, and will not be drawn
            if (useComputeShaders)
            {
                // Draw the star-burst
                Vector3 toLight = new Vector3(lightPositionScreenSpace.x, -lightPositionScreenSpace.y, 0f);

                float starburstScale = settings.starburstBaseSize;
                Matrix4x4 starburstTansform = Matrix4x4.Scale(new Vector3(starburstScale, starburstScale * _camera.aspect, 1f));
                starburstTansform = Matrix4x4.Translate(toLight) * starburstTansform;

                m_CommandBuffer.SetGlobalMatrix(Uniforms._StarburstTransform, starburstTansform);

                // Draw the starburst
                m_CommandBuffer.DrawMesh(quad, Matrix4x4.identity, material, 0, (int)FlareShaderPasses.DrawStarburst);
            }
        }

        // Draw the flare canvas onto the screen
        // m_CommandBuffer.SetGlobalTexture(Uniforms._FlareTexture, Uniforms._FlareCanvas);
        m_CommandBuffer.Blit(Uniforms._FlareCanvas, screenBufferIdentifier, material, (int)FlareShaderPasses.ComposeOverlay);

        // End and release temporaries
        m_CommandBuffer.ReleaseTemporaryRT(Uniforms._FlareCanvas);
        m_CommandBuffer.EndSample("Lens Flares");
    }
}
