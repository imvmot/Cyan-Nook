using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// InertialBlendクリップ
    /// クリップの長さが減衰時間を決定する（クリップ終了時に99%収束）
    /// </summary>
    [System.Serializable]
    public class InertialBlendClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("慣性補間を適用するHumanoidボーン")]
        [HumanBoneSelect]
        public List<HumanBodyBones> targetBones = InitAllBones();

        private static List<HumanBodyBones> InitAllBones()
        {
            var list = new List<HumanBodyBones>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                if (bone == HumanBodyBones.LeftEye || bone == HumanBodyBones.RightEye || bone == HumanBodyBones.Jaw)
                    continue;
                list.Add(bone);
            }
            return list;
        }

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<InertialBlendBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.targetBones = targetBones;
            return playable;
        }
    }
}
