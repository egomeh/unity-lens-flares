using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using UnityEditor;
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
        public static readonly int _FlareOffsetAndScale = Shader.PropertyToID("_FlareOffsetAndScale");
        public static readonly int _FlareTransform = Shader.PropertyToID("_FlareTransform");
        public static readonly int _AperatureTexture = Shader.PropertyToID("_AperatureTexture");
        public static readonly int _FlareTexture = Shader.PropertyToID("_FlareTexture");
        public static readonly int _AperatureEdges = Shader.PropertyToID("_AperatureEdges");
        public static readonly int _Smoothing = Shader.PropertyToID("_Smoothing");
        public static readonly int _Intensity = Shader.PropertyToID("_Intensity");
        public static readonly int _FlareColor = Shader.PropertyToID("_FlareColor");
        public static readonly int _LineColor = Shader.PropertyToID("_LineColor");
        public static readonly int _Line = Shader.PropertyToID("_Axis");
        public static readonly int _LightPositionIndicator = Shader.PropertyToID("_LightPositionIndicator");
        public static readonly int _LightPositionColor = Shader.PropertyToID("_LightPositionColor");
    }

    const float kRefractiveIndexAir = 1.000293f;

    const float kWavelengthRed = 650f;
    const float kWavelengthGreen = 510f;
    const float kWavelengthBlue = 475f;

    public Light mainLight;

    public float distanceToFirstInterface = 1f;

    [Serializable]
    public class Lens
    {
        public float distanceToNext =  .5f;

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

    public Texture2D aperatureTexture;

    public Lens[] interfacesBeforeAperature;

    public float apertureHeight = 5;

    public float distanceToNextInterface = 10f;

    [Range(1f, 16f)]
    public float fNumber = 1f;

    [Range(4, 10)]
    public int aperatureGons;

    [Range(0f, 1f)]
    public float smoothing;

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

    void OnDisable()
    {
        GraphicsUtils.Destroy(m_Material);
        GraphicsUtils.Destroy(m_Quad);

        m_Quad = null;
        m_Material = null;
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

    /*
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        material.SetColor(Uniforms._LineColor, Color.red);
        material.SetVector(Uniforms._Line, new Vector4());

        Vector2 axis = new Vector2();
        Vector3 lightPositionScreenSpace = new Vector3();

        float angleToLight = 0f;

        if (mainLight.type == LightType.Point)
        {
            Vector3 directionToLight = mainLight.transform.position - _camera.transform.position;
            angleToLight = Vector3.Angle(directionToLight, _camera.transform.forward);

            lightPositionScreenSpace = (_camera.projectionMatrix * _camera.worldToCameraMatrix).MultiplyPoint(mainLight.transform.position);
        }
        else
        {
            angleToLight = Vector3.Angle(-mainLight.transform.forward, _camera.transform.forward);

            Vector3 distantPoint = _camera.transform.position + mainLight.transform.forward * _camera.farClipPlane;

            lightPositionScreenSpace = (_camera.projectionMatrix * _camera.worldToCameraMatrix).MultiplyPoint(distantPoint);
        }

        axis = new Vector4(lightPositionScreenSpace.x, lightPositionScreenSpace.y, 0f, 0f);

        material.SetVector(Uniforms._Line, axis);
        material.SetVector(Uniforms._LineColor, Color.red);

        Vector4 lightPositionUV = new Vector4(lightPositionScreenSpace.x, lightPositionScreenSpace.y, lightPositionScreenSpace.z, 0f);

        material.SetVector(Uniforms._LightPositionIndicator, lightPositionUV);
        material.SetColor(Uniforms._LightPositionColor, Color.blue);

        Graphics.Blit(source, destination, material, 5);
    }
    */

    static float Reflectance(float wavelength, float coatingThickness, float angle, float n1, float n2, float n3)
    {
        // Apply Snell's law to get the other angles
        float angle2 = Mathf.Asin(n1 * Mathf.Asin(angle) / n2);
        float angle3 = Mathf.Asin(n1 * Mathf.Asin(angle) / n3);
 
        float cos1 = Mathf.Cos(angle);
        float cos2 = Mathf.Cos(angle2);
        float cos3 = Mathf.Cos(angle3);
 
        float beta = (2.0f * Mathf.PI) / wavelength * n2 * coatingThickness * cos2;
 
        // Compute the fresnel terms for the first and second interfaces for both s and p polarized
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

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RenderTexture flareTexture = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear, 1);
        flareTexture.name = "Flare textrue";

        // Clear rendertexture, might not be needed.
        Graphics.Blit(null, flareTexture, material, 7);

        int totalInterfaces = interfacesBeforeAperature.Length + interfacesAfterAperature.Length + 1;

        Matrix4x4[] Translations = new Matrix4x4[totalInterfaces];

        Matrix4x4[] Reflections = new Matrix4x4[totalInterfaces];
        Matrix4x4[] ReflectionsInverse = new Matrix4x4[totalInterfaces];

        Matrix4x4[] Refractions = new Matrix4x4[totalInterfaces];
        Matrix4x4[] RefractionsInverse = new Matrix4x4[totalInterfaces];

        Lens[] interfaces = new Lens[totalInterfaces];
        interfacesBeforeAperature.CopyTo(interfaces, 0);
        interfacesAfterAperature.CopyTo(interfaces, interfacesBeforeAperature.Length + 1);

        interfaces[interfacesBeforeAperature.Length] = new Lens()
        {
            distanceToNext = distanceToNextInterface,
            air = true,
            refractiveIndex = kRefractiveIndexAir,
            flat = true,
        };

        Matrix4x4 T0 = Matrix4x4.identity;
        T0[0, 1] = distanceToFirstInterface;

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
                RInv[1, 1] =  refractiveIndex / previousRefractiveIndex;
                // L is just identity
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

        Matrix4x4 SystemEntranceToAperture = T0;

        for (int i = 0; i < interfacesBeforeAperature.Length; ++i)
        {
            SystemEntranceToAperture = Translations[i] * Refractions[i] * SystemEntranceToAperture;
        }

        List<Ghost> flareGhosts = new List<Ghost>();

        for (int i = 0; i < interfacesBeforeAperature.Length - 1; ++i)
        {
            for (int j = i + 1; j < interfacesBeforeAperature.Length; ++j)
            {

                string matrixOrder = "";

                // refract until the j'th interface
                // reflect on j'th interface
                // refract back to i'th interface
                // refract from i'th interface to the aperature
                // Debug.Log("r -> " + j + " <- " + i);

                Matrix4x4 entranceToAperture = T0;

                matrixOrder = matrixOrder + "T0";

                // Refract on all interfaces up to j'th interface
                for (int k = 0; k < j; ++k)
                {
                    matrixOrder = "T" + (k+1) + " R" + (k+1) + " " + matrixOrder;
                    entranceToAperture = Translations[k] * Refractions[k] * entranceToAperture;
                }

                // reflect from j'th interface
                matrixOrder = "L" + (j+1) + " " + matrixOrder;
                entranceToAperture = Reflections[j] * entranceToAperture;

                // refract in reverse order from j'th interface back to the i'th interface
                for (int k = j - 1; k > i; --k)
                {
                    matrixOrder = "Ri" + (k+1) + " T" + (k+1) + " " + matrixOrder;
                    entranceToAperture =  RefractionsInverse[k] * Translations[k] * entranceToAperture;
                }

                // refelct on the i'th interface
                matrixOrder = "T" + (i+1) + " " + "Li" + (i+1) + " T" + (i+1) + " " + matrixOrder;
                entranceToAperture = Translations[i] * ReflectionsInverse[i] * Translations[i] * entranceToAperture;

                // refract to the aperature
                for (int k = i + 1; k < interfacesBeforeAperature.Length; ++k)
                {
                    matrixOrder = "T" + (k+1) + " R" + (k+1) + " " + matrixOrder;
                    entranceToAperture = Translations[k] * Refractions[k] * entranceToAperture;
                }

                int aperatureIndex = interfacesBeforeAperature.Length;
                Matrix4x4 aperatureToSensor = Translations[aperatureIndex] * Refractions[aperatureIndex];

                for (int l = 0; l < interfacesAfterAperature.Length; ++l)
                {
                    int index = aperatureIndex + 1 + l;
                    aperatureToSensor = Translations[index] * Refractions[index] * aperatureToSensor;
                }

                // Debug.Log(matrixOrder);
                // Debug.Log(entranceToAperture);

                float n1 = interfaces[i].air ? kRefractiveIndexAir : interfaces[i].refractiveIndex;
                float n2 = interfaces[i + 1].air ? kRefractiveIndexAir : interfaces[i + 1].refractiveIndex;
                flareGhosts.Add(new Ghost(entranceToAperture, aperatureToSensor, n1, n2));
            }
        }

        Vector3 lightPositionScreenSpace = Vector3.up;

        float angleToLight = 0f;

        if (mainLight.type == LightType.Point)
        {
            Vector3 directionToLight = mainLight.transform.position - _camera.transform.position;
            angleToLight = Vector3.Angle(directionToLight, _camera.transform.forward);
            
            lightPositionScreenSpace = (_camera.projectionMatrix * _camera.worldToCameraMatrix).MultiplyPoint(mainLight.transform.position);
        }
        else
        {
            angleToLight = Vector3.Angle(-mainLight.transform.forward, _camera.transform.forward);

            Vector3 distantPoint = _camera.transform.position + mainLight.transform.forward * _camera.farClipPlane;
            lightPositionScreenSpace = (_camera.projectionMatrix * _camera.worldToCameraMatrix).MultiplyPoint(distantPoint);
        }

        Vector2 axis = new Vector4(lightPositionScreenSpace.x, lightPositionScreenSpace.y);
        axis.Normalize();
        axis.y *= -1f;
        axis.x *= _camera.aspect;

        angleToLight *= Mathf.Deg2Rad;

        foreach (var ghost in flareGhosts)
        {
            // Aperture projected onto the entrance
            float H_e1 = (apertureHeight - ghost.ma[0, 1] * angleToLight) / ghost.ma[0, 0];
            float H_e2 = (-apertureHeight - ghost.ma[0, 1] * angleToLight) / ghost.ma[0, 0];

            float H_p1 = (ghost.ms * ghost.ma * new Vector4(H_e1, angleToLight, 0f, 0f)).x;
            float H_p2 = (ghost.ms * ghost.ma * new Vector4(H_e2, angleToLight, 0f, 0f)).x;

            // Project on to image circle
            float sensorSize = 43.3f;
            H_p1 /= sensorSize / 2f;
            H_p2 /= sensorSize / 2f;

            float center = (H_p1 + H_p2) / 2f;
            float radius = Mathf.Abs(H_p1 - H_p2) / 2f;

            Matrix4x4 flareTansform = Matrix4x4.Scale(new Vector3(radius, radius * _camera.aspect));
            flareTansform *= Matrix4x4.Translate(new Vector3(axis.x, axis.y, 0f) * center);

            /*
                entrancePupil = H_a / system.M_a[0][0];
                Intensity = Sqr(H_e1 - H_e2) / Sqr(2 * entrancePupil);
                Intensity /= Sqr(2 * Radius);
             */
            float entrancePupil = apertureHeight / SystemEntranceToAperture[0, 0];
            float intensity = Mathf.Pow(H_e1 - H_e2, 2f) / Mathf.Pow(2f * entrancePupil, 2f);
            intensity = intensity / (Mathf.Pow(radius, 2f) * sensorSize);
            intensity = intensity * (1f - angleToLight);
            intensity = Mathf.Min(Mathf.Max(.2f, intensity), 1f);

            if (intensity < .05f && radius > .5f)
            {
                //continue;
            }

            Vector4 flareColor = new Vector4(0, 0, 0, 0);
            float coatingThickness = 3f;
            flareColor.w = intensity;
            float angle = .2f;
            float d = 550f / 4.0f / ghost.n1;
            flareColor.x = Reflectance(kWavelengthRed, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
            flareColor.y = Reflectance(kWavelengthGreen, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
            flareColor.z = Reflectance(kWavelengthBlue, d, angle, ghost.n1, Mathf.Max(Mathf.Sqrt(ghost.n1 * ghost.n2), 1.38f), ghost.n2);
            flareColor *= 5f + intensity;

            Debug.Log(intensity);

            material.SetInt(Uniforms._AperatureEdges, aperatureGons);
            material.SetFloat(Uniforms._Smoothing, smoothing * .2f);
            material.SetFloat(Uniforms._Intensity, intensity);
            material.SetMatrix(Uniforms._FlareTransform, flareTansform);
            material.SetTexture(Uniforms._AperatureTexture, aperatureTexture);
            material.SetVector(Uniforms._FlareColor, flareColor);

            Graphics.SetRenderTarget(flareTexture);
            material.SetPass(4);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity, 4);
        }

        Graphics.Blit(flareTexture, source, material, 6);
        
        material.SetTexture(Uniforms._FlareTexture, flareTexture);
        Graphics.Blit(source, destination);

        RenderTexture.ReleaseTemporary(flareTexture);
    }

    Texture2D GeneratePolygonAparatureTexture(int sides, int resolution, float smoothing)
    {
        RenderTexture aperatureTexture = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);

        // Graphics.Blit(null, aperatureTexture, material, 5);

        Texture2D texture = null;
        return texture;
    }
}
