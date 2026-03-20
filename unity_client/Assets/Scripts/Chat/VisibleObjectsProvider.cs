using UnityEngine;
using System.Text;
using CyanNook.Core;
using CyanNook.Character;

namespace CyanNook.Chat
{
    /// <summary>
    /// キャラクターカメラのFrustum内にあるSceneObjectDescriptorを収集し、
    /// LLMプロンプト用のテキストを生成するプロバイダー
    /// LLMリクエスト時のみ呼び出される（毎フレーム実行ではない）
    /// </summary>
    public class VisibleObjectsProvider : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("キャラクターカメラコントローラー")]
        public CharacterCameraController cameraController;

        /// <summary>
        /// キャラクターカメラのFrustum内にあるオブジェクトの説明テキストを生成
        /// </summary>
        /// <returns>可視オブジェクトの説明リスト（改行区切り）。可視オブジェクトがない場合は空文字</returns>
        public string GenerateVisibleObjectsText()
        {
            if (cameraController == null || cameraController.characterCamera == null)
                return "";

            var camera = cameraController.characterCamera;

            // オンデマンドレンダリング時: カメラが無効でもFrustum計算は可能
            // （カメラのtransformはLateUpdateで更新済み）
            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);

            var descriptors = FindObjectsByType<SceneObjectDescriptor>(FindObjectsSortMode.None);
            if (descriptors.Length == 0)
                return "";

            var sb = new StringBuilder();
            int count = 0;

            foreach (var descriptor in descriptors)
            {
                if (string.IsNullOrEmpty(descriptor.objectName))
                    continue;

                var renderer = descriptor.ObjectRenderer;
                if (renderer == null || !renderer.enabled)
                    continue;

                // Frustum内判定
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds))
                {
                    if (count > 0) sb.Append("\n");
                    sb.Append("- ");
                    sb.Append(descriptor.GetPromptText());
                    count++;
                }
            }

            if (count > 0)
            {
                Debug.Log($"[VisibleObjectsProvider] {count} objects visible to character camera");
            }

            return sb.ToString();
        }
    }
}
