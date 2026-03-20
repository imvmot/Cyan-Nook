using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// インタラクション完了ポイントを定義するクリップ。
    /// クリップの開始位置がInteractionEnd発火タイミングとなる。
    /// LoopRegionを持たないInteractタイムライン（interact_exit等）の完了検知に使用。
    ///
    /// 使用方法:
    /// 1. TimelineにInteractionEndTrackを追加
    /// 2. InteractionEndClipをタイムライン終了付近に配置
    /// 3. CharacterAnimationControllerが自動的に発火タイミングを読み取る
    /// </summary>
    [System.Serializable]
    public class InteractionEndClip : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<InteractionEndBehaviour>.Create(graph);
        }
    }
}
