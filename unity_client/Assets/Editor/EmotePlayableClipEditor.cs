using UnityEditor;
using CyanNook.Timeline;

namespace CyanNook.Editor
{
    /// <summary>
    /// EmotePlayableClipのカスタムエディタ
    /// additiveBones用のボーンテンプレートボタンを表示する
    /// </summary>
    [CustomEditor(typeof(EmotePlayableClip))]
    public class EmotePlayableClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            BoneTemplateUtility.DrawTemplateButtons(serializedObject, "additiveBones");
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
