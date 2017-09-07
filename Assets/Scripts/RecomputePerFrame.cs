using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LensFlares))]
public class RecomputePerFrame : MonoBehaviour
{

    LensFlares m_FlareComponent;

    bool previousPressed = false;
    bool activeMode = false;

    public void OnEnable()
    {
        m_FlareComponent = GetComponent<LensFlares>();
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
            LensFlares.Settings settings = m_FlareComponent.settings;
            settings.apertureEdges = 5 + Time.renderedFrameCount % 2;
            m_FlareComponent.settings = settings;
        }
    }
}
