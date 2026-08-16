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
    /// - Texture Compression - Desktop (DXT) / Mobile (ASTC): WebGLビルドのテクスチャ圧縮形式切替
    /// - Create/Open Unityroom Config: UnityroomConfig.asset を作成または開く
    /// </summary>
    public static class UnityroomBuildMenu
    {
        private const string DEFINE_SYMBOL = "UNITYROOM_BUILD";
        private const string CONFIG_ASSET_PATH = "Assets/Resources/UnityroomConfig.asset";

        private const string MENU_TEX_DESKTOP = "CyanNook/Build/Texture Compression - Desktop (DXT)";
        private const string MENU_TEX_MOBILE = "CyanNook/Build/Texture Compression - Mobile (ASTC)";

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

            // unityroom版はPC客層向けのためテクスチャはDXT固定。
            // ASTCのままアップロードする事故（PC側で全テクスチャCPU展開）を防ぐ
            if (EditorUserBuildSettings.webGLBuildSubtarget == WebGLTextureSubtarget.ASTC)
            {
                EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;
                Debug.LogWarning("[UnityroomBuildMenu] Texture compression was ASTC -> reset to DXT (unityroom build is desktop-targeted)");
            }

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
        // Texture Compression Switch (WebGL)
        //
        // PC GPU は DXT、モバイル GPU (Mali/Apple) は ASTC しか直接扱えない。
        // 非対応形式はロード時に CPU で無圧縮展開されメモリが数倍化し、
        // 低メモリ端末 (Fire HD 等) では OOM でロードが止まる。
        // モバイル向けに配信する場合は ASTC に切り替えてビルドする
        // ─────────────────────────────────────

        [MenuItem(MENU_TEX_DESKTOP)]
        public static void SwitchTextureToDesktop()
        {
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;
            Debug.Log("[UnityroomBuildMenu] WebGL texture compression: DXT (PC向け)");
        }

        [MenuItem(MENU_TEX_MOBILE)]
        public static void SwitchTextureToMobile()
        {
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ASTC;
            Debug.Log("[UnityroomBuildMenu] WebGL texture compression: ASTC (モバイル向け)。" +
                "ビルド時は出力先を build/ とは別のフォルダ (例: build-mobile/) にすること");
        }

        [MenuItem(MENU_TEX_DESKTOP, true)]
        private static bool ValidateTextureDesktop()
        {
            // Generic (未指定) はPC向けデフォルト扱いとしてチェックを付ける
            var sub = EditorUserBuildSettings.webGLBuildSubtarget;
            Menu.SetChecked(MENU_TEX_DESKTOP, sub != WebGLTextureSubtarget.ASTC && sub != WebGLTextureSubtarget.ETC2);
            return true;
        }

        [MenuItem(MENU_TEX_MOBILE, true)]
        private static bool ValidateTextureMobile()
        {
            Menu.SetChecked(MENU_TEX_MOBILE, EditorUserBuildSettings.webGLBuildSubtarget == WebGLTextureSubtarget.ASTC);
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
