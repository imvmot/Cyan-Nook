using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// AdditiveCancelClip
    /// クリップ配置フレームで加算解除＋慣性補間開始。
    /// クリップの長さがそのまま補間時間になる。
    ///
    /// overrideBones を空のままにしておくと、
    /// 実行時に AdditiveOverrideHelper が管理している加算ボーンがそのまま使われる。
    /// 加算先タイムラインごとに別ボーンを補間したい場合のみ明示指定する。
    /// </summary>
    [System.Serializable]
    public class AdditiveCancelClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("補間対象のHumanoidボーン。空欄の場合は実行時の加算ボーンをそのまま使う。")]
        [HumanBoneSelect]
        public List<HumanBodyBones> overrideBones = new List<HumanBodyBones>();

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<AdditiveCancelBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.overrideBones = overrideBones;
            return playable;
        }
    }
}
