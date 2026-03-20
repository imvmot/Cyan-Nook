using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UniVRM10;
using CyanNook.Character;
using CyanNook.Core;

namespace CyanNook.Timeline
{
    /// <summary>
    /// VRM Expression制御トラック
    /// Clipが存在する期間のみVRM Expressionを制御する
    /// CharacterExpressionControllerの値を上書きして優先適用
    ///
    /// blendEmotionTag: ブレンドTimeline用。このトラックが対応する感情タイプを指定。
    /// Neutral（デフォルト）= 共通トラック（Blink等）で常にウェイト1.0
    /// それ以外 = ブレンド比率に応じてウェイトが動的制御される
    /// </summary>
    [TrackColor(0.8f, 0.4f, 0.8f)]
    [TrackClipType(typeof(VrmExpressionClip))]
    [TrackBindingType(typeof(CharacterExpressionController))]
    public class VrmExpressionTrack : TrackAsset
    {
        [Tooltip("ブレンドTimeline用: このトラックが対応する感情タイプ。Neutralは常にウェイト1.0")]
        public EmotionType blendEmotionTag = EmotionType.Neutral;

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var playable = ScriptPlayable<VrmExpressionMixerBehaviour>.Create(graph, inputCount);
            playable.GetBehaviour().blendEmotionTag = blendEmotionTag;
            return playable;
        }
    }

    /// <summary>
    /// VRM Expression PlayableBehaviour（クリップごとのパラメータ保持）
    /// </summary>
    [System.Serializable]
    public class VrmExpressionBehaviour : PlayableBehaviour
    {
        public ExpressionPreset expressionPreset;
        public string customExpressionName;
        public AnimationCurve curve;
    }

    /// <summary>
    /// VRM Expression Mixer Behaviour
    /// 全クリップのweightを評価し、VRM ExpressionにSetWeightを適用
    /// PlayableDirectorの評価タイミング（Update後）で実行されるため、
    /// CharacterExpressionControllerのUpdate()より後に書き込み、Timeline側が優先される
    ///
    /// 動作:
    /// - フレーム最初のProcessFrameで全Expressionをゼロリセット（加算の基点）
    /// - Neutral preset: リセット済みのためスキップ（値=0 → ニュートラル顔を維持）
    /// - その他: コントローラーの累積器経由で加算適用（同一Timeline上の複数トラックが加算される）
    /// </summary>
    public class VrmExpressionMixerBehaviour : PlayableBehaviour
    {
        /// <summary>トラックのブレンド感情タグ（VrmExpressionTrackから伝達）</summary>
        public EmotionType blendEmotionTag;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var expressionController = playerData as CharacterExpressionController;
            if (expressionController == null) return;

            var vrmInstance = expressionController.GetVrmInstance();
            if (vrmInstance == null || vrmInstance.Runtime == null) return;

            if (vrmInstance.Runtime.Expression == null) return;

            // フレーム最初のProcessFrameで全Expressionをゼロリセット（加算の基点）
            expressionController.ResetExpressionsForTimelineFrame();

            // ブレンドウェイト取得（Neutral=1.0、非ブレンド時も1.0）
            float trackBlendWeight = expressionController.GetTrackBlendWeight(blendEmotionTag);

            int inputCount = playable.GetInputCount();

            for (int i = 0; i < inputCount; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= 0f) continue;

                var input = (ScriptPlayable<VrmExpressionBehaviour>)playable.GetInput(i);
                var behaviour = input.GetBehaviour();
                if (behaviour.curve == null) continue;

                // Neutral: リセット済みのため追加処理不要（全Expression=0 → ニュートラル状態）
                if (behaviour.expressionPreset == ExpressionPreset.neutral)
                    continue;

                // クリップ内のローカル時間を正規化（0～1）
                double duration = input.GetDuration();
                double time = input.GetTime();
                float normalizedTime = duration > 0 ? (float)(time / duration) : 0f;

                // カーブ評価
                float curveValue = behaviour.curve.Evaluate(normalizedTime);

                // ExpressionKey生成
                ExpressionKey key;
                if (behaviour.expressionPreset == ExpressionPreset.custom)
                {
                    if (string.IsNullOrEmpty(behaviour.customExpressionName)) continue;
                    key = ExpressionKey.CreateCustom(behaviour.customExpressionName);
                }
                else
                {
                    key = ExpressionKey.CreateFromPreset(behaviour.expressionPreset);
                }

                // コントローラー経由で加算（同一Timeline上の複数トラックの値が累積される）
                expressionController.AddTimelineExpression(key, curveValue * weight * trackBlendWeight);
            }
        }
    }
}
