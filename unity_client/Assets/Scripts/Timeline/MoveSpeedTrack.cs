using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CyanNook.Character;

namespace CyanNook.Timeline
{
    /// <summary>
    /// NavMeshAgentの移動速度をAnimationCurveで制御するトラック
    /// バインディング: CharacterNavigationController
    /// Walk/Run Timelineのst区間に配置して徐々に加速する表現等に使用
    /// </summary>
    [TrackColor(0.8f, 0.6f, 0.2f)]
    [TrackClipType(typeof(MoveSpeedClip))]
    [TrackBindingType(typeof(CharacterNavigationController))]
    public class MoveSpeedTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<MoveSpeedMixerBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// MoveSpeed PlayableBehaviour
    /// クリップの正規化時間からカーブ値を読み取り、NavigationControllerの速度乗算値を設定
    /// </summary>
    [System.Serializable]
    public class MoveSpeedBehaviour : PlayableBehaviour
    {
        [HideInInspector]
        public AnimationCurve speedCurve;

        [HideInInspector]
        public bool adjustAnimatorSpeed;

        private CharacterNavigationController _navController;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var navController = playerData as CharacterNavigationController;
            if (navController == null) return;

            _navController = navController;

            // 正規化時間からカーブ値を取得
            double time = playable.GetTime();
            double duration = playable.GetDuration();
            float normalizedTime = duration > 0 ? (float)(time / duration) : 1f;

            float multiplier = speedCurve != null ? speedCurve.Evaluate(normalizedTime) : 1f;

            navController.SetMoveSpeedMultiplier(multiplier, adjustAnimatorSpeed);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            // クリップ終了時にデフォルト値に戻す
            if (_navController != null)
            {
                _navController.SetMoveSpeedMultiplier(1f, true);
            }
        }
    }

    /// <summary>
    /// MoveSpeed Mixer Behaviour
    /// ランタイムデータの伝播は不要なため空実装
    /// </summary>
    public class MoveSpeedMixerBehaviour : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // 処理はBehaviour側で行う
        }
    }
}
