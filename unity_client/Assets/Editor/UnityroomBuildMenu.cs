using UnityEditor;
using UnityEngine;
using System.IO;
using CyanNook.Core;

namespace CyanNook.Editor
{
    /// <summary>
    /// UnityroomConfig アセット管理。
    ///
    /// ビルドの切替 (GitHub / Unityroom / Mobile) は Window > Build Profiles の
    /// 3プロファイルで行う (define・テクスチャ圧縮・テンプレートはプロファイル側が保持)。
    /// かつてここにあった切替メニューはプロファイル移行に伴い削除済み。
    ///
    /// CyanNook > Build メニューから操作:
    /// - Create/Open Unityroom Config: UnityroomConfig.asset を作成または開く
    ///   (Unityroom プロファイルでのビルドに必要。gitignore 対象)
    /// </summary>
    public static class UnityroomBuildMenu
    {
        private const string CONFIG_ASSET_PATH = "Assets/Resources/UnityroomConfig.asset";

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
    }
}
