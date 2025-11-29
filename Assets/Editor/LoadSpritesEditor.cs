#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LoadSprites))]
public class LoadSpritesEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var myTarget = (LoadSprites)target;
        if (GUILayout.Button("Rebuild From Resources"))
        {
            myTarget.RebuildFromResources();
            EditorUtility.SetDirty(myTarget);
        }
    }
}
#endif
