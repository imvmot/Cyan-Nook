using UnityEngine;
using UnityEngine.Timeline;
using System;
using System.Collections.Generic;
using CyanNook.Core;

namespace CyanNook.Character
{
    /// <summary>
    /// 感情タイプとFacial Timelineの紐付けデータ
    /// ScriptableObjectとして各キャラクターごとに設定
    /// </summary>
    [CreateAssetMenu(fileName = "FacialTimelineData", menuName = "CyanNook/Facial Timeline Data")]
    public class FacialTimelineData : ScriptableObject
    {
        [Header("Emotion → Facial Timeline")]
        [Tooltip("各感情に対応するFacial Timelineアセット")]
        public List<EmotionTimelineBinding> emotionBindings = new List<EmotionTimelineBinding>();

        [Header("Blend Emotion → Facial Timeline")]
        [Tooltip("隣接感情ブレンド用のFacial Timelineアセット（円環モデル準拠）")]
        public List<BlendEmotionTimelineBinding> blendBindings = new List<BlendEmotionTimelineBinding>();

        /// <summary>
        /// 感情タイプに対応するTimelineアセットを取得
        /// </summary>
        public TimelineAsset GetTimeline(EmotionType emotionType)
        {
            foreach (var binding in emotionBindings)
            {
                if (binding.emotionType == emotionType)
                {
                    return binding.timeline;
                }
            }
            return null;
        }

        /// <summary>
        /// ブレンド感情ペアに対応するTimelineアセットを取得（順序不問）
        /// </summary>
        public TimelineAsset GetBlendTimeline(EmotionType a, EmotionType b)
        {
            foreach (var binding in blendBindings)
            {
                if ((binding.emotionA == a && binding.emotionB == b) ||
                    (binding.emotionA == b && binding.emotionB == a))
                {
                    return binding.timeline;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// 感情タイプとTimelineの紐付け
    /// </summary>
    [Serializable]
    public class EmotionTimelineBinding
    {
        [Tooltip("感情タイプ")]
        public EmotionType emotionType;

        [Tooltip("対応するFacial Timelineアセット")]
        public TimelineAsset timeline;
    }

    /// <summary>
    /// ブレンド感情ペアとTimelineの紐付け
    /// ラッセルの感情円環モデルで隣接する感情ペア用
    /// </summary>
    [Serializable]
    public class BlendEmotionTimelineBinding
    {
        [Tooltip("感情A")]
        public EmotionType emotionA;

        [Tooltip("感情B")]
        public EmotionType emotionB;

        [Tooltip("対応するブレンドFacial Timelineアセット")]
        public TimelineAsset timeline;
    }
}
