using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions.Comparers;

public class DFT
{
    public static Texture2D ComputeFFT(Texture2D image, bool convertToGray = false)
    {
        Texture2D spectrum = new Texture2D(image.width, image.height);

        Color[] pixels = image.GetPixels();
        float[] intensities = new float[pixels.Length];

        float[] spectrumPixelsReal = new float[pixels.Length];
        float[] spectrumPixelsImaginary = new float[pixels.Length];

        for (int i = 0; i < pixels.Length; ++i)
        {
            intensities[i] = pixels[i].grayscale;
        }

        for (int i = 0; i < pixels.Length; ++i)
        {
            int sourceX = i % image.width;
            int sourceY = i / image.height;

            float realPart = 0f;
            float imaginaryPart = 0f;

            float fWidth = image.width;
            float fHeight = image.height;

            for (int y = 0; y < image.height; ++y)
            {
                for (int x = 0; x < image.width; ++x)
                {
                    int linearIndex = y * image.width + x;
                    float sourceIntensity = intensities[linearIndex];

                    float fx = x;
                    float fy = y;

                    float angle = 2f * Mathf.PI * ((fx / fWidth) * sourceX + (fy / fHeight) * sourceY);

                    realPart += sourceIntensity * Mathf.Cos(angle);
                    imaginaryPart -= sourceIntensity * Mathf.Sin(angle);
                }
            }

            spectrumPixelsReal[i] = realPart;
            spectrumPixelsImaginary[i] = imaginaryPart;
        }

        Color[] spectrumPixels = new Color[pixels.Length];

        for (int i = 0; i < pixels.Length; ++i)
        {
            float power = Mathf.Sqrt(spectrumPixelsReal[i] * spectrumPixelsImaginary[i]);
            spectrumPixels[i] = new Color(power, power, power, 1f);
        }

        spectrum.SetPixels(spectrumPixels);
        spectrum.Apply();

        return spectrum;
    }

    public static Texture2D ComputeFFTSeperate(Texture2D image, bool convertToGray = false)
    {
        Texture2D spectrum = new Texture2D(image.width, image.height);

        Color[] pixels = image.GetPixels();
        float[] intensities = new float[pixels.Length];

        float[] tempPixelsReal = new float[pixels.Length];
        float[] tempPixelsImaginary = new float[pixels.Length];

        float[] spectrumPixelsReal = new float[pixels.Length];
        float[] spectrumPixelsImaginary = new float[pixels.Length];

        for (int i = 0; i < pixels.Length; ++i)
        {
            intensities[i] = pixels[i].grayscale;
        }

        // Naive DFT on each row
        for (int v = 0; v < image.height; ++v)
        {
            for (int u = 0; u < image.width; ++u)
            {
                float realPart = 0f;
                float imaginaryPart = 0f;

                for (int x = 0; x < image.width; ++x)
                {
                    float sourceIntensity = intensities[v * image.width + x];

                    float angle = 2f * Mathf.PI * x * u / image.width;

                    realPart += sourceIntensity * Mathf.Cos(angle);
                    imaginaryPart += -sourceIntensity * Mathf.Sin(angle);
                }

                int uvIndex = v * image.width + u;

                tempPixelsReal[uvIndex] = realPart;
                tempPixelsImaginary[uvIndex] = imaginaryPart;
            }
        }

        // Naive DFT on each column
        for (int v = 0; v < image.height; ++v)
        {
            for (int u = 0; u < image.width; ++u)
            {
                float realPart = 0f;
                float imaginaryPart = 0f;

                for (int y = 0; y < image.height; ++y)
                {
                    int sourceIndex = y * image.width + u;

                    float angle = 2f * Mathf.PI * y * v / image.height;

                    realPart += tempPixelsReal[sourceIndex] * Mathf.Cos(angle) + tempPixelsImaginary[sourceIndex] * Mathf.Sin(angle);
                    imaginaryPart += -tempPixelsReal[sourceIndex] * Mathf.Sin(angle) + tempPixelsImaginary[sourceIndex] * Mathf.Cos(angle);
                }

                int uvIndex = v * image.width + u;

                spectrumPixelsReal[uvIndex] = realPart;
                spectrumPixelsImaginary[uvIndex] = imaginaryPart;
            }
        }

        Color[] spectrumPixels = new Color[pixels.Length];

        for (int i = 0; i < pixels.Length; ++i)
        {
            int x = i % image.width;
            int y = i / image.width;

            x = (x + image.width / 2) % image.width;
            y = (y + image.height / 2) % image.height;

            int shiftedIndex = image.width * y + x;

            float real = spectrumPixelsReal[shiftedIndex];
            float imaginary = spectrumPixelsImaginary[shiftedIndex];
            float power = Mathf.Sqrt(real * real + imaginary * imaginary);
            spectrumPixels[i] = new Color(power, power, power, 1f);
        }

        spectrum.SetPixels(spectrumPixels);
        spectrum.Apply();

        return spectrum;
    }
}
