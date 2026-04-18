using UnityEngine;
using System;
using CyanNook.Core;

namespace CyanNook.Chat
{
    /// <summary>
    /// LLM API設定の保存・読み込みを管理
    /// PlayerPrefsを使用（Editor: レジストリ, WebGL: IndexedDB）
    /// </summary>
    public static class LLMConfigManager
    {
        private const string CONFIG_KEY = "llm_config";

        /// <summary>
        /// 設定を保存
        /// </summary>
        public static void Save(LLMConfig config)
        {
            if (config == null)
            {
                Debug.LogWarning("[LLMConfigManager] Cannot save null config");
                return;
            }

            string json = JsonUtility.ToJson(config);
            PlayerPrefs.SetString(CONFIG_KEY, json);
            PlayerPrefs.Save();
            Debug.Log($"[LLMConfigManager] Config saved: {json}");
        }

        /// <summary>
        /// 設定を読み込み
        /// </summary>
        public static LLMConfig Load()
        {
            string json = PlayerPrefs.GetString(CONFIG_KEY, "");

            if (string.IsNullOrEmpty(json))
            {
                Debug.Log("[LLMConfigManager] No saved config found, using defaults");
                return LLMConfig.GetDefault();
            }

            try
            {
                var config = JsonUtility.FromJson<LLMConfig>(json);
                Debug.Log($"[LLMConfigManager] Config loaded: endpoint={config.apiEndpoint}, model={config.modelName}");
                return config;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLMConfigManager] Failed to parse config: {e.Message}");
                return LLMConfig.GetDefault();
            }
        }

        /// <summary>
        /// 設定が保存されているか確認
        /// </summary>
        public static bool HasSavedConfig()
        {
            return PlayerPrefs.HasKey(CONFIG_KEY);
        }

        /// <summary>
        /// 保存された設定を削除
        /// </summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(CONFIG_KEY);
            PlayerPrefs.Save();
            Debug.Log("[LLMConfigManager] Config cleared");
        }
    }

    /// <summary>
    /// LLM API設定データ
    /// </summary>
    [Serializable]
    public class LLMConfig
    {
        [Tooltip("API エンドポイント URL")]
        public string apiEndpoint;

        [Tooltip("使用するモデル名")]
        public string modelName;

        [Tooltip("Temperature (0.0-2.0)")]
        public float temperature;

        [Tooltip("Top P (0.0-1.0)")]
        public float topP;

        [Tooltip("Top K (0-100)")]
        public int topK;

        [Tooltip("最大応答トークン数 (-1=無制限)")]
        public int numPredict;

        [Tooltip("コンテキスト長（トークン数）")]
        public int numCtx;

        [Tooltip("繰り返しペナルティ (1.0=無効)")]
        public float repeatPenalty;

        [Tooltip("Thinkingモード（推論モデル用）")]
        public bool think;

        [Tooltip("リクエストタイムアウト（秒）")]
        public float timeout;

        [Tooltip("API タイプ")]
        public LLMApiType apiType;

        [Tooltip("API Key（Dify/OpenAI/Claude/Gemini用）")]
        public string apiKey;

        /// <summary>
        /// デフォルト設定を取得（Ollama用）
        /// </summary>
        public static LLMConfig GetDefault()
        {
            return new LLMConfig
            {
                apiEndpoint = "http://localhost:11434/api/generate",
                modelName = "gemma2",
                temperature = 0.7f,
                topP = 0.9f,
                topK = 40,
                numPredict = 512,
                numCtx = 4096,
                repeatPenalty = 1.1f,
                think = false,
                timeout = 60f,
                apiType = LLMApiType.Ollama,
                apiKey = ""
            };
        }

        /// <summary>
        /// APIタイプごとのデフォルトエンドポイントを取得
        /// </summary>
        public static string GetDefaultEndpoint(LLMApiType apiType)
        {
            switch (apiType)
            {
                case LLMApiType.Ollama:
                    return "http://localhost:11434/api/generate";
                case LLMApiType.LMStudio:
                    return "http://localhost:1234/v1/chat/completions";
                case LLMApiType.Dify:
                    return "http://localhost/v1";
                case LLMApiType.OpenAI:
                    return "https://api.openai.com/v1/chat/completions";
                case LLMApiType.Claude:
                    return "https://api.anthropic.com/v1/messages";
                case LLMApiType.Gemini:
                    return "https://generativelanguage.googleapis.com/v1beta";
                case LLMApiType.WebLLM:
                    return "";
                default:
                    return "http://localhost:11434/api/generate";
            }
        }

        /// <summary>
        /// 設定が有効かどうか検証
        /// </summary>
        public bool IsValid()
        {
            if (apiType == LLMApiType.WebLLM)
                return true;
            return !string.IsNullOrEmpty(apiEndpoint) && !string.IsNullOrEmpty(modelName);
        }

        /// <summary>
        /// 実効APIキーを取得する。
        /// ユーザーが自分のキーを設定していればそちらを優先。
        /// 空の場合、UNITYROOM_BUILDではUnityroomConfigのデフォルトキーにフォールバック。
        /// UIには空欄で表示されるが、API呼び出し時にはデフォルトキーが使われる。
        /// </summary>
        public string ResolveApiKey()
        {
            if (!string.IsNullOrEmpty(apiKey)) return apiKey;

#if UNITYROOM_BUILD
            if (apiType == LLMApiType.Gemini)
            {
                var unityroomConfig = UnityroomConfig.Load();
                if (unityroomConfig != null && unityroomConfig.HasDefaultApiKey)
                {
                    return unityroomConfig.geminiApiKey;
                }
            }
#endif

            return apiKey;
        }

        /// <summary>
        /// unityroom版のデフォルトGemini設定を作成（apiKeyは空 → ResolveApiKeyでフォールバック）
        /// </summary>
        public static LLMConfig GetUnityroomDefault()
        {
            var unityroomConfig = UnityroomConfig.Load();
            string endpoint = unityroomConfig != null && !string.IsNullOrEmpty(unityroomConfig.geminiEndpoint)
                ? unityroomConfig.geminiEndpoint
                : GetDefaultEndpoint(LLMApiType.Gemini);
            string model = unityroomConfig != null && !string.IsNullOrEmpty(unityroomConfig.geminiModelName)
                ? unityroomConfig.geminiModelName
                : "gemini-2.5-flash";

            return new LLMConfig
            {
                apiType = LLMApiType.Gemini,
                apiEndpoint = endpoint,
                modelName = model,
                temperature = 0.7f,
                topP = 0.9f,
                topK = 40,
                numPredict = 512,
                numCtx = 4096,
                repeatPenalty = 1.1f,
                think = false,
                timeout = 120f,
                apiKey = "" // UIには空欄。実行時にResolveApiKeyでデフォルトキーにフォールバック
            };
        }
    }

    /// <summary>
    /// LLM API タイプ
    /// </summary>
    public enum LLMApiType
    {
        Ollama,     // ローカル Ollama（独自API）
        LMStudio,   // LM Studio（OpenAI互換 Chat Completions API）
        Dify,       // Dify（SSE Chat Messages API）
        OpenAI,     // OpenAI API（Chat Completions API + Bearer認証）
        Claude,     // Anthropic Claude API（Messages API + x-api-key認証）
        Gemini,     // Google Gemini API（Generative Language API + x-goog-api-key認証）
        WebLLM      // ブラウザ内LLM推論（WebGPU + @mlc-ai/web-llm）
    }
}
