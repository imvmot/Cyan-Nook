using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// キャンセル可能区間を定義するカスタムTimelineトラック。
    /// バインド不要。ActionCancelClipの配置位置からキャンセル可能区間を決定する。
    ///
    /// 使用方法:
    /// 1. TimelineにActionCancelTrackを追加
    /// 2. ActionCancelClipをキャンセル可能区間に配置
    /// 3. クリップのInspectorでallowedTransitions（遷移先Timeline）を設定
    /// 4. CharacterAnimationControllerが自動的にキャンセル可能区間を読み取る
    /// </summary>
    [TrackColor(0.8f, 0.2f, 0.2f)]
    [TrackClipType(typeof(ActionCancelClip))]
    public class ActionCancelTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, UnityEngine.GameObject go, int inputCount)
        {
            return ScriptPlayable<ActionCancelBehaviour>.Create(graph, inputCount);
        }
    }
}
