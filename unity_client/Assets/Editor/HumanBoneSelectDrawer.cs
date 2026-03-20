using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace CyanNook.Editor
{
    /// <summary>
    /// HumanBoneSelectAttribute用のPropertyDrawer
    /// HumanBodyBones enumをカテゴリ別のAdvancedDropdownで表示する
    /// </summary>
    [CustomPropertyDrawer(typeof(HumanBoneSelectAttribute))]
    public class HumanBoneSelectDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Enum)
            {
                EditorGUI.LabelField(position, label.text, "Use with HumanBodyBones enum");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            string currentName = property.enumNames[property.enumValueIndex];

            if (GUI.Button(position, new GUIContent(currentName), EditorStyles.popup))
            {
                var dropdown = new HumanBoneDropdown(new AdvancedDropdownState(), property);
                dropdown.Show(position);
            }

            EditorGUI.EndProperty();
        }
    }

    /// <summary>
    /// Humanoidボーンをカテゴリ別に表示するAdvancedDropdown
    /// </summary>
    public class HumanBoneDropdown : AdvancedDropdown
    {
        private SerializedProperty _property;

        public HumanBoneDropdown(AdvancedDropdownState state, SerializedProperty property) : base(state)
        {
            _property = property;
            minimumSize = new Vector2(200, 300);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Humanoid Bones");

            var torso = new AdvancedDropdownItem("Torso (体幹)");
            var head = new AdvancedDropdownItem("Head (頭部)");
            var leftArm = new AdvancedDropdownItem("Left Arm (左腕)");
            var rightArm = new AdvancedDropdownItem("Right Arm (右腕)");
            var leftLeg = new AdvancedDropdownItem("Left Leg (左脚)");
            var rightLeg = new AdvancedDropdownItem("Right Leg (右脚)");
            var leftFingers = new AdvancedDropdownItem("Left Fingers (左指)");
            var rightFingers = new AdvancedDropdownItem("Right Fingers (右指)");

            root.AddChild(torso);
            root.AddChild(head);
            root.AddChild(leftArm);
            root.AddChild(rightArm);
            root.AddChild(leftLeg);
            root.AddChild(rightLeg);
            root.AddChild(leftFingers);
            root.AddChild(rightFingers);

            string[] names = _property.enumNames;

            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == "LastBone") continue;

                string boneName = names[i];
                var item = new AdvancedDropdownItem(boneName) { id = i };

                // カテゴリ分類
                if (boneName.Contains("Hips") || boneName.Contains("Spine") || boneName.Contains("Chest"))
                {
                    torso.AddChild(item);
                }
                else if (boneName.Contains("Head") || boneName.Contains("Neck")
                    || boneName.Contains("Eye") || boneName.Contains("Jaw"))
                {
                    head.AddChild(item);
                }
                else if (boneName.StartsWith("Left") && IsArmBone(boneName))
                {
                    leftArm.AddChild(item);
                }
                else if (boneName.StartsWith("Right") && IsArmBone(boneName))
                {
                    rightArm.AddChild(item);
                }
                else if (boneName.StartsWith("Left") && IsLegBone(boneName))
                {
                    leftLeg.AddChild(item);
                }
                else if (boneName.StartsWith("Right") && IsLegBone(boneName))
                {
                    rightLeg.AddChild(item);
                }
                else if (boneName.StartsWith("Left") && IsFingerBone(boneName))
                {
                    leftFingers.AddChild(item);
                }
                else if (boneName.StartsWith("Right") && IsFingerBone(boneName))
                {
                    rightFingers.AddChild(item);
                }
                else
                {
                    root.AddChild(item);
                }
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            _property.serializedObject.Update();
            _property.enumValueIndex = item.id;
            _property.serializedObject.ApplyModifiedProperties();
        }

        private static bool IsArmBone(string boneName)
        {
            return boneName.Contains("Shoulder") || boneName.Contains("UpperArm")
                || boneName.Contains("LowerArm") || boneName.Contains("Hand");
        }

        private static bool IsLegBone(string boneName)
        {
            return boneName.Contains("UpperLeg") || boneName.Contains("LowerLeg")
                || boneName.Contains("Foot") || boneName.Contains("Toes");
        }

        private static bool IsFingerBone(string boneName)
        {
            return boneName.Contains("Thumb") || boneName.Contains("Index")
                || boneName.Contains("Middle") || boneName.Contains("Ring")
                || boneName.Contains("Little");
        }
    }
}
