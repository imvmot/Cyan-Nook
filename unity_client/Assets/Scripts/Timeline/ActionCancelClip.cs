using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// キャンセル可能区間を定義するクリップ。
    /// クリップの持続期間中はキャンセルして別のTimelineに即時遷移可能。
    /// allowedTransitionsに遷移先のTimelineアセットを設定する。
    /// </summary>
    [System.Serializable]
    public class ActionCancelClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("キャンセル時に遷移可能なTimelineリスト")]
        public List<TimelineAsset> allowedTransitions = new List<TimelineAsset>();

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<ActionCancelBehaviour>.Create(graph);
        }
    }
}
