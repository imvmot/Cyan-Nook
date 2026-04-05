using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using System.Linq;
using CyanNook.Character;
using CyanNook.Core;
using CyanNook.Furniture;

namespace CyanNook.DebugTools
{
    /// <summary>
    /// デバッグ用キー入力の一括管理
    /// W: 前進歩行, A/D: 歩行中旋回, C: Talk開始, V: Talk終了
    /// F: 家具インタラクション, G: インタラクション終了, H: インタラクションキャンセル
    /// </summary>
    public class DebugKeyController : MonoBehaviour
    {
        [Header("References")]
        public CharacterAnimationController animationController;
        public CharacterNavigationController navigationController;
        public TalkController talkController;
        public InteractionController interactionController;
        public FurnitureManager furnitureManager;

        [Tooltip("入力フィールド（フォーカス中はキー無効）")]
        public TMP_InputField inputField;

        [Header("Walk Settings")]
        [Tooltip("A/Dキーの旋回速度（度/秒）")]
        public float turnSpeed = 180f;

        [Header("Interaction Settings")]
        [Tooltip("家具を探す範囲")]
        public float searchRadius = 10f;

        // 状態
        private bool _isWalking;
        private bool _isTalking;

        // 手動エッジ検出用（wasPressedThisFrameがTimeline再生中に動作しない場合の対策）
        private bool _wasGKeyPressed;
        private bool _wasHKeyPressed;

        // デバッグ表示用
        [Header("Debug")]
        [SerializeField]
        private FurnitureInstance _nearestFurniture;

        /// <summary>デバッグキーが有効かどうか</summary>
        public bool IsEnabled { get; private set; } = false;

        /// <summary>
        /// デバッグキーの有効/無効を設定
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;

            // 無効化時に進行中の状態をクリーン
            if (!enabled)
            {
                if (_isWalking)
                {
                    StopWalk();
                }
                if (_isTalking)
                {
                    // Talkはコントローラ側に任せる（強制終了しない）
                }
            }

            Debug.Log($"[DebugKeyController] Enabled: {enabled}");
        }

        // =====================================================================
        // Update
        // =====================================================================

        private void Update()
        {
            if (!IsEnabled) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 入力フィールドにフォーカスがある場合はキー入力を無視
            if (inputField != null && inputField.isFocused) return;

            UpdateWalkInput(keyboard);
            UpdateTalkInput(keyboard);
            UpdateInteractionInput(keyboard);
        }

        // =====================================================================
        // Walk: W (前進), A/D (旋回)
        // =====================================================================

        private void UpdateWalkInput(Keyboard keyboard)
        {
            if (animationController == null) return;

            // W押下: 歩行開始
            if (keyboard.wKey.wasPressedThisFrame && !_isWalking)
            {
                _isWalking = true;
                animationController.OnEndPhaseComplete += OnWalkEndPhaseComplete;

                if (navigationController != null)
                {
                    navigationController.StartForwardWalk();
                    Debug.Log("[DebugKeyController] W pressed → StartForwardWalk (NavMesh)");
                }
                else
                {
                    animationController.StartWalk();
                    Debug.Log("[DebugKeyController] W pressed → StartWalk (fallback)");
                }
            }

            // A/D: 歩行中の旋回（CharacterRoot=NavigationController自身を回転）
            // UpdateMoving()がtransform.forwardで移動方向を決定するため、CharacterRootを回転させる
            if (_isWalking && navigationController != null)
            {
                float turnInput = 0f;
                if (keyboard.aKey.isPressed) turnInput -= 1f;
                if (keyboard.dKey.isPressed) turnInput += 1f;

                if (turnInput != 0f)
                {
                    navigationController.transform.Rotate(0f, turnInput * turnSpeed * Time.deltaTime, 0f);
                }
            }

            // W離し: 歩行停止
            if (keyboard.wKey.wasReleasedThisFrame && _isWalking)
            {
                // NavMeshAgentを停止
                if (navigationController != null)
                {
                    navigationController.StopForwardWalk();
                }

                // walk_edがあればRequestEndPhase、なければ直接Idle
                if (animationController.HasLoopRegion)
                {
                    animationController.RequestEndPhase();
                    Debug.Log("[DebugKeyController] W released → RequestEndPhase");
                }
                else
                {
                    StopWalk();
                    animationController.ReturnToIdle();
                    Debug.Log("[DebugKeyController] W released → ReturnToIdle (no LoopRegion)");
                }
            }
        }

        private void OnWalkEndPhaseComplete()
        {
            StopWalk();
            animationController.ReturnToIdle();
            Debug.Log("[DebugKeyController] Walk ed complete → ReturnToIdle");
        }

        private void StopWalk()
        {
            _isWalking = false;
            if (animationController != null)
            {
                animationController.OnEndPhaseComplete -= OnWalkEndPhaseComplete;
            }
        }

        // =====================================================================
        // Talk: C (開始), V (終了)
        // =====================================================================

