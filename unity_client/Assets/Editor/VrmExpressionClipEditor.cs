using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using UniVRM10;
using CyanNook.Timeline;
using System.Linq;

namespace CyanNook.Editor
{
    /// <summary>
    /// VrmExpressionClipのカスタムエディタ
    /// sourceClipからのカーブ抽出（Bake）機能を提供
    /// </summary>
    [CustomEditor(typeof(VrmExpressionClip))]
    public class VrmExpressionClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var clip = (VrmExpressionClip)target;

            // --- Expression ---
            EditorGUILayout.PropertyField(serializedObject.FindProperty("expressionPreset"));

            var presetProp = serializedObject.FindProperty("expressionPreset");
            // ExpressionPreset.custom の enum value
            if (presetProp.enumValueIndex == (int)ExpressionPreset.custom)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("customExpressionName"));
            }

            EditorGUILayout.Space();

            // --- Source Clip ---
            var sourceClipProp = serializedObject.FindProperty("sourceClip");
            EditorGUILayout.PropertyField(sourceClipProp);

            var sourceClip = sourceClipProp.objectReferenceValue as AnimationClip;
            if (sourceClip != null)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bakeScale"));
                DrawSourceCurveSelector(sourceClip);
            }

            EditorGUILayout.Space();

            // --- Curve ---
            EditorGUILayout.PropertyField(serializedObject.FindProperty("curve"));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSourceCurveSelector(AnimationClip sourceClip)
        {
            // BlendShapeカーブのみ抽出
            var bindings = AnimationUtility.GetCurveBindings(sourceClip);
            var blendShapeBindings = bindings
                .Where(b => b.propertyName.StartsWith("blendShape."))
                .ToArray();

            if (blendShapeBindings.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Source clip に BlendShape カーブが含まれていません。",
                    MessageType.Warning);
                return;
            }

            // カーブ名リスト
            var curveNames = blendShapeBindings
                .Select(b => b.propertyName.Replace("blendShape.", ""))
                .ToArray();

            // 現在の選択を取得
            var sourceCurveProp = serializedObject.FindProperty("sourceCurveProperty");
            string currentProperty = sourceCurveProp.stringValue;

            // 選択中のプロパティが存在するか確認
            int currentIndex = System.Array.FindIndex(blendShapeBindings,
                b => b.propertyName == currentProperty);

            if (currentIndex < 0)
            {
                // 以前選択したカーブが見つからない
                if (!string.IsNullOrEmpty(currentProperty))
                {
                    string missingName = currentProperty.Replace("blendShape.", "");
                    EditorGUILayout.HelpBox(
                        $"選択中のカーブ \"{missingName}\" が Source clip に存在しません。\n" +
                        "AnimationClip が更新された可能性があります。別のカーブを選択してください。",
                        MessageType.Warning);
                }
                currentIndex = 0;
                // 先頭カーブをデフォルトとして保存
                sourceCurveProp.stringValue = blendShapeBindings[0].propertyName;
            }

            // ドロップダウン表示
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup("Source Curve", currentIndex, curveNames);
            if (EditorGUI.EndChangeCheck())
            {
                sourceCurveProp.stringValue = blendShapeBindings[newIndex].propertyName;
            }

            // Bakeボタン
            if (GUILayout.Button("Bake Curve from Source"))
            {
                // 現在のドロップダウン選択を確実に保存
                sourceCurveProp.stringValue = blendShapeBindings[newIndex].propertyName;
                // SerializedObjectの保留中の変更を先に確定
                serializedObject.ApplyModifiedProperties();

                var expressionClip = (VrmExpressionClip)target;
                BakeCurveForClip(expressionClip);

                // BakeCurveForClipが直接変更した値をSerializedObjectに反映
                serializedObject.Update();

                // 末尾のApplyModifiedPropertiesで古い値に戻されるのを防ぐためGUI描画を中断
                GUIUtility.ExitGUI();
            }
        }

        // =====================================================================
        // Static Bake Utility
        // =====================================================================

        /// <summary>
        /// VrmExpressionClipのsourceClipからカーブをBakeする
        /// Inspector Bakeボタンと一括Bakeの両方から使用
        /// </summary>
        /// <returns>Bake成功時 true</returns>
        public static bool BakeCurveForClip(VrmExpressionClip clip)
        {
            if (clip.sourceClip == null)
            {
                Debug.LogWarning($"[VrmExpressionClipEditor] sourceClip が未設定: {clip.name}");
                return false;
            }

            if (string.IsNullOrEmpty(clip.sourceCurveProperty))
            {
                Debug.LogWarning($"[VrmExpressionClipEditor] sourceCurveProperty が未設定: {clip.name}");
                return false;
            }

            // sourceClipからバインディングを検索
            var bindings = AnimationUtility.GetCurveBindings(clip.sourceClip);
            var binding = bindings.FirstOrDefault(b => b.propertyName == clip.sourceCurveProperty);

            if (string.IsNullOrEmpty(binding.propertyName))
            {
                string missingName = clip.sourceCurveProperty.Replace("blendShape.", "");
                Debug.LogWarning($"[VrmExpressionClipEditor] カーブ \"{missingName}\" が sourceClip に存在しません: {clip.name}");
                return false;
            }

            var sourceCurve = AnimationUtility.GetEditorCurve(clip.sourceClip, binding);
            if (sourceCurve == null)
            {
                Debug.LogWarning($"[VrmExpressionClipEditor] カーブの取得に失敗: {clip.name}");
                return false;
            }

            float duration = clip.sourceClip.length;
            if (duration <= 0f)
            {
                Debug.LogWarning($"[VrmExpressionClipEditor] sourceClip の長さが 0: {clip.name}");
                return false;
            }

            float bakeScale = clip.bakeScale;

            // 時間を 0～1 に正規化 + 値にスケール適用
            var normalizedCurve = new AnimationCurve();
            foreach (var key in sourceCurve.keys)
            {
                var newKey = new Keyframe
                {
                    time = key.time / duration,
                    value = key.value * bakeScale,
                    inTangent = key.inTangent * duration * bakeScale,
                    outTangent = key.outTangent * duration * bakeScale,
                    inWeight = key.inWeight,
                    outWeight = key.outWeight,
                    weightedMode = key.weightedMode
                };
                normalizedCurve.AddKey(newKey);
            }

            normalizedCurve.preWrapMode = sourceCurve.preWrapMode;
            normalizedCurve.postWrapMode = sourceCurve.postWrapMode;

            // Undo対応 + 値の適用
            Undo.RecordObject(clip, "Bake VRM Expression Curve");
            clip.curve = normalizedCurve;
            EditorUtility.SetDirty(clip);

            string curveName = clip.sourceCurveProperty.Replace("blendShape.", "");
            Debug.Log($"[VrmExpressionClipEditor] Bake 完了: {clip.name} / {curveName} ({sourceCurve.keys.Length} keys, {duration}s → normalized 0-1, scale: {bakeScale})");
            return true;
        }

        // =====================================================================
        // Menu Command: 一括Bake
        // =====================================================================

        [MenuItem("CyanNook/Animation/Rebake All VRM Expression Curves")]
        public static void RebakeAllVrmExpressionCurves()
        {
            // プロジェクト内の全TimelineAssetを検索
            var timelineGuids = AssetDatabase.FindAssets("t:TimelineAsset");

            int totalClips = 0;
            int bakedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            foreach (var guid in timelineGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                if (timeline == null) continue;

                foreach (var track in timeline.GetOutputTracks())
                {
                    if (track is not VrmExpressionTrack) continue;

                    foreach (var timelineClip in track.GetClips())
                    {
                        var expressionClip = timelineClip.asset as VrmExpressionClip;
                        if (expressionClip == null) continue;

                        totalClips++;

                        // sourceClipが未設定のクリップはスキップ（手動カーブ編集のため）
                        if (expressionClip.sourceClip == null || string.IsNullOrEmpty(expressionClip.sourceCurveProperty))
                        {
                            skippedCount++;
                            continue;
                        }

                        if (BakeCurveForClip(expressionClip))
                        {
                            bakedCount++;
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                }
            }

            AssetDatabase.SaveAssets();

            string message = $"Rebake 完了\n\n" +
                             $"検出: {totalClips} clips\n" +
                             $"Bake成功: {bakedCount}\n" +
                             $"スキップ（sourceClip未設定）: {skippedCount}\n" +
                             $"エラー: {errorCount}";

            Debug.Log($"[VrmExpressionClipEditor] {message.Replace("\n", " ")}");
            EditorUtility.DisplayDialog("Rebake All VRM Expression Curves", message, "OK");
        }
    }
}
