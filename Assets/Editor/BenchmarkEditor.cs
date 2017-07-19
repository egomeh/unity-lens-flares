using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Benchmark))]
public class BenchmarkEditor : Editor {

    SerializedProperty benchmarkName;
    SerializedProperty numberOfSampled;
    
    void OnEnable()
    {
        benchmarkName = serializedObject.FindProperty("benchmarkName");
        numberOfSampled = serializedObject.FindProperty("numberOfSamples");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Benchmark benchmark = serializedObject.targetObject as Benchmark;

        EditorGUILayout.PropertyField(benchmarkName);
        EditorGUILayout.PropertyField(numberOfSampled);

        if (benchmark.isCapturing)
        {
            GUI.enabled = false;
        }

        if (GUILayout.Button(new GUIContent("Capture")))
        {
            benchmark.Capture();
        }

        GUI.enabled = true;

        serializedObject.ApplyModifiedProperties();
    }

}
