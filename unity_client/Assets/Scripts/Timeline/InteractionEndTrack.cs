using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// インタラクション完了ポイントを定義するカスタムTimelineトラック。
    /// バインド不要。InteractionEndClipの配置位置からインタラクション完了タイミングを決定する。
    ///
    /// 使用方法:
    /// 1. TimelineにInteractionEndTrackを追加
    /// 2. InteractionEndClipをタイムライン終了付近に配置
    /// 3. CharacterAnimationControllerが自動的に完了タイミングを読み取る
    /// </summary>
    [TrackColor(0.8f, 0.4f, 0.2f)]
    [TrackClipType(typeof(InteractionEndClip))]
    public class InteractionEndTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, UnityEngine.GameObject go, int inputCount)
        {
            return ScriptPlayable<InteractionEndBehaviour>.Create(graph, inputCount);
        }
    }
}
