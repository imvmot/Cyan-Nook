using UnityEngine;
using UnityEditor;

namespace CyanNook.Editor
{
    /// <summary>
    /// VRMランタイム読み込みに必要なシェーダーをビルドに含めるエディタツール
    /// WebGLビルドではシーンから参照されていないシェーダーがストリップされるため、
    /// "Always Included Shaders" に手動追加が必要
    /// </summary>
    public static class VrmShaderIncluder
    {
        // VRMランタイム読み込みで Shader.Find() されるシェーダー名
        private static readonly string[] RequiredShaderNames = new string[]
        {
            "VRM10/Universal Render Pipeline/MToon10",  // URP MToon
            "VRM10/MToon10",                            // Built-in MToon (VRM 0.x migration)
            "UniGLTF/UniUnlit",                         // UniUnlit
        };

        [MenuItem("CyanNook/Add VRM Shaders to Always Included")]
        public static void AddVrmShaders()
        {
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null)
            {
                Debug.LogError("[VrmShaderIncluder] GraphicsSettings.asset not found");
                return;
            }

            var serializedObject = new SerializedObject(graphicsSettings);
            var arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");

            int addedCount = 0;

            foreach (string shaderName in RequiredShaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[VrmShaderIncluder] Shader not found: {shaderName}");
                    continue;
                }

                // 既に含まれているかチェック
                bool alreadyIncluded = false;
                for (int i = 0; i < arrayProp.arraySize; i++)
                {
                    if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        alreadyIncluded = true;
                        break;
                    }
                }

                if (!alreadyIncluded)
                {
                    int newIndex = arrayProp.arraySize;
                    arrayProp.InsertArrayElementAtIndex(newIndex);
                    arrayProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
                    addedCount++;
                    Debug.Log($"[VrmShaderIncluder] Added: {shaderName}");
                }
                else
                {
                    Debug.Log($"[VrmShaderIncluder] Already included: {shaderName}");
                }
            }

            if (addedCount > 0)
            {
                serializedObject.ApplyModifiedProperties();
                Debug.Log($"[VrmShaderIncluder] {addedCount} shaders added to Always Included Shaders");
            }
            else
            {
                Debug.Log("[VrmShaderIncluder] All required shaders already included");
            }
        }
    }
}
