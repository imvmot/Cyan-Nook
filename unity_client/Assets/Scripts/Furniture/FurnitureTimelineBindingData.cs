using UnityEngine;
using UnityEngine.Timeline;
using System;
using System.Collections.Generic;

namespace CyanNook.Furniture
{
    /// <summary>
    /// 家具アニメーションIDとTimelineアセットの紐付けデータ
    /// 各家具タイプ（Door01等）ごとにScriptableObjectとして設定
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureTimelineBindings_", menuName = "CyanNook/Furniture Timeline Binding Data")]
    public class FurnitureTimelineBindingData : ScriptableObject
    {
        [Header("Furniture Info")]
        [Tooltip("家具ID（Door01 など）")]
        public string furnitureId;

        [Header("Timeline Bindings")]
        [Tooltip("アニメーションIDに対応するTimelineアセット")]
        public List<FurnitureTimelineBinding> bindings = new List<FurnitureTimelineBinding>();

        /// <summary>
        /// アニメーションIDに対応するTimelineアセットを取得
        /// </summary>
        public TimelineAsset GetTimeline(string animationId)
        {
            if (string.IsNullOrEmpty(animationId)) return null;

            foreach (var binding in bindings)
            {
                if (binding.animationId == animationId)
                {
                    return binding.timeline;
                }
            }
            return null;
        }

        /// <summary>
        /// アニメーションIDとTimelineの紐付けを追加または更新
        /// </summary>
        public void SetBinding(string animationId, TimelineAsset timeline)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].animationId == animationId)
                {
                    bindings[i] = new FurnitureTimelineBinding
                    {
                        animationId = animationId,
                        timeline = timeline
                    };
                    return;
                }
            }

            bindings.Add(new FurnitureTimelineBinding
            {
                animationId = animationId,
                timeline = timeline
            });
        }

        /// <summary>
        /// 指定アニメーションIDに対応するバインディングが存在するか
        /// </summary>
        public bool HasBinding(string animationId)
        {
            if (string.IsNullOrEmpty(animationId)) return false;

            foreach (var binding in bindings)
            {
                if (binding.animationId == animationId) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// アニメーションIDとTimelineの紐付け
    /// </summary>
    [Serializable]
    public class FurnitureTimelineBinding
    {
        [Tooltip("対応するキャラクターアニメーションID（interact_exit01, interact_entry01 など）")]
        public string animationId;

        [Tooltip("家具用Timelineアセット")]
        public TimelineAsset timeline;
    }
}
