using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using UnityEngine.Playables;
using UniVRM10;
using UniGLTF;
using CyanNook.Core;
using CyanNook.Furniture;
using CyanNook.Timeline;

namespace CyanNook.Character
{
    /// <summary>
    /// VRMモデルのランタイム読み込みを担当
    /// StreamingAssetsからVRMを読み込み、必要なコンポーネントをセットアップする
    /// </summary>
    public class VrmLoader : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("VRMファイルが配置されているフォルダ名（StreamingAssets以下）")]
        public string vrmFolderName = "VRM";

        [Tooltip("読み込み時にメッシュを表示するか（falseにするとアニメーション適用後に手動で表示する）")]
        public bool showMeshesOnLoad = false;

        [Tooltip("VRMのCast Shadow（影の投影）を無効化する")]
        public bool disableCastShadow = true;

        [Header("References")]
        [Tooltip("キャラクターテンプレートデータ")]
        public CharacterTemplateData templateData;

        [Tooltip("Timelineバインディングデータ")]
        public TimelineBindingData timelineBindings;

        [Tooltip("回転補間用のピボット（VRMの親となる）")]
        public Transform blendPivot;

        // Events
        public event Action<Vrm10Instance> OnVrmLoaded;
        public event Action<string> OnVrmLoadFailed;

        [Header("State")]
        [SerializeField]
        private Vrm10Instance _currentVrmInstance;
        public Vrm10Instance CurrentVrmInstance => _currentVrmInstance;

        [SerializeField]
        private bool _isLoading = false;
        public bool IsLoading => _isLoading;

        private CancellationTokenSource _cancellationTokenSource;

        private void OnDestroy()
        {
            CancelLoading();
            DisposeCurrentVrm();
        }

        /// <summary>
        /// VRMファイルを読み込む
        /// </summary>
        /// <param name="fileName">ファイル名（例: chr001_w001_model.vrm）</param>
        public async void LoadVrmAsync(string fileName)
        {
            try
            {
                await LoadVrmInternalAsync(fileName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VrmLoader] LoadVrmAsync unhandled exception: {ex.Message}");
            }
        }

        /// <summary>
        /// CharacterTemplateDataに設定されたデフォルトVRMを読み込む
        /// </summary>
        public async void LoadDefaultVrmAsync()
        {
            try
            {
                if (templateData == null)
                {
                    Debug.LogError("[VrmLoader] CharacterTemplateData is not assigned");
                    OnVrmLoadFailed?.Invoke("CharacterTemplateData is not assigned");
                    return;
                }

                if (string.IsNullOrEmpty(templateData.defaultVrmFileName))
                {
                    Debug.LogError("[VrmLoader] defaultVrmFileName is not set in CharacterTemplateData");
                    OnVrmLoadFailed?.Invoke("defaultVrmFileName is not set");
                    return;
                }

                await LoadVrmInternalAsync(templateData.defaultVrmFileName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VrmLoader] LoadDefaultVrmAsync unhandled exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 読み込み中のVRMをキャンセル
        /// </summary>
        public void CancelLoading()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _isLoading = false;
        }

        /// <summary>
        /// 現在のVRMを破棄
        /// </summary>
        public void DisposeCurrentVrm()
        {
            if (_currentVrmInstance != null)
            {
                Destroy(_currentVrmInstance.gameObject);
                _currentVrmInstance = null;
            }
        }

        private async Task LoadVrmInternalAsync(string fileName)
        {
            if (_isLoading)
            {
                Debug.LogWarning("[VrmLoader] Already loading VRM");
                return;
            }

            // 前回の読み込みをキャンセル（_isLoadingフラグは維持）
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            _isLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                string path = GetVrmPath(fileName);
                Debug.Log($"[VrmLoader] Loading VRM from: {path}");

                // StreamingAssetsからバイトデータを読み込む
                byte[] vrmBytes = await LoadBytesFromStreamingAssetsAsync(path, _cancellationTokenSource.Token);

                if (vrmBytes == null || vrmBytes.Length == 0)
                {
                    throw new Exception($"Failed to load VRM file: {fileName}");
                }

                // 以前のVRMを破棄
                DisposeCurrentVrm();

                // WebGLはシングルスレッドのため、スレッドを使わないAwaitCallerを使用
#if UNITY_WEBGL && !UNITY_EDITOR
                IAwaitCaller awaitCaller = new RuntimeOnlyNoThreadAwaitCaller();
#else
                IAwaitCaller awaitCaller = new RuntimeOnlyAwaitCaller();
#endif

                // VRM1.0として読み込み
                var vrm10Instance = await Vrm10.LoadBytesAsync(
                    vrmBytes,
                    canLoadVrm0X: true,
                    showMeshes: showMeshesOnLoad,
                    awaitCaller: awaitCaller,
                    ct: _cancellationTokenSource.Token
                );

                if (vrm10Instance == null)
                {
                    throw new Exception("Failed to create Vrm10Instance");
                }

                _currentVrmInstance = vrm10Instance;

                // 親を設定（BlendPivotがあればその子、なければ直接の子）
                Transform parentTransform = blendPivot != null ? blendPivot : transform;
                vrm10Instance.transform.SetParent(parentTransform, false);
                vrm10Instance.transform.localPosition = Vector3.zero;
                vrm10Instance.transform.localRotation = Quaternion.identity;

                // BlendPivotも初期化
                if (blendPivot != null)
                {
                    blendPivot.localPosition = Vector3.zero;
                    blendPivot.localRotation = Quaternion.identity;
                    Debug.Log("[VrmLoader] VRM parented to BlendPivot");
                }

                // Animatorの設定
                SetupAnimator(vrm10Instance);

                // PlayableDirectorの設定
                SetupPlayableDirector(vrm10Instance);

                // セルフシャドウ制御
                if (disableCastShadow)
                    DisableCastShadow(vrm10Instance);

                // バウンディングボックス毎フレーム更新（ライト判定の正確性確保）
                SetUpdateWhenOffscreen(true);

                Debug.Log($"[VrmLoader] VRM loaded successfully: {fileName}");
                OnVrmLoaded?.Invoke(vrm10Instance);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[VrmLoader] VRM loading cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VrmLoader] Failed to load VRM: {ex}");
                OnVrmLoadFailed?.Invoke(ex.Message);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private string GetVrmPath(string fileName)
        {
            return Path.Combine(Application.streamingAssetsPath, vrmFolderName, fileName);
        }

        private async Task<byte[]> LoadBytesFromStreamingAssetsAsync(string path, CancellationToken ct)
        {
            // WebGLではUnityWebRequestを使用する必要がある
#if UNITY_WEBGL && !UNITY_EDITOR
            return await LoadBytesWithWebRequestAsync(path, ct);
#else
            // エディタやスタンドアロンではFile.ReadAllBytesでもOK
            // ただしWebGL対応のためUnityWebRequestを使用
            return await LoadBytesWithWebRequestAsync(path, ct);
#endif
        }

        private async Task<byte[]> LoadBytesWithWebRequestAsync(string path, CancellationToken ct)
        {
            using (var request = UnityWebRequest.Get(path))
            {
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to load file: {request.error}");
                }

                return request.downloadHandler.data;
            }
        }

        private void SetupAnimator(Vrm10Instance vrm10Instance)
        {
            var animator = vrm10Instance.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning("[VrmLoader] Animator not found on VRM instance");
                return;
            }

            // Timeline主導のため、AnimatorControllerは設定しない
            // （Timelineから直接AnimationClipを再生する）
            Debug.Log("[VrmLoader] Animator ready for Timeline control");
        }

        private PlayableDirector SetupPlayableDirector(Vrm10Instance vrm10Instance)
        {
            // PlayableDirectorを追加（なければ）
            var director = vrm10Instance.GetComponent<PlayableDirector>();
            if (director == null)
            {
                director = vrm10Instance.gameObject.AddComponent<PlayableDirector>();
            }

            // 初期設定
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;

            Debug.Log("[VrmLoader] PlayableDirector setup complete");
            return director;
        }

        private void DisableCastShadow(Vrm10Instance vrm10Instance)
        {
            var renderers = vrm10Instance.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Debug.Log($"[VrmLoader] Cast shadow disabled for {renderers.Length} renderers");
        }

        /// <summary>
        /// VRMモデルの全Rendererの表示/非表示を切り替える
        /// </summary>
        public void SetMeshVisibility(bool visible)
        {
            if (_currentVrmInstance == null) return;

            var renderers = _currentVrmInstance.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                r.enabled = visible;
            }
            Debug.Log($"[VrmLoader] Mesh visibility set to {visible} ({renderers.Length} renderers)");
        }

        /// <summary>
        /// SkinnedMeshRendererのupdateWhenOffscreenを一括設定
        /// Sleep復元時など、BlendPivotリセットでワールド座標が変わった際に
        /// boundsが旧位置のまま残りフラスタムカリングが誤動作するのを防止するため
        /// 一時的にtrueに設定し、安定後にfalseに戻す
        /// </summary>
        public void SetUpdateWhenOffscreen(bool enabled)
        {
            if (_currentVrmInstance == null) return;

            var renderers = _currentVrmInstance.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in renderers)
            {
                smr.updateWhenOffscreen = enabled;
            }
            Debug.Log($"[VrmLoader] updateWhenOffscreen set to {enabled} ({renderers.Length} SkinnedMeshRenderers)");
        }

        /// <summary>
        /// VRM読み込み後にCharacterControllerコンポーネントをセットアップ
        /// </summary>
        public void SetupCharacterComponents(
            CharacterAnimationController animationController,
            CharacterExpressionController expressionController,
            CharacterLookAtController lookAtController)
        {
            SetupCharacterComponents(animationController, expressionController, lookAtController, null, null, null, null, null);
        }

        /// <summary>
        /// VRM読み込み後に全てのCharacterコンポーネントをセットアップ（Navigation/Interaction/Talk/Camera含む）
        /// </summary>
        public void SetupCharacterComponents(
            CharacterAnimationController animationController,
            CharacterExpressionController expressionController,
            CharacterLookAtController lookAtController,
            CharacterNavigationController navigationController,
            InteractionController interactionController,
            FurnitureManager furnitureManager,
            TalkController talkController = null,
            CharacterCameraController cameraController = null,
            LipSyncController lipSyncController = null,
            CharacterFaceLightController faceLightController = null)
        {
            if (_currentVrmInstance == null)
            {
                Debug.LogWarning("[VrmLoader] No VRM instance loaded");
                return;
            }

            var animator = _currentVrmInstance.GetComponent<Animator>();
            var director = _currentVrmInstance.GetComponent<PlayableDirector>();

            // AnimationController（Timeline主導）
            if (animationController != null)
            {
                animationController.animator = animator;
                animationController.director = director;
                animationController.templateData = templateData;
                animationController.timelineBindings = timelineBindings;
                animationController.lookAtController = lookAtController;
                animationController.expressionController = expressionController;
                animationController.navigationController = navigationController;
                animationController.SetCharacterTransform(_currentVrmInstance.transform);
            }

            // ExpressionController
            if (expressionController != null)
            {
                expressionController.SetVrmInstance(_currentVrmInstance);
                expressionController.lookAtController = lookAtController;

                // Facial用PlayableDirectorのセットアップ（再生開始は全セットアップ完了後に行う）
                if (expressionController.facialTimelineData != null)
                {
                    // Body用PlayableDirectorとの競合を避けるため、子オブジェクトに配置
                    var facialGo = new GameObject("FacialDirector");
                    facialGo.transform.SetParent(_currentVrmInstance.transform, false);
                    var facialDirector = facialGo.AddComponent<PlayableDirector>();
                    facialDirector.playOnAwake = false;
                    facialDirector.extrapolationMode = DirectorWrapMode.Hold;
                    expressionController.facialDirector = facialDirector;
                    Debug.Log("[VrmLoader] Facial PlayableDirector setup complete");
                }
            }

            // LookAtController
            if (lookAtController != null)
            {
                lookAtController.SetVrmInstance(_currentVrmInstance);
            }

            // NavigationController
            if (navigationController != null)
            {
                navigationController.animationController = animationController;
                navigationController.animator = animator;
                navigationController.characterTransform = _currentVrmInstance.transform;

                // NavMeshAgentのセットアップ（RequireComponentで自動追加される）
                var navAgent = navigationController.GetComponent<NavMeshAgent>();
                if (navAgent != null)
                {
                    navigationController.agent = navAgent;
                    // NavMeshAgentの初期位置をVRMに合わせる
                    navAgent.Warp(_currentVrmInstance.transform.position);
                }

                // RootMotionForwarderをVRMインスタンスに追加
                // OnAnimatorMoveはAnimatorと同じGameObjectでしか呼ばれないため必要
                var rootMotionForwarder = _currentVrmInstance.GetComponent<RootMotionForwarder>();
                if (rootMotionForwarder == null)
                {
                    rootMotionForwarder = _currentVrmInstance.gameObject.AddComponent<RootMotionForwarder>();
                }
                rootMotionForwarder.navigationController = navigationController;
                Debug.Log("[VrmLoader] RootMotionForwarder added to VRM instance");
            }

            // InteractionController
            if (interactionController != null)
            {
                interactionController.animationController = animationController;
                interactionController.navigationController = navigationController;
                // directorはanimationController.directorを使用するため、直接設定しない

                // CharacterAnimationControllerのイベント購読をセットアップ
                interactionController.SetupEventSubscriptions();

                // InertialBlendHelperをVRMインスタンスに追加
                var inertialHelper = _currentVrmInstance.gameObject.GetComponent<InertialBlendHelper>();
                if (inertialHelper == null)
                {
                    inertialHelper = _currentVrmInstance.gameObject.AddComponent<InertialBlendHelper>();
                }
                inertialHelper.animator = animator;
                animationController.inertialBlendHelper = inertialHelper;
                interactionController.inertialBlendHelper = inertialHelper;
                if (lookAtController != null)
                {
                    lookAtController.inertialBlendHelper = inertialHelper;
                }
                Debug.Log("[VrmLoader] InertialBlendHelper added to VRM instance");

                // InertialBlendPrePassをVRMインスタンスに追加（SpringBone前のポーズ補正）
                var prePass = _currentVrmInstance.gameObject.GetComponent<InertialBlendPrePass>();
                if (prePass == null)
                {
                    prePass = _currentVrmInstance.gameObject.AddComponent<InertialBlendPrePass>();
                }
                prePass.inertialBlendHelper = inertialHelper;
                Debug.Log("[VrmLoader] InertialBlendPrePass added to VRM instance");

                // AdditiveOverrideHelperをVRMインスタンスに追加
                var additiveHelper = _currentVrmInstance.gameObject.GetComponent<AdditiveOverrideHelper>();
                if (additiveHelper == null)
                {
                    additiveHelper = _currentVrmInstance.gameObject.AddComponent<AdditiveOverrideHelper>();
                }
                additiveHelper.animator = animator;
                additiveHelper.inertialBlendHelper = inertialHelper;
                animationController.additiveOverrideHelper = additiveHelper;
                // PrePassにAO参照を渡し、SpringBone前にAO復元を実行できるようにする
                prePass.additiveOverrideHelper = additiveHelper;
                Debug.Log("[VrmLoader] AdditiveOverrideHelper added to VRM instance");

            }

            // TalkController
            if (talkController != null)
            {
                talkController.animationController = animationController;
                talkController.navigationController = navigationController;
                talkController.lookAtController = lookAtController;
                Debug.Log("[VrmLoader] TalkController setup complete");
            }

            // CharacterCameraController
            if (cameraController != null)
            {
                cameraController.SetVrmInstance(_currentVrmInstance);
                Debug.Log("[VrmLoader] CharacterCameraController setup complete");
            }

            // LipSyncController
            if (lipSyncController != null)
            {
                lipSyncController.SetVrmInstance(_currentVrmInstance);
                Debug.Log("[VrmLoader] LipSyncController setup complete");
            }

            // CharacterFaceLightController
            if (faceLightController != null)
            {
                faceLightController.SetVrmInstance(_currentVrmInstance);
                Debug.Log("[VrmLoader] CharacterFaceLightController setup complete");
            }

            // Facial Timeline初期再生（全コンポーネントのセットアップ完了後に行う）
            if (expressionController != null && expressionController.facialDirector != null)
            {
                expressionController.StartNeutralFacial();
            }

            Debug.Log("[VrmLoader] Character components setup complete");
        }

        /// <summary>
        /// Navigation/Interaction/Talk コンポーネントをセットアップ
        /// SetupCharacterComponentsへ委譲（後方互換性のため残存）
        /// </summary>
        public void SetupNavigationComponents(
            CharacterAnimationController animationController,
            CharacterNavigationController navigationController,
            InteractionController interactionController,
            TalkController talkController = null,
            CharacterLookAtController lookAtController = null)
        {
            SetupCharacterComponents(
                animationController,
                null, // expressionController
                lookAtController,
                navigationController,
                interactionController,
                null, // furnitureManager
                talkController,
                null  // cameraController
            );
        }
    }
}
