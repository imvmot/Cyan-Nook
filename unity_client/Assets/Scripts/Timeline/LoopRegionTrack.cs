using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// ループ領域を定義するカスタムTimelineトラック。
    /// バインド不要。LoopRegionClipの配置位置からループ領域を決定する。
    ///
    /// 使用方法:
    /// 1. TimelineにLoopRegionTrackを追加
    /// 2. LoopRegionClipをlpアニメーションクリップと同じ位置・長さに配置
    /// 3. CharacterAnimationControllerが自動的にループ領域を読み取る
    /// </summary>
    [TrackColor(0.2f, 0.8f, 0.4f)]
    [TrackClipType(typeof(LoopRegionClip))]
    public class LoopRegionTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, UnityEngine.GameObject go, int inputCount)
        {
            return ScriptPlayable<LoopRegionBehaviour>.Create(graph, inputCount);
        }
    }
}
