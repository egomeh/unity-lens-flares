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
        Application.CaptureScreenshot(filename);
    }

    [MenuItem("MyTab/Test FFT")]
    public static void TestFFT()
    {
        string imageName = "Image-" + System.DateTime.Now + ".png";
        imageName = imageName.Replace(" ", "-");
        imageName = imageName.Replace("/", "-");
        imageName = imageName.Replace(":", "-");

        string fftName = "FFT-" + System.DateTime.Now + ".png";
        fftName = fftName.Replace(" ", "-");
        fftName = fftName.Replace("/", "-");
        fftName = fftName.Replace(":", "-");

        Texture2D image = new Texture2D(8, 8);
        Color[] pixels = image.GetPixels();
        for (int i = 0; i < pixels.Length; ++i)
        {
            float color = (float)i % 2;
            pixels[i] = new Color(color, color, color, color);
        }
        image.SetPixels(pixels);
        image.Apply();

        Texture2D fft = DFT.ComputeFFTPowerOf2(image);

        byte[] imageBytes = image.EncodeToPNG();
        byte[] fftBytes = fft.EncodeToPNG();

        File.WriteAllBytes(Application.dataPath + "/../" + imageName, imageBytes);
        File.WriteAllBytes(Application.dataPath + "/../" + fftName, fftBytes);
    }
}
