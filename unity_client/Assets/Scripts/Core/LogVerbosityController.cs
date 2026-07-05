using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// リリースビルドでの情報ログ抑止。
    /// Debug.Logは誰も見ていなくても呼び出しごとに文字列組み立て+
    /// スタックトレース採取+コンソール書き出しのコストを払う。
    /// WebGLはインクリメンタルGCがなく文字列ゴミがGCスパイクの原因になるため、
    /// ビルドでは既定でWarning/Error/Exceptionのみ通す。
    /// デバッグ設定パネルの Verbose Logs トグルでONに戻せる（不具合調査用）。
    /// エディタでは常に全ログを出す（開発時のログを失わないため）。
    /// ※ ログ呼び出しの引数の文字列組み立てだけはC#仕様上残る
    /// （呼び出し前に評価されるため）。最も重いスタックトレース採取と
    /// コンソール書き出しが消えるだけで十分な効果がある
    /// </summary>
    public static class LogVerbosityController
    {
        private const string PrefKey_VerboseLogs = "debug_verboseLogs";

        /// <summary>保存されているVerbose設定（ビルドでの情報ログ有効/無効）</summary>
        public static bool SavedVerbose => PlayerPrefs.GetInt(PrefKey_VerboseLogs, 0) == 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnStartup()
        {
#if UNITY_EDITOR
            // エディタでは常に全ログで開始
            Apply(true);
#else
            Apply(SavedVerbose);
#endif
        }

        /// <summary>
        /// Verboseログの有効/無効を切り替えて保存（DebugSettingsPanelから呼ばれる）
        /// 明示的な操作のためエディタでも即時反映する
        /// </summary>
        public static void SetVerbose(bool verbose)
        {
            PlayerPrefs.SetInt(PrefKey_VerboseLogs, verbose ? 1 : 0);
            PlayerPrefs.Save();
            Apply(verbose);
        }

        private static void Apply(bool verbose)
        {
            // filterLogType: 指定した重要度以上のみ通す
            // （LogType.Warning = Warning/Assert/Error を通し Log を遮断。Exceptionは常に通る）
            Debug.unityLogger.filterLogType = verbose ? LogType.Log : LogType.Warning;

            // 情報ログのスタックトレース採取を停止（1件ごとの最大のコスト源）
            Application.SetStackTraceLogType(LogType.Log,
                verbose ? StackTraceLogType.ScriptOnly : StackTraceLogType.None);
        }
    }
}
