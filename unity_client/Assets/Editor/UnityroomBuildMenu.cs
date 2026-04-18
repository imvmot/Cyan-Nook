using UnityEditor;
using UnityEngine;
using System.IO;
using CyanNook.Core;

namespace CyanNook.Editor
{
    /// <summary>
    /// unityroom版 / GitHub版のビルド切替 + UnityroomConfig アセット管理。
    ///
    /// CyanNook > Build メニューから操作:
    /// - Switch to GitHub Build: UNITYROOM_BUILD シンボルを除去
    /// - Switch to Unityroom Build: UNITYROOM_BUILD シンボルを追加
    /// - Create/Open Unityroom Config: UnityroomConfig.asset を作成または開く
    /// </summary>
    public static class UnityroomBuildMenu
    {
        private const string DEFINE_SYMBOL = "UNITYROOM_BUILD";
        private const string CONFIG_ASSET_PATH = "Assets/Resources/UnityroomConfig.asset";

        // ─────────────────────────────────────
        // Build Switch
        // ─────────────────────────────────────

        [MenuItem("CyanNook/Build/Switch to GitHub Build")]
        public static void SwitchToGitHub()
        {
            RemoveDefineSymbol(DEFINE_SYMBOL);
            Debug.Log("[UnityroomBuildMenu] Switched to GitHub build (UNITYROOM_BUILD removed)");
        }

        [MenuItem("CyanNook/Build/Switch to Unityroom Build")]
        public static void SwitchToUnityroom()
        {
            AddDefineSymbol(DEFINE_SYMBOL);

            // UnityroomConfig が無ければ作成を促す
            var config = UnityroomConfig.Load();
            if (config == null)
            {
                bool create = EditorUtility.DisplayDialog(
                    "Unityroom Config",
                    "UnityroomConfig.asset が見つかりません。\n" +
                    "デフォルトAPIキーを設定するには先にアセットを作成してください。\n\n" +
                    "今すぐ作成しますか？",
                    "作成", "後で");

                if (create)
                {
                    CreateOrOpenConfig();
                }
            }

            Debug.Log("[UnityroomBuildMenu] Switched to Unityroom build (UNITYROOM_BUILD added)");
        }

        // Validation: チェックマーク表示
        [MenuItem("CyanNook/Build/Switch to GitHub Build", true)]
        private static bool ValidateGitHub()
        {
            Menu.SetChecked("CyanNook/Build/Switch to GitHub Build", !HasDefineSymbol(DEFINE_SYMBOL));
            return true;
        }

        [MenuItem("CyanNook/Build/Switch to Unityroom Build", true)]
        private static bool ValidateUnityroom()
        {
            Menu.SetChecked("CyanNook/Build/Switch to Unityroom Build", HasDefineSymbol(DEFINE_SYMBOL));
            return true;
        }

        // ─────────────────────────────────────
        // Config Asset Management
        // ─────────────────────────────────────

        [MenuItem("CyanNook/Build/Create or Open Unityroom Config")]
        public static void CreateOrOpenConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityroomConfig>(CONFIG_ASSET_PATH);
            if (existing != null)
            {
                // 既にある場合はInspectorで開く
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[UnityroomBuildMenu] Opened existing UnityroomConfig");
                return;
            }

            // Resources フォルダが無ければ作成
            string resourcesDir = Path.GetDirectoryName(CONFIG_ASSET_PATH);
            if (!AssetDatabase.IsValidFolder(resourcesDir))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            // 新規アセット作成
            var config = ScriptableObject.CreateInstance<UnityroomConfig>();
            AssetDatabase.CreateAsset(config, CONFIG_ASSET_PATH);
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            Debug.Log("[UnityroomBuildMenu] Created UnityroomConfig at " + CONFIG_ASSET_PATH);
        }

        // ─────────────────────────────────────
        // Define Symbol Utilities
        // ─────────────────────────────────────

        private static bool HasDefineSymbol(string symbol)
        {
            var target = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(target);
            return ContainsSymbol(defines, symbol);
        }

        private static void AddDefineSymbol(string symbol)
        {
            var target = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(target);

            if (ContainsSymbol(defines, symbol)) return;

            defines = string.IsNullOrEmpty(defines) ? symbol : defines + ";" + symbol;
            PlayerSettings.SetScriptingDefineSymbolsForGroup(target, defines);
        }

        private static void RemoveDefineSymbol(string symbol)
        {
            var target = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(target);

            if (!ContainsSymbol(defines, symbol)) return;

            var list = new System.Collections.Generic.List<string>(defines.Split(';'));
            list.RemoveAll(s => s.Trim() == symbol);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(target, string.Join(";", list));
        }

        private static bool ContainsSymbol(string defines, string symbol)
        {
            if (string.IsNullOrEmpty(defines)) return false;
            foreach (var s in defines.Split(';'))
            {
                if (s.Trim() == symbol) return true;
            }
            return false;
        }
    }
}
