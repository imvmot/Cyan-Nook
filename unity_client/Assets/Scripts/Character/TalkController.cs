using UnityEngine;
using System;

namespace CyanNook.Character
{
    /// <summary>
    /// Talkモードの状態管理を担当
    /// キャラクターをRoomTarget "talk" 位置へ移動させ、talk_lookattargetを注視しながら対話モードに入る
    /// </summary>
    public class TalkController : MonoBehaviour
    {
        [Header("Talk Position")]
        [Tooltip("RoomTargetManagerから'talk'ターゲットを取得")]
        public RoomTargetManager roomTargetManager;

        [Tooltip("フォールバック: RoomTargetに'talk'がない場合の待機位置")]
        public Transform talkPositionFallback;

        [Header("References")]
        public CharacterAnimationController animationController;
        public CharacterNavigationController navigationController;
        public CharacterLookAtController lookAtController;

        [Header("Settings")]
        [Tooltip("Talk開始時にtalk_positionへ移動するか（falseならその場でTalk開始）")]
        public bool moveToTalkPosition = true;

        [Tooltip("Talk終了後にIdleへ戻るか")]
        public bool returnToIdleOnExit = true;

        [Header("Animation IDs")]
        [Tooltip("Talk中の待機アニメーションID")]
        [SerializeField]
        private string _talkIdleAnimationId = "talk_idle01";

        [Tooltip("Thinking中のアニメーションID")]
        [SerializeField]
        private string _thinkingAnimationId = "talk_thinking01";

        [Header("State")]
        [SerializeField]
        private TalkState _currentState = TalkState.None;
        public TalkState CurrentState => _currentState;

        // イベント
        public event Action OnTalkStarted;
        public event Action OnTalkEnded;
        public event Action OnApproachComplete;

        /// <summary>
        /// RoomTargetから'talk'位置を取得。なければフォールバックを使用。
        /// </summary>
        private Transform GetTalkPosition()
        {
            if (roomTargetManager != null)
            {
                var talkTarget = roomTargetManager.GetTarget("talk");
                if (talkTarget != null) return talkTarget.transform;
            }
            return talkPositionFallback;
        }

        /// <summary>
        /// RoomTargetから'talk_lookattarget'を取得。
        /// </summary>
        private Transform GetTalkLookAtTarget()
        {
            if (roomTargetManager != null)
            {
                var talkTarget = roomTargetManager.GetTarget("talk");
                if (talkTarget?.lookAtTarget != null) return talkTarget.lookAtTarget;
            }
            return null;
        }

        /// <summary>
        /// Talkモードを開始
        /// </summary>
        public void EnterTalk()
        {
            if (_currentState != TalkState.None)
            {
                Debug.LogWarning("[TalkController] Already in talk mode or transitioning");
                return;
            }

            Debug.Log("[TalkController] EnterTalk called");

            var talkPos = GetTalkPosition();
            if (moveToTalkPosition && talkPos != null)
            {
                // talk_positionへ移動
                StartApproaching();
            }
            else
            {
                // その場でTalk開始
                StartTalkMode();
            }
        }

        /// <summary>
        /// Talkモードを終了
        /// </summary>
        public void ExitTalk()
        {
            if (_currentState == TalkState.None)
            {
                Debug.LogWarning("[TalkController] Not in talk mode");
                return;
            }

            if (_currentState == TalkState.Exiting)
            {
                Debug.LogWarning("[TalkController] Already exiting");
                return;
            }

            Debug.Log("[TalkController] ExitTalk called");

            // Thinking中の場合はForceStop（ed再生なしで即時復帰→talk_idleに戻る）
            if (animationController != null && animationController.IsThinkingActive)
            {
                animationController.ForceStopThinking();
            }

            _currentState = TalkState.Exiting;

            // 視線を正面に戻す
            if (lookAtController != null)
            {
                lookAtController.LookForward();
            }

            // talk_idle_edを再生（LoopRegionがあれば終了フェーズへ）
            if (animationController != null && animationController.HasLoopRegion)
            {
                animationController.OnEndPhaseComplete -= OnExitComplete;
                animationController.OnEndPhaseComplete += OnExitComplete;
                animationController.RequestEndPhase();
            }
            else
            {
                // LoopRegionがない場合は直接終了
                OnExitComplete();
            }
        }

        /// <summary>
        /// Talkモードをキャンセル（移動中のみ）
        /// </summary>
        public void CancelTalk()
        {
            if (_currentState == TalkState.Approaching)
            {
                Debug.Log("[TalkController] CancelTalk - stopping movement");
                navigationController?.StopMoving();
                _currentState = TalkState.None;
            }
            else if (_currentState == TalkState.InTalk)
            {
                // InTalk中はExitTalkを呼ぶ
                ExitTalk();
            }
        }

