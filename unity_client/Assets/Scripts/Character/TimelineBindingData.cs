using UnityEngine;
using UnityEngine.Timeline;
using System;
using System.Collections.Generic;

namespace CyanNook.Character
{
    /// <summary>
    /// ステートとTimelineアセット、AnimationClipの紐付けデータ
    /// ScriptableObjectとして各キャラクターごとに設定
    /// </summary>
    [CreateAssetMenu(fileName = "TimelineBindingData", menuName = "CyanNook/Timeline Binding Data")]
    public class TimelineBindingData : ScriptableObject
    {
        [Header("Timeline Bindings")]
        [Tooltip("各ステートに対応するTimelineアセット")]
        public List<StateTimelineBinding> stateBindings = new List<StateTimelineBinding>();

        [Header("Animation ID Bindings")]
        [Tooltip("アニメーションIDに対応するTimelineアセット（interact_sit01など）")]
        public List<AnimationIdTimelineBinding> animationIdBindings = new List<AnimationIdTimelineBinding>();

        [Header("Animation Clip Variants")]
        [Tooltip("差し替え可能なAnimationClipのバリアント")]
        public List<AnimationClipVariant> clipVariants = new List<AnimationClipVariant>();

        /// <summary>
        /// ステートに対応するTimelineアセットを取得
        /// </summary>
        public TimelineAsset GetTimeline(AnimationStateType state)
        {
            foreach (var binding in stateBindings)
            {
                if (binding.state == state)
                {
                    return binding.timeline;
                }
            }
            return null;
        }

        /// <summary>
        /// アニメーションIDに対応するTimelineアセットを取得
        /// </summary>
        public TimelineAsset GetTimelineByAnimationId(string animationId)
        {
            if (string.IsNullOrEmpty(animationId)) return null;

            foreach (var binding in animationIdBindings)
            {
                if (binding.animationId == animationId)
                {
                    return binding.timeline;
                }
            }
            return null;
        }

        /// <summary>
        /// アニメーションIDとTimelineの紐付けを追加
        /// </summary>
        public void SetAnimationIdBinding(string animationId, TimelineAsset timeline)
        {
            for (int i = 0; i < animationIdBindings.Count; i++)
            {
                if (animationIdBindings[i].animationId == animationId)
                {
                    animationIdBindings[i] = new AnimationIdTimelineBinding
                    {
                        animationId = animationId,
                        timeline = timeline
                    };
                    return;
                }
            }

            animationIdBindings.Add(new AnimationIdTimelineBinding
            {
                animationId = animationId,
                timeline = timeline
            });
        }

        /// <summary>
        /// バリアント名からAnimationClipを取得
        /// </summary>
        public AnimationClip GetAnimationClip(string variantName)
        {
            foreach (var variant in clipVariants)
            {
                if (variant.variantName == variantName)
                {
                    return variant.clip;
                }
            }
            return null;
        }

        /// <summary>
        /// ステートに対応するデフォルトのAnimationClipを取得
        /// </summary>
        public AnimationClip GetDefaultClipForState(AnimationStateType state)
        {
            foreach (var binding in stateBindings)
            {
                if (binding.state == state)
                {
                    return binding.defaultClip;
                }
            }
            return null;
        }

        /// <summary>
        /// 指定ステートのバインディングを追加または更新
        /// </summary>
        public void SetBinding(AnimationStateType state, TimelineAsset timeline, AnimationClip defaultClip = null)
        {
            for (int i = 0; i < stateBindings.Count; i++)
            {
                if (stateBindings[i].state == state)
                {
                    stateBindings[i] = new StateTimelineBinding
                    {
                        state = state,
                        timeline = timeline,
                        defaultClip = defaultClip ?? stateBindings[i].defaultClip
                    };
                    return;
                }
            }

            stateBindings.Add(new StateTimelineBinding
            {
                state = state,
                timeline = timeline,
                defaultClip = defaultClip
            });
        }

        /// <summary>
        /// クリップバリアントを追加
        /// </summary>
        public void AddClipVariant(string variantName, AnimationClip clip)
        {
            foreach (var variant in clipVariants)
            {
                if (variant.variantName == variantName)
                {
                    return; // 既に存在
                }
            }

            clipVariants.Add(new AnimationClipVariant
            {
                variantName = variantName,
                clip = clip
            });
        }
    }

    /// <summary>
    /// ステートとTimelineの紐付け
    /// </summary>
    [Serializable]
    public class StateTimelineBinding
    {
        [Tooltip("アニメーションステート")]
        public AnimationStateType state;

        [Tooltip("対応するTimelineアセット")]
        public TimelineAsset timeline;

        [Tooltip("デフォルトで使用するAnimationClip")]
        public AnimationClip defaultClip;
    }

    /// <summary>
    /// AnimationClipのバリアント（差し替え用）
    /// </summary>
    [Serializable]
    public class AnimationClipVariant
    {
        [Tooltip("バリアント名（例: walk_forward, walk_left）")]
        public string variantName;

        [Tooltip("AnimationClip")]
        public AnimationClip clip;
    }

    /// <summary>
    /// アニメーションIDとTimelineの紐付け
    /// </summary>
    [Serializable]
    public class AnimationIdTimelineBinding
    {
        [Tooltip("アニメーションID（例: interact_sit01）")]
        public string animationId;

        [Tooltip("対応するTimelineアセット")]
        public TimelineAsset timeline;
    }
}
