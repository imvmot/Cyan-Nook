using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// シーン内のオブジェクトに説明文を付与するコンポーネント
    /// キャラクターカメラのFrustum内にあるオブジェクトの説明がLLMプロンプトに注入される
    /// Rendererが必要（Frustum判定にBoundsを使用）
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class SceneObjectDescriptor : MonoBehaviour
    {
        [Tooltip("オブジェクトの表示名（例: 木製の椅子、花瓶）")]
        public string objectName = "";

        [TextArea(1, 3)]
        [Tooltip("オブジェクトの詳細説明（例: 白い花が生けてある小さな花瓶）空欄可")]
        public string description = "";

        private Renderer _renderer;

        /// <summary>Frustum判定用のRenderer</summary>
        public Renderer ObjectRenderer
        {
            get
            {
                if (_renderer == null)
                    _renderer = GetComponent<Renderer>();
                return _renderer;
            }
        }

        /// <summary>プロンプト用のテキストを生成</summary>
        public string GetPromptText()
        {
            if (string.IsNullOrEmpty(description))
                return objectName;
            return $"{objectName}（{description}）";
        }
    }
}
