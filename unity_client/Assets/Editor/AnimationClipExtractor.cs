using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace CyanNook.Editor
{
    /// <summary>
    /// FBXからAnimationClipを抽出してClipsフォルダに配置するエディタツール
    /// キャラクターと家具の両方に対応
    /// </summary>
    public static class AnimationClipExtractor
    {
        private const string FurnitureRoot = "Assets/Animations/Furniture";

        [MenuItem("CyanNook/Animation/Extract All Animation Clips")]
        public static void ExtractAllAnimationClips()
        {
            string animationsRoot = "Assets/Animations";

            if (!Directory.Exists(animationsRoot))
            {
                EditorUtility.DisplayDialog("Error", $"Animations folder not found: {animationsRoot}", "OK");
                return;
            }

            int totalExtracted = 0;

            // 各キャラクターフォルダを処理（Furnitureフォルダは除外）
            var characterFolders = Directory.GetDirectories(animationsRoot);
            foreach (var characterFolder in characterFolders)
            {
                string folderName = Path.GetFileName(characterFolder);
                if (folderName == "Furniture") continue; // 家具は別処理

                int extracted = ExtractClipsForCharacter(folderName);
                totalExtracted += extracted;
            }

            // 家具フォルダを処理
            if (Directory.Exists(FurnitureRoot))
            {
                var furnitureFolders = Directory.GetDirectories(FurnitureRoot);
                foreach (var furnitureFolder in furnitureFolders)
                {
                    string furnitureId = Path.GetFileName(furnitureFolder);
                    int extracted = ExtractClipsForFurniture(furnitureId);
                    totalExtracted += extracted;
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[AnimationClipExtractor] Total clips extracted: {totalExtracted}");
            EditorUtility.DisplayDialog("Complete", $"Extracted {totalExtracted} animation clips.", "OK");
        }

        [MenuItem("CyanNook/Animation/Extract Clips for Selected Character")]
        public static void ExtractClipsForSelectedCharacter()
        {
            // 選択されたフォルダまたはアセットからキャラクター名を取得
            var selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a character folder or FBX file.", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            string characterName = ExtractCharacterNameFromPath(path);

            if (string.IsNullOrEmpty(characterName))
            {
                EditorUtility.DisplayDialog("Error", "Could not determine character name from selection.", "OK");
                return;
            }

            int extracted = ExtractClipsForCharacter(characterName);
            AssetDatabase.Refresh();

            Debug.Log($"[AnimationClipExtractor] Extracted {extracted} clips for {characterName}");
            EditorUtility.DisplayDialog("Complete", $"Extracted {extracted} animation clips for {characterName}.", "OK");
        }

        [MenuItem("CyanNook/Animation/Extract All Furniture Animation Clips")]
        public static void ExtractAllFurnitureAnimationClips()
        {
            if (!Directory.Exists(FurnitureRoot))
            {
                EditorUtility.DisplayDialog("Error", $"Furniture animations folder not found: {FurnitureRoot}", "OK");
                return;
            }

            int totalExtracted = 0;

            var furnitureFolders = Directory.GetDirectories(FurnitureRoot);
            foreach (var furnitureFolder in furnitureFolders)
            {
                string furnitureId = Path.GetFileName(furnitureFolder);
                int extracted = ExtractClipsForFurniture(furnitureId);
                totalExtracted += extracted;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[AnimationClipExtractor] Total furniture clips extracted: {totalExtracted}");
            EditorUtility.DisplayDialog("Complete", $"Extracted {totalExtracted} furniture animation clips.", "OK");
        }

        [MenuItem("CyanNook/Animation/Extract Clips for Selected Furniture")]
        public static void ExtractClipsForSelectedFurniture()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a furniture folder or FBX file.", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            string furnitureId = ExtractFurnitureIdFromPath(path);

            if (string.IsNullOrEmpty(furnitureId))
            {
                EditorUtility.DisplayDialog("Error", "Could not determine furniture ID from selection.\nExpected path: Assets/Animations/Furniture/{FurnitureID}/...", "OK");
                return;
            }

            int extracted = ExtractClipsForFurniture(furnitureId);
            AssetDatabase.Refresh();

            Debug.Log($"[AnimationClipExtractor] Extracted {extracted} clips for furniture {furnitureId}");
            EditorUtility.DisplayDialog("Complete", $"Extracted {extracted} animation clips for furniture {furnitureId}.", "OK");
        }

        /// <summary>
        /// 指定家具のFBXからAnimationClipを抽出
        /// </summary>
        public static int ExtractClipsForFurniture(string furnitureId)
        {
            string fbxFolder = $"{FurnitureRoot}/{furnitureId}/FBX";
            string clipsFolder = $"{FurnitureRoot}/{furnitureId}/Clips";

            if (!Directory.Exists(fbxFolder))
            {
                Debug.LogWarning($"[AnimationClipExtractor] Furniture FBX folder not found: {fbxFolder}");
                return 0;
            }

            // Clipsフォルダを作成
            if (!Directory.Exists(clipsFolder))
            {
                Directory.CreateDirectory(clipsFolder);
                AssetDatabase.Refresh();
            }

            int extractedCount = 0;

            var fbxFiles = Directory.GetFiles(fbxFolder, "*.fbx");
            foreach (var fbxPath in fbxFiles)
            {
                string assetPath = fbxPath.Replace("\\", "/");
                extractedCount += ExtractClipsFromFbx(assetPath, clipsFolder);
            }

            return extractedCount;
        }

        /// <summary>
        /// 指定キャラクターのFBXからAnimationClipを抽出
        /// </summary>
        public static int ExtractClipsForCharacter(string characterName)
        {
            string fbxFolder = $"Assets/Animations/{characterName}/FBX";
            string clipsFolder = $"Assets/Animations/{characterName}/Clips";

            if (!Directory.Exists(fbxFolder))
            {
                Debug.LogWarning($"[AnimationClipExtractor] FBX folder not found: {fbxFolder}");
                return 0;
            }

            // Clipsフォルダを作成
            if (!Directory.Exists(clipsFolder))
            {
                Directory.CreateDirectory(clipsFolder);
                AssetDatabase.Refresh();
            }

            int extractedCount = 0;

            // FBXフォルダ内の全FBXファイルを処理
            var fbxFiles = Directory.GetFiles(fbxFolder, "*.fbx");
            foreach (var fbxPath in fbxFiles)
            {
                string assetPath = fbxPath.Replace("\\", "/");
                extractedCount += ExtractClipsFromFbx(assetPath, clipsFolder);
            }

            return extractedCount;
        }

        /// <summary>
        /// 単一のFBXからAnimationClipを抽出
        /// Import AnimationがOFFのFBXはスキップ（抽出済みとみなす）
        /// 既存の.animファイルは上書きする
        /// </summary>
        private static int ExtractClipsFromFbx(string fbxAssetPath, string outputFolder)
        {
            // Import AnimationがOFFならスキップ（抽出済み）
            var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[AnimationClipExtractor] Could not get importer: {fbxAssetPath}");
                return 0;
            }

            if (!importer.importAnimation)
            {
                Debug.Log($"[AnimationClipExtractor] Skipping (already extracted): {Path.GetFileName(fbxAssetPath)}");
                return 0;
            }

            // FBX内の全アセットを取得
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
            int extractedCount = 0;

            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    string outputPath = $"{outputFolder}/{clip.name}.anim";

                    var existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
                    if (existingClip != null)
                    {
                        // 既存アセットの中身を上書き（GUIDを維持、Timeline参照が壊れない）
                        EditorUtility.CopySerialized(clip, existingClip);
                        existingClip.name = clip.name;
                        EditorUtility.SetDirty(existingClip);
                        Debug.Log($"[AnimationClipExtractor] Updated in-place: {clip.name}");
                    }
                    else
                    {
                        // 新規作成
                        var newClip = Object.Instantiate(clip);
                        newClip.name = clip.name;
                        AssetDatabase.CreateAsset(newClip, outputPath);
                        Debug.Log($"[AnimationClipExtractor] Extracted (new): {clip.name} -> {outputPath}");
                    }
                    extractedCount++;
                }
            }

            // 抽出後、FBXのアニメーションインポートを無効にする
            if (extractedCount > 0)
            {
                DisableAnimationImport(fbxAssetPath);
            }

            return extractedCount;
        }

        /// <summary>
        /// FBXのアニメーションインポートを無効にする
        /// </summary>
        private static void DisableAnimationImport(string fbxAssetPath)
        {
            var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer != null && importer.importAnimation)
            {
                importer.importAnimation = false;
                importer.SaveAndReimport();
                Debug.Log($"[AnimationClipExtractor] Disabled animation import: {fbxAssetPath}");
            }
        }

        [MenuItem("CyanNook/Animation/Disable Animation Import for FBX in Selected Folder")]
        public static void DisableAnimationImportForSelected()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a character folder.", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            string characterName = ExtractCharacterNameFromPath(path);

            if (string.IsNullOrEmpty(characterName))
            {
                EditorUtility.DisplayDialog("Error", "Could not determine character name from selection.", "OK");
                return;
            }

            string fbxFolder = $"Assets/Animations/{characterName}/FBX";
            if (!Directory.Exists(fbxFolder))
            {
                EditorUtility.DisplayDialog("Error", $"FBX folder not found: {fbxFolder}", "OK");
                return;
            }

            int disabledCount = 0;
            var fbxFiles = Directory.GetFiles(fbxFolder, "*.fbx");
            foreach (var fbxPath in fbxFiles)
            {
                string assetPath = fbxPath.Replace("\\", "/");
                var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer != null && importer.importAnimation)
                {
                    importer.importAnimation = false;
                    importer.SaveAndReimport();
                    disabledCount++;
                }
            }

            Debug.Log($"[AnimationClipExtractor] Disabled animation import for {disabledCount} FBX files");
            EditorUtility.DisplayDialog("Complete", $"Disabled animation import for {disabledCount} FBX files.", "OK");
        }

        /// <summary>
        /// パスからキャラクター名を抽出
        /// </summary>
        private static string ExtractCharacterNameFromPath(string path)
        {
            // Assets/Animations/chr001/... のようなパスからchr001を抽出
            if (path.Contains("/Animations/"))
            {
                int startIndex = path.IndexOf("/Animations/") + "/Animations/".Length;
                int endIndex = path.IndexOf("/", startIndex);

                if (endIndex > startIndex)
                {
                    return path.Substring(startIndex, endIndex - startIndex);
                }
                else if (startIndex < path.Length)
                {
                    // フォルダ自体が選択されている場合
                    return path.Substring(startIndex);
                }
            }

            return null;
        }

        /// <summary>
        /// パスから家具IDを抽出
        /// Assets/Animations/Furniture/Door01/... → "Door01"
        /// </summary>
        private static string ExtractFurnitureIdFromPath(string path)
        {
            string furniturePrefix = "/Animations/Furniture/";
            if (path.Contains(furniturePrefix))
            {
                int startIndex = path.IndexOf(furniturePrefix) + furniturePrefix.Length;
                int endIndex = path.IndexOf("/", startIndex);

                if (endIndex > startIndex)
                {
                    return path.Substring(startIndex, endIndex - startIndex);
                }
                else if (startIndex < path.Length)
                {
                    return path.Substring(startIndex);
                }
            }

            return null;
        }

        [MenuItem("CyanNook/Animation/Extract All Animation Clips", true)]
        private static bool ValidateExtractAll()
        {
            return Directory.Exists("Assets/Animations");
        }

        [MenuItem("CyanNook/Animation/Extract All Furniture Animation Clips", true)]
        private static bool ValidateExtractAllFurniture()
        {
            return Directory.Exists(FurnitureRoot);
        }
    }
}
