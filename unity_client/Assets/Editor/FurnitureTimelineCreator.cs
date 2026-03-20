using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using System.IO;
using System.Collections.Generic;
using CyanNook.Furniture;

namespace CyanNook.Editor
{
    /// <summary>
    /// 家具用Timelineアセットを自動生成するエディタツール
    /// Clips内のAnimationClipごとにTimelineを作成し、FurnitureTimelineBindingDataに登録する
    /// </summary>
    public static class FurnitureTimelineCreator
    {
        private const string FurnitureAnimRoot = "Assets/Animations/Furniture";

        [MenuItem("CyanNook/Animation/Create Timelines for All Furniture")]
        public static void CreateTimelinesForAllFurniture()
        {
            if (!Directory.Exists(FurnitureAnimRoot))
            {
                EditorUtility.DisplayDialog("Error", $"Furniture animations folder not found: {FurnitureAnimRoot}", "OK");
                return;
            }

            int totalCreated = 0;
            var furnitureFolders = Directory.GetDirectories(FurnitureAnimRoot);
            foreach (var folder in furnitureFolders)
            {
                string furnitureId = Path.GetFileName(folder);
                totalCreated += CreateTimelinesForFurniture(furnitureId);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Complete", $"Created {totalCreated} furniture timelines.", "OK");
        }

        [MenuItem("CyanNook/Animation/Create Timelines for Selected Furniture")]
        public static void CreateTimelinesForSelectedFurniture()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a furniture folder or asset.", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            string furnitureId = ExtractFurnitureIdFromPath(path);

            if (string.IsNullOrEmpty(furnitureId))
            {
                EditorUtility.DisplayDialog("Error", "Could not determine furniture ID from selection.\nExpected path: Assets/Animations/Furniture/{FurnitureID}/...", "OK");
                return;
            }

            int created = CreateTimelinesForFurniture(furnitureId);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Complete", $"Created {created} timelines for furniture {furnitureId}.", "OK");
        }

        /// <summary>
        /// 指定家具のTimelineを作成
        /// </summary>
        public static int CreateTimelinesForFurniture(string furnitureId)
        {
            string furnitureFolder = $"{FurnitureAnimRoot}/{furnitureId}";
            string clipsFolder = $"{furnitureFolder}/Clips";
            string timelinesFolder = $"{furnitureFolder}/Timelines";
            string bindingDataPath = $"{furnitureFolder}/{furnitureId}_FurnitureTimelineBindings.asset";

            // Clipsフォルダが存在しない場合は先にExtract
            if (!Directory.Exists(clipsFolder))
            {
                Debug.Log($"[FurnitureTimelineCreator] Clips folder not found, extracting clips first...");
                AnimationClipExtractor.ExtractClipsForFurniture(furnitureId);
                AssetDatabase.Refresh();
            }

            if (!Directory.Exists(clipsFolder))
            {
                Debug.LogWarning($"[FurnitureTimelineCreator] No clips found for: {furnitureId}");
                return 0;
            }

            // Timelinesフォルダを作成
            if (!Directory.Exists(timelinesFolder))
            {
                Directory.CreateDirectory(timelinesFolder);
            }

            // FurnitureTimelineBindingDataを作成または取得
            var bindingData = AssetDatabase.LoadAssetAtPath<FurnitureTimelineBindingData>(bindingDataPath);
            if (bindingData == null)
            {
                bindingData = ScriptableObject.CreateInstance<FurnitureTimelineBindingData>();
                bindingData.furnitureId = furnitureId;
                AssetDatabase.CreateAsset(bindingData, bindingDataPath);
            }

            // Clipを読み込み
            var clipFiles = Directory.GetFiles(clipsFolder, "*.anim");
            int createdCount = 0;

            foreach (var clipFile in clipFiles)
            {
                string clipAssetPath = clipFile.Replace("\\", "/");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath);
                if (clip == null) continue;

                // Clip名からアニメーションIDを推定
                // 例: chr001_anim_interact_exit01_Door01 → interact_exit01
                string animationId = ExtractAnimationIdFromClipName(clip.name, furnitureId);
                if (string.IsNullOrEmpty(animationId))
                {
                    Debug.LogWarning($"[FurnitureTimelineCreator] Could not extract animation ID from: {clip.name}");
                    continue;
                }

                // Timeline作成
                string timelineName = $"TL_{furnitureId}_{animationId}";
                string timelinePath = $"{timelinesFolder}/{timelineName}.playable";

                // 既存チェック
                var existingTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
                if (existingTimeline != null)
                {
                    Debug.Log($"[FurnitureTimelineCreator] Timeline already exists: {timelinePath}");
                    bindingData.SetBinding(animationId, existingTimeline);
                    continue;
                }

                // Timelineアセットを作成
                var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

                // AnimationTrackを追加
                var animTrack = timeline.CreateTrack<AnimationTrack>(null, $"{furnitureId} Animation");
                var timelineClip = animTrack.CreateClip(clip);
                timelineClip.displayName = clip.name;
                timelineClip.duration = clip.length;

                AssetDatabase.CreateAsset(timeline, timelinePath);
                bindingData.SetBinding(animationId, timeline);
                createdCount++;

                Debug.Log($"[FurnitureTimelineCreator] Created: {timelinePath} (animationId: {animationId})");
            }

            EditorUtility.SetDirty(bindingData);

            Debug.Log($"[FurnitureTimelineCreator] Created {createdCount} timelines for {furnitureId}");
            return createdCount;
        }

        /// <summary>
        /// Clip名からアニメーションIDを抽出
        /// chr001_anim_interact_exit01_Door01 → interact_exit01
        /// パターン: {char}_anim_{animationId}_{furnitureId}
        /// </summary>
        private static string ExtractAnimationIdFromClipName(string clipName, string furnitureId)
        {
            // 末尾の _FurnitureId を除去
            string suffix = $"_{furnitureId}";
            string name = clipName;
            if (name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - suffix.Length);
            }

            // {char}_anim_ プレフィックスを除去
            int animIndex = name.IndexOf("_anim_");
            if (animIndex >= 0)
            {
                return name.Substring(animIndex + "_anim_".Length);
            }

            // フォールバック: そのまま使用
            return name;
        }

        /// <summary>
        /// パスから家具IDを抽出
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

        [MenuItem("CyanNook/Animation/Create Timelines for All Furniture", true)]
        private static bool ValidateCreateAll()
        {
            return Directory.Exists(FurnitureAnimRoot);
        }
    }
}
