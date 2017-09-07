using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FreeFlight : MonoBehaviour
{
    Camera m_Camera;

    Vector3 m_Look;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        m_Camera = GetComponent<Camera>();

        m_Look = m_Camera.transform.rotation.eulerAngles;
    }

    void Update()
    {
        float horizontalMovement = Input.GetAxis("Mouse X");
        float verticalMovement = -Input.GetAxis("Mouse Y");

        m_Look += new Vector3(verticalMovement, horizontalMovement, 0f);

        transform.rotation = Quaternion.Euler(m_Look);

        float forwardMovement = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        float rightMovement = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);

        m_Camera.transform.position += m_Camera.transform.forward * .05f * forwardMovement;
        m_Camera.transform.position += m_Camera.transform.right * .05f * rightMovement;
    }
}
