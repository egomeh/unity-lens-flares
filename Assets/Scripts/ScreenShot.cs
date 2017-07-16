using UnityEngine;
using UnityEditor;
using System.IO;

public class ScreenShot : MonoBehaviour
{
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
}
