using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// ループ領域を定義するクリップ。
    /// クリップの配置位置・長さからLoopStart / LoopEnd / EndStartを自動導出する。
    ///
    /// - LoopStart = clip.start
    /// - LoopEnd   = clip.end + loopEndOffsetFrames / frameRate（デフォルト: -1F）
    /// - EndStart  = clip.end
    /// </summary>
    [System.Serializable]
    public class LoopRegionClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("LoopEnd位置のオフセット（フレーム単位）。デフォルト-1でクリップ終了の1F手前。")]
        public int loopEndOffsetFrames = -1;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<LoopRegionBehaviour>.Create(graph);
        }
    }
}
