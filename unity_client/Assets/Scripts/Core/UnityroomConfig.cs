using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// unityroom版ビルド用の秘密設定。
    /// Resources/UnityroomConfig.asset に配置し、.gitignoreで除外する。
    /// ビルドに含まれるがGitHubリポジトリには公開されない。
    ///
    /// GitHub版ビルドではこのアセットが存在しないため、
    /// Resources.Load は null を返し、デフォルトキーなしで動作する。
    /// </summary>
    [CreateAssetMenu(fileName = "UnityroomConfig", menuName = "CyanNook/Unityroom Config")]
    public class UnityroomConfig : ScriptableObject
    {
        [Header("Default API Keys")]
        [Tooltip("LLM用 Gemini API キー（unityroom版の初期設定に使用。ユーザーが自分のキーを設定すればそちらが優先）")]
        public string geminiApiKey = "";

        [Header("Default LLM Settings")]
        [Tooltip("デフォルトのGeminiモデル名")]
        public string geminiModelName = "gemini-2.5-flash";

        [Tooltip("デフォルトのGemini APIエンドポイント")]
        public string geminiEndpoint = "https://generativelanguage.googleapis.com/v1beta";

        /// <summary>
        /// Resources フォルダからロードする。アセットが存在しなければ null。
        /// </summary>
        public static UnityroomConfig Load()
        {
            return Resources.Load<UnityroomConfig>("UnityroomConfig");
        }

        /// <summary>
        /// 有効なデフォルトAPIキーが設定されているかどうか
        /// </summary>
        public bool HasDefaultApiKey => !string.IsNullOrEmpty(geminiApiKey);
    }
}
