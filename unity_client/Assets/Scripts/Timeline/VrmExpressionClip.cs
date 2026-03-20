using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UniVRM10;

namespace CyanNook.Timeline
{
    /// <summary>
    /// VRM Expressionクリップ
    /// 指定したVRM ExpressionをAnimationCurveで制御する
    ///
    /// カーブソース:
    /// - sourceClip が設定されている場合: sourceClipからBakeしたカーブを使用
    /// - sourceClip が未設定の場合: curve フィールドを直接編集して使用
    /// カーブのX軸は正規化時間（0～1）、Y軸はExpression weight（0～1）
    /// </summary>
    [System.Serializable]
    public class VrmExpressionClip : PlayableAsset, ITimelineClipAsset
    {
        [Header("Expression")]
        [Tooltip("制御するVRM Expression")]
        public ExpressionPreset expressionPreset = ExpressionPreset.blink;

        [Tooltip("カスタムExpression名（expressionPreset が custom の場合に使用）")]
        public string customExpressionName;

        [Header("Curve Source")]
        [Tooltip("カーブのソースとなるAnimationClip（任意）。設定時はBakeボタンでカーブを抽出")]
        public AnimationClip sourceClip;

        [Tooltip("sourceClipから抽出するBlendShapeカーブのプロパティパス")]
        public string sourceCurveProperty;

        [Tooltip("Bake時にカーブ値に適用するスケール（Blender出力は0-100のため、デフォルト0.01で0-1に変換）")]
        public float bakeScale = 0.01f;

        [Header("Curve")]
        [Tooltip("Expression weightカーブ（X: 正規化時間 0～1、Y: weight 0～1）")]
        public AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f)
        );

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<VrmExpressionBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.expressionPreset = expressionPreset;
            behaviour.customExpressionName = customExpressionName;
            behaviour.curve = curve;

            return playable;
        }
    }
}
