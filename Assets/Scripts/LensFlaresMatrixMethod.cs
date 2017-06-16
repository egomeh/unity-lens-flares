using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
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


    public Light mainLight;

    [Range(0f, 1f)]
    public float aparatureLocation = .5f;

    public float aparatureHeight = 5;

    public Lens[] lensSystem;

    const int k_MaxPyramidSize = 16;

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

    void OnPostRender()
    {
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

        float angleToLight = 0f;

        if (mainLight.type == LightType.Spot)
        {
            Vector3 directionToLight = mainLight.transform.position - _camera.transform.position;
            angleToLight = Vector3.Angle(directionToLight, _camera.transform.forward);
        }
        else
        {
            angleToLight = Vector3.Angle(-mainLight.transform.forward, _camera.transform.forward);
        }

        angleToLight *= Mathf.Deg2Rad;

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

        Vector4 offsetAndScale = new Vector4(0f, 0f, 1f, Screen.height / (float)Screen.width);
        offsetAndScale.z = Radius;

        Vector3 lightForward = mainLight.transform.forward;
        Vector4 lightDirection = new Vector4(lightForward.x, lightForward.y, lightForward.z, 0f);

        Vector2 directionInPlane = _camera.worldToCameraMatrix * lightDirection;
        directionInPlane.Normalize();

        offsetAndScale.x = Center * -directionInPlane.x;
        offsetAndScale.y = Center * -directionInPlane.y;

        Debug.Log(directionInPlane);

        material.SetVector(Uniforms._FlareOffsetAndScale, offsetAndScale);

        material.SetPass(4);
        Graphics.DrawMeshNow(quad, Matrix4x4.identity, 4);
    }

    Texture2D RenderateRegularPolygonAparatureTexture(int sides, int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.ARGB32, true);
        return texture;
    }
}
