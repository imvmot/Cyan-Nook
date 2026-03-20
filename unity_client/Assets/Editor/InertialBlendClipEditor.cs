using UnityEditor;
using CyanNook.Timeline;

namespace CyanNook.Editor
{
    /// <summary>
    /// InertialBlendClipのカスタムエディタ
    /// ボーンテンプレートボタンを表示する
    /// </summary>
    [CustomEditor(typeof(InertialBlendClip))]
    public class InertialBlendClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            BoneTemplateUtility.DrawTemplateButtons(serializedObject, "targetBones");
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
