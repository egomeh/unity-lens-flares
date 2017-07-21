using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LensFlaresMatrixMethod))]
public class RecomputePerFrame : MonoBehaviour
{

    LensFlaresMatrixMethod m_FlareComponent;

    public void OnEnable()
    {
        m_FlareComponent = GetComponent<LensFlaresMatrixMethod>();
    }

    public void Update()
    {
        m_FlareComponent.aperatureEdges = 5 + Time.renderedFrameCount % 2;
        m_FlareComponent.Clean();
    }

}