        /// <summary>
        /// talk_positionへの移動を開始
        /// </summary>
        private void StartApproaching()
        {
            _currentState = TalkState.Approaching;

            var talkPos = GetTalkPosition();
            if (navigationController != null && talkPos != null)
            {
                navigationController.MoveTo(
                    talkPos.position,
                    talkPos.rotation,
                    OnApproachCompleteCallback
                );
                Debug.Log($"[TalkController] Moving to talk position: {talkPos.position}");
            }
            else
            {
                // NavigationControllerがない場合は直接Talk開始
                StartTalkMode();
            }
        }

        /// <summary>
        /// 移動完了コールバック
        /// </summary>
        private void OnApproachCompleteCallback()
        {
            Debug.Log("[TalkController] Approach complete");
            OnApproachComplete?.Invoke();
            StartTalkMode();
        }

        /// <summary>
        /// Talkモードを開始（アニメーション・視線）
        /// </summary>
        private void StartTalkMode()
        {
            _currentState = TalkState.InTalk;

            // Talk待機アニメーションを再生
            if (animationController != null)
            {
                animationController.PlayState(AnimationStateType.Talk, _talkIdleAnimationId);
            }

            // talk_lookattargetがあればそちらを注視、なければカメラを注視
            if (lookAtController != null)
            {
                var lookAtTarget = GetTalkLookAtTarget();
                if (lookAtTarget != null)
                {
                    lookAtController.LookAtPosition(lookAtTarget.position);
                }
                else
                {
                    lookAtController.LookAtPlayer();
                }
            }

            Debug.Log("[TalkController] Talk mode started");
            OnTalkStarted?.Invoke();
        }

        /// <summary>
        /// 終了完了コールバック
        /// </summary>
        private void OnExitComplete()
        {
            if (animationController != null)
            {
                animationController.OnEndPhaseComplete -= OnExitComplete;
            }

            _currentState = TalkState.None;

            // Idleへ戻る
            if (returnToIdleOnExit && animationController != null)
            {
                animationController.ReturnToIdle();
            }

            Debug.Log("[TalkController] Talk mode ended");
            OnTalkEnded?.Invoke();
        }

        /// <summary>
        /// Thinking状態に切り替え（LLM応答待ち）
        /// ThinkingPlayableTrackが現在のTimelineにアクティブであれば再生可能
        /// Talk mode以外でも動作する（座り中のThinking等）
        /// </summary>
        public void StartThinking()
        {
            if (animationController != null && animationController.CanPlayThinking())
            {
                animationController.PlayThinkingWithReturn(_thinkingAnimationId);
                Debug.Log("[TalkController] Thinking started");
            }
        }

        /// <summary>
        /// Thinking状態から復帰先に戻る（graceful: ed再生あり）
        /// </summary>
        public void StopThinking()
        {
            if (animationController != null && animationController.IsThinkingActive)
            {
                animationController.StopThinkingAndReturn();
                Debug.Log("[TalkController] Thinking stopped");
            }
        }

        /// <summary>
        /// Thinking状態を即座に終了（ed再生なし）
        /// emoteが最初に届いた場合等、直接次のアニメーションに遷移する時に使用
        /// </summary>
        public void ForceStopThinking()
        {
            if (animationController != null && animationController.IsThinkingActive)
            {
                animationController.ForceStopThinking();
                Debug.Log("[TalkController] Thinking force-stopped");
            }
        }

        /// <summary>
        /// 現在Talkモード中かどうか
        /// </summary>
        public bool IsInTalkMode => _currentState == TalkState.InTalk;

        /// <summary>
        /// Talkモードへ移行中かどうか
        /// </summary>
        public bool IsTransitioning => _currentState == TalkState.Approaching || _currentState == TalkState.Exiting;

        /// <summary>
        /// Talkモードを即座に終了（終了アニメーションなし）
        /// 別のアクション（move+dynamic等）に移行する場合に使用
        /// </summary>
        public void ForceExitTalk()
        {
            if (_currentState == TalkState.None) return;

            Debug.Log("[TalkController] ForceExitTalk");

            if (animationController != null)
            {
                animationController.OnEndPhaseComplete -= OnExitComplete;
            }

            _currentState = TalkState.None;
            OnTalkEnded?.Invoke();
        }

        /// <summary>
        /// talk_positionから離れているかどうか
        /// </summary>
        public bool IsAwayFromTalkPosition(float threshold = 1.0f)
        {
            var talkPos = GetTalkPosition();
            if (talkPos == null || navigationController == null) return false;

            float distance = Vector3.Distance(
                navigationController.transform.position,
                talkPos.position
            );
            return distance > threshold;
        }
    }

    /// <summary>
    /// Talkモードの状態
    /// </summary>
    public enum TalkState
    {
        None,        // 通常状態
        Approaching, // talk_positionへ移動中
        InTalk,      // Talkモード中
        Exiting      // Talkモード終了中
    }
}
