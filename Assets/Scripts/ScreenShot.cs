using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;

public class ScreenShot : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("MyTab/Take Screenshot")]
    public static void TakeScreenshot()
    {
        string filename = "Screenshot-" + System.DateTime.Now + ".png";
        filename = filename.Replace(" ", "-");
        filename = filename.Replace("/", "-");
        filename = filename.Replace(":", "-");
        Debug.Log("Saving screenshota as " + filename);
        ScreenCapture.CaptureScreenshot(filename);
    }
#endif
}
