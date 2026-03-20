using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CyanNook.Editor
{
    /// <summary>
    /// HumanBodyBonesリスト用のテンプレートボタンUI共有ユーティリティ。
    /// InertialBlendClip、EmotePlayableClip、ThinkingPlayableClip で共用する。
    /// </summary>
    public static class BoneTemplateUtility
    {
        public enum TemplateType
        {
            All,
            ExcludeFingers,
            UpperBody,
            LowerBody,
            Clear,
        }

        // Eye/Jawは対象外（全テンプレート共通）
        private static bool IsExcludedBone(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftEye
                || bone == HumanBodyBones.RightEye
                || bone == HumanBodyBones.Jaw;
        }

        private static bool IsFingerBone(string boneName)
        {
            return boneName.Contains("Thumb") || boneName.Contains("Index")
                || boneName.Contains("Middle") || boneName.Contains("Ring")
                || boneName.Contains("Little");
        }

        private static bool IsUpperBodyBone(string boneName)
        {
            return boneName.Contains("Spine") || boneName.Contains("Chest")
                || boneName.Contains("Neck") || boneName.Contains("Head")
                || boneName.Contains("Shoulder") || boneName.Contains("UpperArm")
                || boneName.Contains("LowerArm") || boneName.Contains("Hand");
        }

        private static bool IsLowerBodyBone(string boneName)
        {
            return boneName.Contains("Hips") || boneName.Contains("UpperLeg")
                || boneName.Contains("LowerLeg") || boneName.Contains("Foot")
                || boneName.Contains("Toes");
        }

        /// <summary>
        /// テンプレートボタンを描画する。
        /// 「全て」「指除外」「上半身」「下半身」「クリア」の5つのボタンを横並びに表示。
        /// </summary>
        /// <param name="serializedObject">対象のSerializedObject</param>
        /// <param name="propertyName">ボーンリストのプロパティ名</param>
        public static void DrawTemplateButtons(SerializedObject serializedObject, string propertyName)
        {
            EditorGUILayout.LabelField("Template", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全て")) ApplyTemplate(serializedObject, propertyName, TemplateType.All);
            if (GUILayout.Button("指除外")) ApplyTemplate(serializedObject, propertyName, TemplateType.ExcludeFingers);
            if (GUILayout.Button("上半身")) ApplyTemplate(serializedObject, propertyName, TemplateType.UpperBody);
            if (GUILayout.Button("下半身")) ApplyTemplate(serializedObject, propertyName, TemplateType.LowerBody);
            if (GUILayout.Button("クリア")) ApplyTemplate(serializedObject, propertyName, TemplateType.Clear);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// テンプレートをSerializedPropertyに適用する
        /// </summary>
        public static void ApplyTemplate(SerializedObject serializedObject, string propertyName, TemplateType template)
        {
            serializedObject.Update();

            var bones = GetTemplateBones(template);
            var list = serializedObject.FindProperty(propertyName);

            list.ClearArray();
            for (int i = 0; i < bones.Count; i++)
            {
                list.InsertArrayElementAtIndex(i);
                list.GetArrayElementAtIndex(i).intValue = (int)bones[i];
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// テンプレートに基づくボーンリストを取得する
        /// </summary>
        public static List<HumanBodyBones> GetTemplateBones(TemplateType template)
        {
            var bones = new List<HumanBodyBones>();

            if (template == TemplateType.Clear)
            {
                return bones; // 空リスト
            }

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                if (IsExcludedBone(bone)) continue;

                string name = bone.ToString();
                switch (template)
                {
                    case TemplateType.All:
                        bones.Add(bone);
                        break;
                    case TemplateType.ExcludeFingers:
                        if (!IsFingerBone(name)) bones.Add(bone);
                        break;
                    case TemplateType.UpperBody:
                        if (IsUpperBodyBone(name) || IsFingerBone(name)) bones.Add(bone);
                        break;
                    case TemplateType.LowerBody:
                        if (IsLowerBodyBone(name)) bones.Add(bone);
                        break;
                }
            }
            return bones;
        }
    }
}
