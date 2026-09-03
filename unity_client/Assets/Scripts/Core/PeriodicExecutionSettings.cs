using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// 定期実行（IdleChat / SleepChat / Outing の自動LLMリクエスト）のマスターON/OFF共有設定。
    /// 3つのコントローラーと設定UIが同じ状態を参照するため、PlayerPrefsアクセスをここに集約する。
    /// マスターOFF: 3機能の自動リクエストを全停止。
    /// マスターON: 各機能のinterval設定に従う（interval 0以下の機能は個別に無効）。
    /// </summary>
    public static class PeriodicExecutionSettings
    {
        public const string PrefKey = "periodic_enabled";

        // 旧設定からの移行用: マスターキー未保存の環境では旧IdleChatのON/OFFを引き継ぐ
        private const string LegacyIdleChatKey = "idleChatEnabled";

        /// <summary>
        /// 定期実行マスターが有効か。
        /// 未設定の場合は旧idleChatEnabledを引き継ぎ、それも無ければfalse（デフォルトOFF）。
        /// </summary>
        public static bool IsEnabled()
        {
            if (PlayerPrefs.HasKey(PrefKey))
            {
                return PlayerPrefs.GetInt(PrefKey) == 1;
            }
            if (PlayerPrefs.HasKey(LegacyIdleChatKey))
            {
                return PlayerPrefs.GetInt(LegacyIdleChatKey) == 1;
            }
            return false;
        }

        /// <summary>
        /// 定期実行マスターのON/OFFを保存
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(PrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
