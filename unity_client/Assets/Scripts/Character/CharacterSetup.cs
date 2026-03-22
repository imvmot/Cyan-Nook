using System.Collections;
using UnityEngine;
using CyanNook.Character;
using CyanNook.Core;
using CyanNook.Furniture;
using CyanNook.Chat;
using CyanNook.DebugTools;
using CyanNook.UI;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターセットアップ
    /// シーン再生時にVRMを読み込み、全コンポーネントを初期化・接続する
    /// </summary>
    public class CharacterSetup : MonoBehaviour
    {
        [Header("VRM Settings")]
        [Tooltip("読み込むVRMファイル名")]
        public string vrmFileName = "chr001_w001_model.vrm";

        [Header("References")]
        public VrmLoader vrmLoader;
        public CharacterAnimationController animationController;
        public CharacterExpressionController expressionController;
        public CharacterLookAtController lookAtController;

        [Header("Navigation/Interaction References")]
        public CharacterNavigationController navigationController;
        public InteractionController interactionController;
        public FurnitureManager furnitureManager;
        public RoomTargetManager roomTargetManager;
        public TalkController talkController;
        public ChatManager chatManager;

        [Header("Camera")]
        public CharacterCameraController cameraController;

        [Header("Face Light")]
        public CharacterFaceLightController faceLightController;

        [Header("LipSync")]
        public LipSyncController lipSyncController;

        [Header("Rendering")]
        [Tooltip("VRMモデルの全Rendererに設定するRendering Layer Mask（URP Rendering Layer）")]
        public uint vrmRenderingLayerMask = 1;

        [Tooltip("VRMモデルの全GameObjectに設定するLayer（Light cullingMask用）。-1で変更しない")]
        public int vrmCullingLayer = -1;

        [Header("Boredom")]
        public BoredomController boredomController;

        [Header("Sleep")]
        public SleepController sleepController;

        [Header("Outing")]
        public OutingController outingController;

        [Header("Room Light")]
        public RoomLightController roomLightController;

        [Header("UI")]
        public UIController uiController;
        public FirstRunController firstRunController;
        public LLMSettingsPanel llmSettingsPanel;

        [Header("Debug Keys")]
        public DebugKeyController debugKeyController;

        [Header("Test Settings")]
        [Tooltip("読み込み完了後にカメラを向くか")]
        public bool lookAtCameraOnLoad = true;

        [Tooltip("テスト用表情を適用するか")]
        public bool applyTestExpression = false;

        private Camera _mainCamera;
        private bool _entryPending;

        private void Start()
        {
            _mainCamera = Camera.main;

            if (vrmLoader == null)
            {
                vrmLoader = GetComponent<VrmLoader>();
            }

            if (vrmLoader != null)
            {
                vrmLoader.OnVrmLoaded += OnVrmLoaded;
                vrmLoader.OnVrmLoadFailed += OnVrmLoadFailed;

                // VRM読み込み開始
                Debug.Log($"[CharacterSetup] Starting VRM load: {vrmFileName}");
                vrmLoader.LoadVrmAsync(vrmFileName);
            }
            else
            {
                Debug.LogError("[CharacterSetup] VrmLoader not found!");
            }
        }

        private void OnDestroy()
        {
            if (vrmLoader != null)
            {
                vrmLoader.OnVrmLoaded -= OnVrmLoaded;
                vrmLoader.OnVrmLoadFailed -= OnVrmLoadFailed;
            }
            if (llmSettingsPanel != null)
            {
                llmSettingsPanel.OnLLMConfigured -= OnLLMConfiguredFromSettings;
            }
        }

        private void OnVrmLoaded(UniVRM10.Vrm10Instance vrmInstance)
        {
            Debug.Log($"[CharacterSetup] VRM loaded successfully: {vrmInstance.name}");

            // コンポーネントのセットアップ（Navigation/Interaction/Talk/Camera含む）
            vrmLoader.SetupCharacterComponents(
                animationController,
                expressionController,
                lookAtController,
                navigationController,
                interactionController,
                furnitureManager,
                talkController,
                cameraController,
                lipSyncController,
                faceLightController
            );

            // TalkControllerにRoomTargetManagerを設定
            if (talkController != null && roomTargetManager != null)
            {
                talkController.roomTargetManager = roomTargetManager;
                Debug.Log("[CharacterSetup] TalkController.roomTargetManager set");
            }

            // VRMインスタンスの全RendererにRendering Layer Maskを設定
            if (vrmRenderingLayerMask != 1)
            {
                var renderers = vrmInstance.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    r.renderingLayerMask = vrmRenderingLayerMask;
                }
                Debug.Log($"[CharacterSetup] Rendering Layer Mask set to {vrmRenderingLayerMask} on {renderers.Length} renderers");
            }

            // VRMインスタンスの全GameObjectにLayer設定（Light cullingMask用）
            if (vrmCullingLayer >= 0)
            {
                SetLayerRecursive(vrmInstance.gameObject, vrmCullingLayer);
                Debug.Log($"[CharacterSetup] Culling Layer set to {vrmCullingLayer} on VRM hierarchy");
            }

            // AnimationControllerにRoomLightController参照設定
            if (animationController != null && roomLightController != null)
            {
                animationController.roomLightController = roomLightController;
            }

            // SleepController参照設定
            if (sleepController != null)
            {
                sleepController.interactionController = interactionController;
                sleepController.furnitureManager = furnitureManager;
                sleepController.chatManager = chatManager;
                sleepController.boredomController = boredomController;
                sleepController.vrmLoader = vrmLoader;
                Debug.Log("[CharacterSetup] SleepController references set");
            }

            // OutingController参照設定
            if (outingController != null)
            {
                outingController.animationController = animationController;
                outingController.navigationController = navigationController;
                outingController.furnitureManager = furnitureManager;
                outingController.chatManager = chatManager;
                outingController.vrmLoader = vrmLoader;
                outingController.boredomController = boredomController;
                outingController.uiController = uiController;
                Debug.Log("[CharacterSetup] OutingController references set");
            }

            // Sleep状態の復元チェック → 復元成功時はIdle/Entry開始をスキップ
            bool sleepRestored = false;
            if (sleepController != null && sleepController.ShouldStartAsSleep())
            {
                sleepRestored = sleepController.RestoreSleep(animationController, navigationController);
                if (sleepRestored)
                {
                    Debug.Log("[CharacterSetup] Sleep state restored, skipping Entry/Idle start");
                }
            }

            if (!sleepRestored)
            {
                // LLM未設定チェック: 初回起動時はEntryを保留し、ポップアップ表示
                if (firstRunController != null && !LLMConfigManager.HasSavedConfig())
                {
                    _entryPending = true;

                    // 設定パネルからの保存完了を購読
                    if (llmSettingsPanel != null)
                    {
                        llmSettingsPanel.OnLLMConfigured += OnLLMConfiguredFromSettings;
                    }

                    firstRunController.Show(onComplete: () =>
                    {
                        _entryPending = false;
                        PlayEntryOrIdle();
                    });
                    Debug.Log("[CharacterSetup] LLM not configured, showing first run popup");
                }
                else
                {
                    PlayEntryOrIdle();
                }
            }
            else
            {
                // Sleep復元時: 従来通りの表示処理
                StartCoroutine(ShowModelAfterAnimation());
            }

            // カメラを見る設定
            if (lookAtCameraOnLoad && lookAtController != null && _mainCamera != null)
            {
                lookAtController.SetPlayerTarget(_mainCamera.transform);
                lookAtController.LookAtPlayer();
                Debug.Log("[CharacterSetup] LookAt camera enabled");
            }

            // テスト表情
            if (applyTestExpression && expressionController != null)
            {
                expressionController.SetEmotion(new EmotionData
                {
                    happy = 0.5f,
                    relaxed = 0.3f
                });
                Debug.Log("[CharacterSetup] Test expression applied");
            }

            // DebugKeyController参照設定
            if (debugKeyController != null)
            {
                debugKeyController.animationController = animationController;
                debugKeyController.navigationController = navigationController;
                debugKeyController.talkController = talkController;
                debugKeyController.interactionController = interactionController;
                debugKeyController.furnitureManager = furnitureManager;
                Debug.Log("[CharacterSetup] DebugKeyController references set");
            }

            // ChatManager参照設定
            if (chatManager != null)
            {
                chatManager.talkController = talkController;
                chatManager.expressionController = expressionController;
                chatManager.boredomController = boredomController;
                chatManager.sleepController = sleepController;
                chatManager.outingController = outingController;
                if (cameraController != null)
                {
                    chatManager.cameraController = cameraController;
                }
                Debug.Log("[CharacterSetup] ChatManager references set");

                // VOICEVOX用LipSyncControllerにVRMインスタンスを接続
                var voiceLipSync = chatManager.voiceSynthesisController?.lipSyncController;
                if (voiceLipSync != null)
                {
                    voiceLipSync.vrmInstance = vrmInstance;
                    Debug.Log("[CharacterSetup] VoiceSynthesis LipSyncController VRM instance set");
                }
            }
        }

        /// <summary>
        /// Entry再生またはIdle開始（共通処理）
        /// </summary>
        private void PlayEntryOrIdle()
        {
            // ライト制御はLightControlTrack（Timeline）で行う

            if (outingController != null)
            {
                outingController.PlayEntry(() =>
                {
                    if (animationController != null)
                    {
                        animationController.PlayState(AnimationStateType.Idle);
                        Debug.Log("[CharacterSetup] Entry animation complete, starting Idle");
                    }
                }, skipBlend: true);
                Debug.Log("[CharacterSetup] Playing entry animation on startup");
            }
            else
            {
                if (animationController != null)
                {
                    animationController.PlayState(AnimationStateType.Idle, skipBlend: true);
                    Debug.Log("[CharacterSetup] Idle animation started (no OutingController)");
                }
                StartCoroutine(ShowModelAfterAnimation());
            }
        }

        /// <summary>
        /// LLMSettingsPanelからの保存完了コールバック（Noルート用）
        /// </summary>
        private void OnLLMConfiguredFromSettings()
        {
            if (!_entryPending) return;
            _entryPending = false;

            Debug.Log("[CharacterSetup] LLM configured from settings panel");

            if (llmSettingsPanel != null)
            {
                llmSettingsPanel.OnLLMConfigured -= OnLLMConfiguredFromSettings;
            }

            // FirstRunControllerがまだ表示中ならComplete()で閉じる
            if (firstRunController != null && firstRunController.IsShowing)
            {
                firstRunController.Complete();
            }
            else
            {
                // ShowDownloadOnlyのComplete()で既に閉じられている場合
                // ライトONを確認してから直接Entry開始
                PlayEntryOrIdle();
            }
        }

        /// <summary>
        /// アニメーション評価が完了してからモデルを表示する。
        /// director.Play() 後、PlayableDirectorの評価 → Animatorのボーン反映まで
        /// 数フレームかかるため、十分なフレーム数を待機してからRendererを有効化する。
        /// 将来的にはここでエフェクト演出を追加可能。
        /// </summary>
        private IEnumerator ShowModelAfterAnimation()
        {
            // Frame 1: PlayableDirectorがPlayableGraphを評価
            yield return null;
            // Frame 2: AnimatorがPlayable出力をボーンに反映
            yield return null;
            // Frame 3: 確実にポーズが適用された状態でRendererを有効化
            yield return null;

            if (vrmLoader != null)
            {
                vrmLoader.SetMeshVisibility(true);
                Debug.Log("[CharacterSetup] Model revealed after animation evaluation");
            }
        }

        private void OnVrmLoadFailed(string error)
        {
            Debug.LogError($"[CharacterSetup] VRM load failed: {error}");
        }

        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>
        /// VRMモデルを再読み込みする
        /// AvatarSettingsPanelのReloadボタンから呼ばれる
        /// </summary>
        public void ReloadVrm(string newFileName)
        {
            if (vrmLoader == null)
            {
                Debug.LogError("[CharacterSetup] VrmLoader not found");
                return;
            }

            if (vrmLoader.IsLoading)
            {
                Debug.LogWarning("[CharacterSetup] VRM is currently loading");
                return;
            }

            vrmFileName = newFileName;
            vrmLoader.DisposeCurrentVrm();
            vrmLoader.LoadVrmAsync(vrmFileName);
            // OnVrmLoadedイベントが発火し、自動的に再セットアップされる
        }

        /// <summary>
        /// インスペクタからテスト表情を適用
        /// </summary>
        [ContextMenu("Apply Happy Expression")]
        public void ApplyHappyExpression()
        {
            if (expressionController != null)
            {
                expressionController.SetEmotion(new EmotionData { happy = 1f });
            }
        }

        [ContextMenu("Apply Sad Expression")]
        public void ApplySadExpression()
        {
            if (expressionController != null)
            {
                expressionController.SetEmotion(new EmotionData { sad = 1f });
            }
        }

        [ContextMenu("Reset Expression")]
        public void ResetExpression()
        {
            if (expressionController != null)
            {
                expressionController.ResetEmotion();
            }
        }
    }
}
