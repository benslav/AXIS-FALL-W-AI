using GravityPortals;
using UnityEditor;
using UnityEngine;

/// <summary>Rebuild and check buttons on the TesseractSettings asset, next to the sizes they use.</summary>
[CustomEditor(typeof(TesseractSettings))]
public class TesseractSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("The map is built from these sizes. Change them, then rebuild (this overwrites the scene).", MessageType.Info);
        if (GUILayout.Button("Rebuild The Tesseract"))
        {
            AssetDatabase.SaveAssets();
            EditorApplication.delayCall += TesseractBuilder.Build;
        }
        if (GUILayout.Button("List Doors and Check")) TesseractBuilder.ListDoors();
    }
}
