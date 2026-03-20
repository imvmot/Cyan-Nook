using UnityEditor;
using CyanNook.Timeline;

namespace CyanNook.Editor
{
    /// <summary>
    /// ThinkingPlayableClipのカスタムエディタ
    /// additiveBones用のボーンテンプレートボタンを表示する
    /// </summary>
    [CustomEditor(typeof(ThinkingPlayableClip))]
    public class ThinkingPlayableClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            BoneTemplateUtility.DrawTemplateButtons(serializedObject, "additiveBones");
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
