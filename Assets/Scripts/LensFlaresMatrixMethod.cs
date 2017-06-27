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
        public static readonly int _AperatureTexture = Shader.PropertyToID("_AperatureTexture");
        public static readonly int _AperatureEdges = Shader.PropertyToID("_AperatureEdges");
        public static readonly int _Smoothing = Shader.PropertyToID("_Smoothing");
        public static readonly int _Intensity = Shader.PropertyToID("_Intensity");
        public static readonly int _LineColor = Shader.PropertyToID("_LineColor");
        public static readonly int _Line = Shader.PropertyToID("_Axis");
        public static readonly int _LightPositionIndicator = Shader.PropertyToID("_LightPositionIndicator");
        public static readonly int _LightPositionColor = Shader.PropertyToID("_LightPositionColor");
    }

    const float refractiveIndexAir = 1.000293f;

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
                return refractiveIndexAir;
            }
            return refractiveIndex;
        }
    }

    public Texture2D aperatureTexture;

    public Lens[] interfacesBeforeAperature;

    public float aparatureHeight = 5;

    public float distanceToNextInterface = 10f;

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
        Matrix4x4 m_BeforeAperature;
        Matrix4x4 m_AfterAperature;

        public Ghost(Matrix4x4 beforeAperature, Matrix4x4 afterAperature)
        {
            m_BeforeAperature = beforeAperature;
            m_AfterAperature = afterAperature;
        }

        public Matrix4x4 ma
        {
            get { return m_BeforeAperature; }
        }

        public Matrix4x4 ms
        {
            get { return m_AfterAperature; }
        }
    }

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

        Debug.Log(axis);
        material.SetVector(Uniforms._Line, axis);
        material.SetVector(Uniforms._LineColor, Color.red);

        Vector4 lightPositionUV = new Vector4(lightPositionScreenSpace.x, lightPositionScreenSpace.y, lightPositionScreenSpace.z, 0f);

        material.SetVector(Uniforms._LightPositionIndicator, lightPositionUV);
        material.SetColor(Uniforms._LightPositionColor, Color.blue);

        Graphics.Blit(source, destination, material, 5);
    }

    void OnPostRender()
    {
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
            refractiveIndex = refractiveIndexAir,
            flat = true,
        };

        Matrix4x4 T0 = Matrix4x4.identity;
        T0[0, 1] = distanceToFirstInterface;

        float previousRefractiveIndex = refractiveIndexAir;
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

                Matrix4x4 entranceToAperature = T0;

                matrixOrder = matrixOrder + "T0";

                // Refract on all interfaces up to j'th interface
                for (int k = 0; k < j; ++k)
                {
                    matrixOrder = "T" + (k+1) + " R" + (k+1) + " " + matrixOrder;
                    entranceToAperature = Translations[k] * Refractions[k] * entranceToAperature;
                }

                // reflect from j'th interface
                matrixOrder = "L" + (j+1) + " " + matrixOrder;
                entranceToAperature = Reflections[j] * entranceToAperature;

                // refract in reverse order from j'th interface back to the i'th interface
                for (int k = j - 1; k > i; --k)
                {
                    matrixOrder = "Ri" + (k+1) + " T" + (k+1) + " " + matrixOrder;
                    entranceToAperature =  RefractionsInverse[k] * Translations[k] * entranceToAperature;
                }

                // refelct on the i'th interface
                matrixOrder = "T" + (i+1) + " " + "Li" + (i+1) + " T" + (i+1) + " " + matrixOrder;
                entranceToAperature = Translations[i] * ReflectionsInverse[i] * Translations[i] * entranceToAperature;

                // refract to the aperature
                for (int k = i + 1; k < interfacesBeforeAperature.Length; ++k)
                {
                    matrixOrder = "T" + (k+1) + " R" + (k+1) + " " + matrixOrder;
                    entranceToAperature = Translations[k] * Refractions[k] * entranceToAperature;
                }

                int aperatureIndex = interfacesBeforeAperature.Length;
                Matrix4x4 aperatureToSensor = Translations[aperatureIndex] * Refractions[aperatureIndex];

                for (int l = 0; l < interfacesAfterAperature.Length; ++l)
                {
                    int index = aperatureIndex + 1 + l;
                    aperatureToSensor = Translations[index] * Refractions[index] * aperatureToSensor;
                }

                Debug.Log(matrixOrder);
                Debug.Log(entranceToAperature);

                flareGhosts.Add(new Ghost(entranceToAperature, aperatureToSensor));
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

        angleToLight *= Mathf.Deg2Rad;

        foreach (var ghost in flareGhosts)
        {
            /*
            Theta_e = acosf(dot(E, L));
            Axis = Project(CameraPos + L * 10000.0).Normalize();
            H_a = GetApertureHeight();
            foreach ghost
                // Aperture projected onto the entrance
                H_e1 = (H_a - ghost.M_a[1][0] * Theta_e) / ghost.M_a[0][0];
                H_e2 = (-H_a - ghost.M_a[1][0] * Theta_e) / ghost.M_a[0][0];

                // Flare quad bounds projected through the system
                H_p1 = ((ghost.M_s * ghost.M_a) * Vector2(H_e1, Theta_e)).x;
                H_p2 = ((ghost.M_s * ghost.M_a) * Vector2(H_e2, Theta_e)).x;

                // Project on to image circle
                H_p1 /= 21.65;
                H_p2 /= 21.65;

                Center = (H_p1 + H_p2) / 2;
                Radius = abs(H_p1 - H_p2) / 2;

                Transform = CreateScale(Radius, Radius * AspectRatio());
                Transform *= CreateTranslation(Axis * Center);

                RenderFlare(Transform);
             */

            Matrix4x4 Ma = ghost.ma;
            Matrix4x4 Ms = ghost.ms;

            // Aperture projected onto the entrance
            float H_e1 = (aparatureHeight - Ma[0, 1] * angleToLight) / Ma[0, 0];
            float H_e2 = (-aparatureHeight - Ma[0, 1] * angleToLight) / Ma[0, 0];

            // Flare quad bounds projected through the system
            float[,] MsMa = new float[2, 2]
            {
                {Ms[0, 0] * Ma[0, 0] + Ms[0, 1] * Ma[1, 0], Ms[0, 0] * Ma[0, 1] + Ms[0, 1] * Ma[1, 1]},
                {Ms[1, 0] * Ma[0, 0] + Ms[1, 1] * Ma[1, 0], Ms[1, 0] * Ma[0, 1] + Ms[1, 1] * Ma[1, 1]},
            };

            // float H_p1 = (MsMa * new Vector2(H_e1, angleToLight)).x;
            // float H_p2 = (MsMa * new Vector2(H_e2, angleToLight)).x;
            Vector2 v1 = new Vector2(H_e1, angleToLight);
            Vector2 v2 = new Vector2(H_e2, angleToLight);

            float H_p1 = MsMa[0, 0] * H_e1 + MsMa[0, 1] * angleToLight;
            float H_p2 = MsMa[0, 0] * H_e2 + MsMa[0, 1] * angleToLight;

            // Project on to image circle
            float sensorSize = 43.3f;
            H_p1 /= sensorSize / 2f;
            H_p2 /= sensorSize / 2f;

            float Center = (H_p1 + H_p2) / 2f;
            float Radius = Mathf.Abs(H_p1 - H_p2) / 2f;

            float entrancePupil = aparatureHeight / Mathf.Abs(Ma[0, 0]);
            float intensity = Mathf.Sqrt(Mathf.Abs(H_e1 - H_e2)) / Mathf.Sqrt(2f * entrancePupil);
            intensity /= Mathf.Sqrt(2f * 16f);

            Vector4 offsetAndScale = new Vector4(0f, 0f, 1f, Screen.height / (float)Screen.width);
            offsetAndScale.z = Radius;

            Vector3 lightForward = mainLight.transform.forward;
            Vector4 lightDirection = new Vector4(lightForward.x, lightForward.y, lightForward.z, 0f);

            Vector2 directionInPlane = _camera.worldToCameraMatrix * lightDirection;
            directionInPlane.Normalize();

            offsetAndScale.x = Center * axis.x;
            offsetAndScale.y = Center * axis.y;

            material.SetInt(Uniforms._AperatureEdges, aperatureGons);

            material.SetFloat(Uniforms._Smoothing, smoothing * .2f);
            material.SetFloat(Uniforms._Intensity, intensity);

            material.SetVector(Uniforms._FlareOffsetAndScale, offsetAndScale);

            material.SetTexture(Uniforms._AperatureTexture, aperatureTexture);

            material.SetPass(4);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity, 4);
        }
        
        // Matrix4x4 myMs = Translations[7] * Refractions[7] * Translations[6] * Refractions[6] * Translations[5] * Refractions[5] * Translations[4];

        // Debug.Log(myMs);

        /*
        float[,] Ms = new float[2, 2]
        {
            {0.4954431883059281f , 33.2568454471298f},
            {-0.002459387653110285f , 1.8533073954767003f},
        };

        float[,] Ma = new float[2, 2]
        {
            {-0.35749219743279453f , 80.06000472093609f},
            {-0.016399658839073523f , 0.8754226422992095f},
        };


        // Aperture projected onto the entrance
        float H_e1 = (aparatureHeight - Ma[0, 1] * angleToLight) / Ma[0, 0];
        float H_e2 = (-aparatureHeight - Ma[0, 1] * angleToLight) / Ma[0, 0];

        // Flare quad bounds projected through the system
        float[,] MsMa = new float[2, 2]
        {
            {Ms[0, 0] * Ma[0, 0] + Ms[0, 1] * Ma[1, 0], Ms[0, 0] * Ma[0, 1] + Ms[0, 1] * Ma[1, 1]},
            {Ms[1, 0] * Ma[0, 0] + Ms[1, 1] * Ma[1, 0], Ms[1, 0] * Ma[1, 1] + Ms[1, 1] * Ma[1, 1]},
        };

        // float H_p1 = (MsMa * new Vector2(H_e1, angleToLight)).x;
        // float H_p2 = (MsMa * new Vector2(H_e2, angleToLight)).x;
        Vector2 v1 = new Vector2(H_e1, angleToLight);
        Vector2 v2 = new Vector2(H_e2, angleToLight);

        float H_p1 = MsMa[0, 0] * v1.x + MsMa[1, 0] * v1.y;
        float H_p2 = MsMa[0, 0] * v2.x + MsMa[1, 0] * v2.y;

        // Project on to image circle
        H_p1 /= 21.65f;
        H_p2 /= 21.65f;

        float Center = (H_p1 + H_p2) / 2;
        float Radius = Mathf.Abs(H_p1 - H_p2) / 2;

        float entrancePupil = aparatureHeight / Mathf.Abs(Ma[0, 0]);
        float intensity = Mathf.Sqrt(Mathf.Abs(H_e1 - H_e2)) / Mathf.Sqrt(2f * entrancePupil);
        intensity /= Mathf.Sqrt(2f * 16f);

        Vector4 offsetAndScale = new Vector4(0f, 0f, 1f, Screen.height / (float)Screen.width);
        offsetAndScale.z = Radius;

        Vector3 lightForward = mainLight.transform.forward;
        Vector4 lightDirection = new Vector4(lightForward.x, lightForward.y, lightForward.z, 0f);

        Vector2 directionInPlane = _camera.worldToCameraMatrix * lightDirection;
        directionInPlane.Normalize();

        offsetAndScale.x = Center * -directionInPlane.x;
        offsetAndScale.y = Center * -directionInPlane.y;

        material.SetInt(Uniforms._AperatureEdges, aperatureGons);

        material.SetFloat(Uniforms._Smoothing, smoothing * .2f);
        material.SetFloat(Uniforms._Intensity, intensity);

        material.SetVector(Uniforms._FlareOffsetAndScale, offsetAndScale);

        material.SetTexture(Uniforms._AperatureTexture, aperatureTexture);

        material.SetPass(4);
        Graphics.DrawMeshNow(quad, Matrix4x4.identity, 4);
        */
    }

    Texture2D GeneratePolygonAparatureTexture(int sides, int resolution, float smoothing)
    {
        RenderTexture aperatureTexture = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);

        // Graphics.Blit(null, aperatureTexture, material, 5);

        Texture2D texture = null;
        return texture;
    }
}
