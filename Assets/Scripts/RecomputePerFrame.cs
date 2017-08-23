using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LensFlaresMatrixMethod))]
public class RecomputePerFrame : MonoBehaviour
{

    LensFlaresMatrixMethod m_FlareComponent;

    bool previousPressed = false;
    bool activeMode = false;

    public void OnEnable()
    {
        m_FlareComponent = GetComponent<LensFlaresMatrixMethod>();
    }

    public void Update()
    {
        bool keyPress = Input.GetKeyDown(KeyCode.Joystick1Button0) || Input.GetKeyDown(KeyCode.A);

        if (keyPress)
        {
            if (!previousPressed)
            {
                activeMode = !activeMode;
            }
        }

        previousPressed = keyPress;

        if (activeMode)
        {
            m_FlareComponent.aperatureEdges = 5 + Time.renderedFrameCount % 2;
            m_FlareComponent.Prepare();
        }
    }
}
