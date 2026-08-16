#if MOBILE_WEB_BUILD
using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// モバイル向けWebGLビルド専用の起動時設定。
    /// MOBILE_WEB_BUILD シンボルは Build Profiles の "WebGL - Mobile" が付与する
    /// (PC/unityroom ビルドではこのファイル全体がコンパイルされない)。
    ///
    /// - Quality を軽量レベル "Cyan-nook Mobile" へ切替
    ///   (HDR/MSAA/ソフトシャドウ無効、シャドウ解像度・距離削減、スキニング2ウェイト)
    /// - フレームレートを30固定 (「窓」用途では滑らかさより省電力・発熱・安定を優先)
    /// </summary>
    public static class MobileWebBootstrap
    {
        private const string QualityLevelName = "Cyan-nook Mobile";

        /// <summary>モバイルの上限FPS。FrameRateLimiter もこの値でクランプする (単一の情報源)</summary>
        public const int TargetFrameRate = 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            bool found = false;
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == QualityLevelName)
                {
                    QualitySettings.SetQualityLevel(i, true);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // レベル名の変更・削除時に黙って重い設定のまま動くのを防ぐための警告
                Debug.LogWarning($"[MobileWebBootstrap] Quality level '{QualityLevelName}' not found, using default");
            }

            Application.targetFrameRate = TargetFrameRate;

            Debug.Log($"[MobileWebBootstrap] Quality='{QualitySettings.names[QualitySettings.GetQualityLevel()]}', targetFrameRate={TargetFrameRate}");
        }
    }
}
#endif
