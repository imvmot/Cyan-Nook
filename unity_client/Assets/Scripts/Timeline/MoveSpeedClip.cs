using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// MoveSpeedクリップ
    /// 移動速度の乗算カーブとアニメーション速度調整のON/OFFを設定
    ///
    /// 使用例:
    /// - st区間: speedCurve (0→0.1, 1→1.0), adjustAnimatorSpeed=false（歩き開始を定速再生）
    /// - lp区間: speedCurve Linear(1.0), adjustAnimatorSpeed=true（Agent速度にアニメ追従）
    /// </summary>
    [System.Serializable]
    public class MoveSpeedClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("移動速度の乗算カーブ（0.0〜1.0で正規化時間、値が速度倍率）")]
        public AnimationCurve speedCurve = AnimationCurve.Linear(0f, 0.1f, 1f, 1f);

        [Tooltip("アニメーション再生速度をAgent速度に合わせるか（false=定速再生）")]
        public bool adjustAnimatorSpeed = true;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<MoveSpeedBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.speedCurve = speedCurve;
            behaviour.adjustAnimatorSpeed = adjustAnimatorSpeed;

            return playable;
        }
    }
}
