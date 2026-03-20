using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using System.IO;
using System.Collections.Generic;
using CyanNook.Character;

namespace CyanNook.Editor
{
    /// <summary>
    /// キャラクター用Timelineアセットを自動生成するエディタツール
    /// </summary>
    public static class TimelineCreator
    {
        [MenuItem("CyanNook/Animation/Create Timelines for Character")]
        public static void CreateTimelinesMenu()
        {
            // 選択からキャラクター名を推定
            var selected = Selection.activeObject;
            string characterName = "chr001"; // デフォルト

            if (selected != null)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (path.Contains("/Animations/"))
                {
                    int startIndex = path.IndexOf("/Animations/") + "/Animations/".Length;
                    int endIndex = path.IndexOf("/", startIndex);
                    if (endIndex > startIndex)
                    {
                        characterName = path.Substring(startIndex, endIndex - startIndex);
                    }
                }
            }

            CreateTimelinesForCharacter(characterName);
        }

        /// <summary>
        /// 指定キャラクター用のTimelineアセットを作成
        /// </summary>
        public static void CreateTimelinesForCharacter(string characterName)
        {
            string clipsFolder = $"Assets/Animations/{characterName}/Clips";
            string timelinesFolder = $"Assets/Animations/{characterName}/Timelines";
            string bindingDataPath = $"Assets/Animations/{characterName}/{characterName}_TimelineBindings.asset";

            // Clipsフォルダが存在しない場合は先にExtractを実行
            if (!Directory.Exists(clipsFolder))
            {
                Debug.Log($"[TimelineCreator] Clips folder not found, extracting clips first...");
                AnimationClipExtractor.ExtractClipsForCharacter(characterName);
                AssetDatabase.Refresh();
            }

            // Timelinesフォルダを作成
            if (!Directory.Exists(timelinesFolder))
            {
                Directory.CreateDirectory(timelinesFolder);
            }

            // Animation Clipsを読み込み
            var clips = LoadAnimationClips(clipsFolder, characterName);

            // TimelineBindingDataを作成または取得
            var bindingData = AssetDatabase.LoadAssetAtPath<TimelineBindingData>(bindingDataPath);
            if (bindingData == null)
            {
                bindingData = ScriptableObject.CreateInstance<TimelineBindingData>();
                AssetDatabase.CreateAsset(bindingData, bindingDataPath);
            }

            // 各ステートのTimelineを作成
            CreateStateTimeline(timelinesFolder, bindingData, characterName, AnimationStateType.Idle, clips);
            CreateStateTimeline(timelinesFolder, bindingData, characterName, AnimationStateType.Walk, clips);
            CreateStateTimeline(timelinesFolder, bindingData, characterName, AnimationStateType.Run, clips);

            // クリップバリアントを登録
            foreach (var kvp in clips)
            {
                bindingData.AddClipVariant(kvp.Key, kvp.Value);
            }

            EditorUtility.SetDirty(bindingData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TimelineCreator] Timelines created for: {characterName}");
            Debug.Log($"[TimelineCreator] Binding data: {bindingDataPath}");
            EditorUtility.DisplayDialog("Complete",
                $"Timelines created at:\n{timelinesFolder}\n\nBinding data:\n{bindingDataPath}", "OK");
        }

        /// <summary>
        /// 単一ステートのTimelineを作成
        /// </summary>
        private static void CreateStateTimeline(
            string folder,
            TimelineBindingData bindingData,
            string characterName,
            AnimationStateType state,
            Dictionary<string, AnimationClip> clips)
        {
            string timelinePath = $"{folder}/TL_{state}.playable";

            // 既存のTimelineがあればスキップ
            var existingTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            if (existingTimeline != null)
            {
                Debug.Log($"[TimelineCreator] Timeline already exists: {timelinePath}");
                bindingData.SetBinding(state, existingTimeline);
                return;
            }

            // 対応するクリップを探す
            string clipNamePattern = state switch
            {
                AnimationStateType.Idle => "idle",
                AnimationStateType.Walk => "walk",
                AnimationStateType.Run => "run",
                _ => state.ToString().ToLower()
            };

            AnimationClip defaultClip = null;
            foreach (var kvp in clips)
            {
                if (kvp.Key.Contains(clipNamePattern))
                {
                    defaultClip = kvp.Value;
                    break;
                }
            }

            // Timelineアセットを作成
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            // Animation Trackを追加
            var animTrack = timeline.CreateTrack<AnimationTrack>(null, $"{state} Animation");

            // クリップがあれば配置
            if (defaultClip != null)
            {
                var timelineClip = animTrack.CreateClip(defaultClip);
                timelineClip.displayName = defaultClip.name;
                timelineClip.duration = defaultClip.length;
            }

            // Signal Track（イベント用）を追加
            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "Events");

            // Timelineを保存
            AssetDatabase.CreateAsset(timeline, timelinePath);

            // バインディングデータに登録
            bindingData.SetBinding(state, timeline, defaultClip);

            Debug.Log($"[TimelineCreator] Created Timeline: {timelinePath}");
        }

        /// <summary>
        /// Clipsフォルダからアニメーションクリップを読み込み
        /// </summary>
        private static Dictionary<string, AnimationClip> LoadAnimationClips(string clipsFolder, string characterName)
        {
            var clips = new Dictionary<string, AnimationClip>();

            if (!Directory.Exists(clipsFolder))
            {
                Debug.LogWarning($"[TimelineCreator] Clips folder not found: {clipsFolder}");
                return clips;
            }

            var clipFiles = Directory.GetFiles(clipsFolder, "*.anim");
            foreach (var clipFile in clipFiles)
            {
                string assetPath = clipFile.Replace("\\", "/");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clip != null)
                {
                    clips[clip.name] = clip;
                    Debug.Log($"[TimelineCreator] Loaded clip: {clip.name}");
                }
            }

            return clips;
        }

        [MenuItem("CyanNook/Animation/Create Timelines for Character", true)]
        private static bool ValidateCreateTimelines()
        {
            return Directory.Exists("Assets/Animations");
        }
    }
}
