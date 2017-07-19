using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/* This class servers as an extremely crude benchmark
 * function to get an idea of the impact that the lens flares
 * have on the performance on a scene.
 */

public class Benchmark : MonoBehaviour
{
    struct CaptureArgs
    {
        public string name;
        public int samples;
    }

    public string benchmarkName = "Benchmark";

    public int numberOfSamples = 1000;

    bool m_IsCapturing = false;

    public bool isCapturing
    {
        get { return m_IsCapturing; }
    }

    public void Capture()
    {
        CaptureArgs args = new CaptureArgs()
        {
            name = benchmarkName,
            samples = numberOfSamples
        };
        IEnumerator coroutine = CaptureFrameTimes(args);
        StartCoroutine(coroutine);
    }

    IEnumerator CaptureFrameTimes(CaptureArgs args)
    {
        if (m_IsCapturing)
        {
            yield break;
        }

        m_IsCapturing = true;

        float[] timeSamples = new float[args.samples];

        for (int i = 0; i < args.samples; ++i)
        {
            timeSamples[i] = Time.deltaTime;
            yield return null;
        }

        float average = 0f;
        float stdDiv = 0f;
        for (int i = 0; i < args.samples; ++i)
        {
            average += timeSamples[i];
        }

        average /= args.samples;

        float variance = 0f;
        for (int i = 0; i < args.samples; ++i)
        {
            float diff = timeSamples[i] - average;
            variance += (diff * diff) / (numberOfSamples - 1f);
        }

        stdDiv = Mathf.Sqrt(variance);

        float avgMs = average * 1000f;
        float stdDivMs = stdDiv * 1000f;
        string results = args.name + ": average = " + avgMs + "ms, standard deviation = " + stdDivMs + " ms.";

        Debug.Log(results);

        m_IsCapturing = false;
    }
}
