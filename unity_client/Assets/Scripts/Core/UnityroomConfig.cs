using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// unityroom版ビルド用のデフォルトAPIキー設定。
    /// Resources/UnityroomConfig.asset に配置し、アセット自体は.gitignoreで除外する。
    ///
    /// 注意: Resources配下のアセットは全ビルドに収録されるため、
    /// キーは unityroom版だけでなく GitHub版・Mobile版のビルド成果物にも
    /// 埋め込まれる（リポジトリの build/ 等に含まれ、ツールで抽出可能）。
    /// 漏洩許容の無料枠キーで運用する前提。有料キーは絶対に入れないこと。
    ///
    /// アセットを作らずに運用した場合、Resources.Load は null を返し
    /// デフォルトキーなしで動作する。
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

        [Header("Default TTS Settings")]
        [Tooltip("unityroom版で使用するGemini TTSモデル名（モデル選択UIを封鎖しているためここで固定。モデル更新時はこの値を変更する）")]
        public string geminiTtsModel = "gemini-2.5-flash-preview-tts";

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