        private void UpdateTalkInput(Keyboard keyboard)
        {
            if (talkController == null) return;

            // Cキー: Talkモード開始
            if (keyboard.cKey.wasPressedThisFrame && !_isTalking)
            {
                if (talkController.CurrentState == TalkState.None)
                {
                    _isTalking = true;
                    talkController.OnTalkEnded += OnTalkEnded;
                    talkController.EnterTalk();
                    Debug.Log("[DebugKeyController] C pressed → EnterTalk");
                }
                else
                {
                    Debug.Log($"[DebugKeyController] C pressed but state is {talkController.CurrentState}, not None");
                }
            }

            // Vキー: Talkモード終了
            if (keyboard.vKey.wasPressedThisFrame && _isTalking)
            {
                if (talkController.CurrentState == TalkState.InTalk)
                {
                    talkController.ExitTalk();
                    Debug.Log("[DebugKeyController] V pressed → ExitTalk");
                }
                else if (talkController.CurrentState == TalkState.Approaching)
                {
                    talkController.CancelTalk();
                    _isTalking = false;
                    talkController.OnTalkEnded -= OnTalkEnded;
                    Debug.Log("[DebugKeyController] V pressed → CancelTalk (approaching)");
                }
            }
        }

        private void OnTalkEnded()
        {
            _isTalking = false;
            talkController.OnTalkEnded -= OnTalkEnded;
            Debug.Log("[DebugKeyController] Talk ended");
        }

        // =====================================================================
        // Interaction: F (開始), G (終了), H (キャンセル)
        // =====================================================================

        private void UpdateInteractionInput(Keyboard keyboard)
        {
            // Fキーでインタラクション開始
            if (keyboard.fKey.wasPressedThisFrame)
            {
                TryStartInteraction();
            }

            // Gキーでインタラクション終了（ループから抜ける）
            // wasPressedThisFrameがTimeline再生中に動作しない場合があるため、手動エッジ検出も併用
            bool gKeyCurrentlyPressed = keyboard.gKey.isPressed;
            bool gKeyJustPressed = keyboard.gKey.wasPressedThisFrame
                                   || (gKeyCurrentlyPressed && !_wasGKeyPressed);

            if (gKeyJustPressed)
            {
                TryExitInteraction();
            }
            _wasGKeyPressed = gKeyCurrentlyPressed;

            // Hキーでインタラクションキャンセル（同様に手動エッジ検出を併用）
            bool hKeyCurrentlyPressed = keyboard.hKey.isPressed;
            bool hKeyJustPressed = keyboard.hKey.wasPressedThisFrame
                                   || (hKeyCurrentlyPressed && !_wasHKeyPressed);

            if (hKeyJustPressed)
            {
                TryCancelInteraction();
            }
            _wasHKeyPressed = hKeyCurrentlyPressed;

            // デバッグ：最寄りの家具を更新
            UpdateNearestFurniture();
        }

        private void TryStartInteraction()
        {
            if (interactionController == null || furnitureManager == null)
            {
                Debug.LogWarning("[DebugKeyController] InteractionController or FurnitureManager not set");
                return;
            }

            if (interactionController.IsInteracting())
            {
                Debug.Log("[DebugKeyController] Already interacting");
                return;
            }

            var characterTransform = navigationController?.characterTransform;
            if (characterTransform == null)
            {
                Debug.LogWarning("[DebugKeyController] characterTransform not available");
                return;
            }

            // 最寄りの家具を探す
            var furniture = FindNearestFurniture(characterTransform);
            if (furniture == null)
            {
                Debug.Log("[DebugKeyController] No furniture found nearby");
                return;
            }

            Debug.Log($"[DebugKeyController] Starting interaction with: {furniture.name}");

            var request = furnitureManager.StartInteraction(furniture.instanceId, characterTransform);
            if (request != null)
            {
                interactionController.StartInteraction(request);
            }
            else
            {
                Debug.LogWarning("[DebugKeyController] Failed to create interaction request");
            }
        }

        private void TryExitInteraction()
        {
            if (interactionController == null) return;

            if (interactionController.IsInteracting())
            {
                Debug.Log($"[DebugKeyController] Exiting interaction (state: {interactionController.CurrentState})");
                interactionController.ExitLoop();
            }
        }

        private void TryCancelInteraction()
        {
            if (interactionController == null) return;

            if (interactionController.IsInteracting())
            {
                if (interactionController.TryCancel())
                {
                    Debug.Log("[DebugKeyController] Cancelled interaction");
                }
                else
                {
                    Debug.Log("[DebugKeyController] Cannot cancel at this time");
                }
            }
        }

        // =====================================================================
        // Furniture Search
        // =====================================================================

        private FurnitureInstance FindNearestFurniture(Transform characterTransform)
        {
            if (characterTransform == null) return null;

            FurnitureInstance nearest = null;
            float nearestDistance = searchRadius;

            var allFurniture = FindObjectsByType<FurnitureInstance>(FindObjectsSortMode.None);
            foreach (var furniture in allFurniture)
            {
                if (furniture.isOccupied) continue;

                // exit/entry専用の家具はデバッグインタラクトから除外
                if (furniture.typeData?.availableActions != null &&
                    furniture.typeData.availableActions.All(a => a == "exit" || a == "enter" || a == "entry"))
                    continue;

                float distance = Vector3.Distance(characterTransform.position, furniture.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = furniture;
                }
            }

            return nearest;
        }

        private void UpdateNearestFurniture()
        {
            var characterTransform = navigationController?.characterTransform;
            _nearestFurniture = FindNearestFurniture(characterTransform);
        }

        private void OnDrawGizmosSelected()
        {
            var characterTransform = navigationController?.characterTransform;
            if (characterTransform == null) return;

            // 検索範囲を表示
            Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
            Gizmos.DrawWireSphere(characterTransform.position, searchRadius);

            // 最寄りの家具への線を表示
            if (_nearestFurniture != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(characterTransform.position, _nearestFurniture.transform.position);
            }
        }
    }
}
