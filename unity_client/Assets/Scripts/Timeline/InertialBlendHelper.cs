using System.Collections.Generic;
using UnityEngine;

namespace CyanNook.Timeline
{
    /// <summary>
    /// 慣性補間（Inertial Blending）の実際の処理を行うヘルパーコンポーネント
    ///
    /// 使用方法:
    /// 1. VRMインスタンスにこのコンポーネントをアタッチ
    /// 2. CharacterAnimationControllerからStartInertialBlend()を呼び出す
    ///
    /// アーキテクチャ（Pre-Update Restoreパターン）:
    /// 1. Update: 前フレームで適用したオフセットをAnimatorが走る前に復元（汚れを拭く）
    /// 2. Animator: クリーンな状態から新しいポーズを計算
    /// 3. LateUpdate: クリーン位置を保存 → オフセットを計算・適用
    /// 4. SkinnedMeshRenderer: オフセット適用済みの位置でメッシュ生成・描画
    ///
    /// ローカル座標（localPosition/localRotation）を使用して累積誤差を防止
    ///
    /// ボーン参照はAwake時に全Humanoidボーンをキャッシュし、
    /// StartInertialBlend時にキャッシュから取得する（GetBoneTransformの再呼び出しを回避）
    /// </summary>
    // UniVRMのSpringBone処理などは11000付近で行われることがあるため、
    // 安全マージンを取って20000を指定します。
    [DefaultExecutionOrder(20000)]
    public class InertialBlendHelper : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("対象のAnimator")]
        public Animator animator;

        [Header("Debug")]
        [SerializeField]
        private bool _isActive;
        [SerializeField]
        private float _elapsedTime;
        [SerializeField]
        private float _blendDuration;

        // Critical Dampingの定数
        // ln(1000) ≈ 6.908 : ブレンド時間終了時に99.9%収束（残余オフセット≈0.8%）
        // ln(100)=4.605では終了時に5.6%残り、大きな初期オフセットで視認可能なポップが発生するため引き上げ
        private const float LN_100 = 6.908f;
        private const float DEFAULT_BLEND_DURATION = 0.3f;

        // 状態管理
        private enum State
        {
            Idle,                // 待機中
            WaitingFirstFrame,   // 1フレーム目待機（ちらつき防止で前ポーズを維持）
            WaitingSecondFrame,  // 2フレーム目待機（Animatorが新ポーズを適用するのを待つ）
            Blending,            // 慣性補間中
        }
        private State _state = State.Idle;

        // ボーンごとの慣性補間データ（ローカル座標）
        private struct BoneBlendData
        {
            public Transform transform;
            public Vector3 previousLocalPosition;
            public Quaternion previousLocalRotation;
            public Vector3 initialPositionOffset;
            public Quaternion initialRotationOffset;
            public Vector3 cleanLocalPosition;
            public Quaternion cleanLocalRotation;
        }

        // Awakeで全Humanoidボーンの参照をキャッシュ
        private Dictionary<HumanBodyBones, Transform> _boneTransformCache;

        // LateUpdateで保存したクリーンポーズのキャッシュ（Transform → ローカル座標）
        // StartInertialBlend時に「前のポーズ」として使用する。
        // ボーンの現在値はLookAtやInertialBlendのオフセットで汚染されている可能性があるため、
        // Animator出力直後に保存したクリーン値を使うことでオフセットの二重適用を防止する。
        private Dictionary<Transform, (Vector3 localPos, Quaternion localRot)> _lastCleanPose
            = new Dictionary<Transform, (Vector3, Quaternion)>();

        private BoneBlendData[] _bones;
        private int _boneCount;
        private float _omega;
        private bool _isOffsetApplied; // オフセットが適用済みかどうか
        private bool _prePassApplied;  // PrePassがWaitingFirstFrameを処理済みか

        // 旧IBのビジュアル状態キャッシュ（CaptureVisualStateIfActiveで保存）
        // 新IB開始時に旧IBオフセット消失によるジャンプを防止するため、
        // RestoreCleanIfActive前の実際の表示状態を保持する。
        private Dictionary<Transform, (Vector3 localPos, Quaternion localRot)> _oldVisualState
            = new Dictionary<Transform, (Vector3, Quaternion)>();

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            // 全HumanoidボーンのTransformをキャッシュ
            _boneTransformCache = new Dictionary<HumanBodyBones, Transform>();
            if (animator != null)
            {
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var bone = (HumanBodyBones)i;
                    var t = animator.GetBoneTransform(bone);
                    if (t != null)
                    {
                        _boneTransformCache[bone] = t;
                    }
                }
            }
        }

        /// <summary>
        /// 慣性補間を開始（ボーンリスト指定）
        /// Timeline再生前に呼び出す
        /// </summary>
        /// <param name="duration">ブレンド時間（秒）</param>
        /// <param name="targetBones">対象ボーンのリスト</param>
        public void StartInertialBlend(float duration, List<HumanBodyBones> targetBones)
        {
            if (targetBones == null || targetBones.Count == 0)
            {
                StartInertialBlend(duration);
                return;
            }

            if (_boneTransformCache == null || _boneTransformCache.Count == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] Bone transform cache is empty, cannot start inertial blend");
                return;
            }

            // 旧IBのビジュアル状態を保存（RestoreCleanIfActiveで消える前に）
            CaptureVisualStateIfActive();
            // 前回のブレンドが動作中の場合、クリーン値に復元してからオフセット汚染を除去
            RestoreCleanIfActive();

            // キャッシュから対象ボーンのTransformを取得してBoneBlendData配列を構築
            _bones = new BoneBlendData[targetBones.Count];
            _boneCount = 0;
            foreach (var bone in targetBones)
            {
                if (_boneTransformCache.TryGetValue(bone, out var t))
                {
                    _bones[_boneCount].transform = t;

                    // 旧IBのビジュアル状態があればそれを使用（旧IBオフセット消失ジャンプ防止）
                    if (_oldVisualState.TryGetValue(t, out var visualPose))
                    {
                        _bones[_boneCount].previousLocalPosition = visualPose.localPos;
                        _bones[_boneCount].previousLocalRotation = visualPose.localRot;
                    }
                    else if (_lastCleanPose.TryGetValue(t, out var cleanPose))
                    {
                        _bones[_boneCount].previousLocalPosition = cleanPose.localPos;
                        _bones[_boneCount].previousLocalRotation = cleanPose.localRot;
                    }
                    else
                    {
                        // キャッシュがない場合（初回起動時など）は現在値をフォールバック
                        _bones[_boneCount].previousLocalPosition = t.localPosition;
                        _bones[_boneCount].previousLocalRotation = t.localRotation;
                    }
                    _boneCount++;
                }
                else
                {
                    Debug.LogWarning($"[InertialBlendHelper] Bone {bone} not found in cache, skipping");
                }
            }

            if (_boneCount == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] No valid bones found, cannot start inertial blend");
                return;
            }

            BeginBlend(duration);
        }

        /// <summary>
        /// 慣性補間を開始（全Humanoidボーン、目・顎を除く）
        /// InertialBlendTrackが無いTimeline遷移時のフォールバックとして使用
        /// </summary>
        /// <param name="duration">ブレンド時間（秒）</param>
        public void StartInertialBlendAllBones(float duration = DEFAULT_BLEND_DURATION)
        {
            if (_boneTransformCache == null || _boneTransformCache.Count == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] Bone transform cache is empty, cannot start inertial blend");
                return;
            }

            // 旧IBのビジュアル状態を保存（RestoreCleanIfActiveで消える前に）
            CaptureVisualStateIfActive();
            // 前回のブレンドが動作中の場合、クリーン値に復元してからオフセット汚染を除去
            RestoreCleanIfActive();

            // キャッシュ内の全ボーンを対象（目・顎を除く、InertialBlendClipと同じ）
            _bones = new BoneBlendData[_boneTransformCache.Count];
            _boneCount = 0;
            foreach (var kvp in _boneTransformCache)
            {
                var bone = kvp.Key;
                var t = kvp.Value;
                if (t == null) continue;
                if (bone == HumanBodyBones.LeftEye || bone == HumanBodyBones.RightEye || bone == HumanBodyBones.Jaw)
                    continue;

                _bones[_boneCount].transform = t;

                // 旧IBのビジュアル状態があればそれを使用（旧IBオフセット消失ジャンプ防止）
                // なければクリーンポーズキャッシュ（通常の新規IB）
                if (_oldVisualState.TryGetValue(t, out var visualPose))
                {
                    _bones[_boneCount].previousLocalPosition = visualPose.localPos;
                    _bones[_boneCount].previousLocalRotation = visualPose.localRot;
                }
                else if (_lastCleanPose.TryGetValue(t, out var cleanPose))
                {
                    _bones[_boneCount].previousLocalPosition = cleanPose.localPos;
                    _bones[_boneCount].previousLocalRotation = cleanPose.localRot;
                }
                else
                {
                    _bones[_boneCount].previousLocalPosition = t.localPosition;
                    _bones[_boneCount].previousLocalRotation = t.localRotation;
                }
                _boneCount++;
            }

            if (_boneCount == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] No valid bones found, cannot start inertial blend");
                return;
            }

            BeginBlend(duration);
        }

        /// <summary>
        /// 慣性補間を開始（Hipsのみ、後方互換用）
        /// Timeline再生前に呼び出す
        /// </summary>
        /// <param name="duration">ブレンド時間（秒）</param>
        public void StartInertialBlend(float duration = DEFAULT_BLEND_DURATION)
        {
            if (_boneTransformCache == null || !_boneTransformCache.ContainsKey(HumanBodyBones.Hips))
            {
                Debug.LogWarning("[InertialBlendHelper] Hips bone not found in cache, cannot start inertial blend");
                return;
            }

            // 旧IBのビジュアル状態を保存（RestoreCleanIfActiveで消える前に）
            CaptureVisualStateIfActive();
            // 前回のブレンドが動作中の場合、クリーン値に復元してからオフセット汚染を除去
            RestoreCleanIfActive();

            var hipsTransform = _boneTransformCache[HumanBodyBones.Hips];

            _bones = new BoneBlendData[1];
            _bones[0].transform = hipsTransform;

            // 旧IBのビジュアル状態があればそれを使用（旧IBオフセット消失ジャンプ防止）
            if (_oldVisualState.TryGetValue(hipsTransform, out var visualPose))
            {
                _bones[0].previousLocalPosition = visualPose.localPos;
                _bones[0].previousLocalRotation = visualPose.localRot;
            }
            else if (_lastCleanPose.TryGetValue(hipsTransform, out var cleanPose))
            {
                _bones[0].previousLocalPosition = cleanPose.localPos;
                _bones[0].previousLocalRotation = cleanPose.localRot;
            }
            else
            {
                _bones[0].previousLocalPosition = hipsTransform.localPosition;
                _bones[0].previousLocalRotation = hipsTransform.localRotation;
            }
            _boneCount = 1;

            BeginBlend(duration);
        }

        /// <summary>
        /// 前回のブレンドが動作中の場合、ボーンをクリーン値に復元する。
        /// StartInertialBlendがUpdate(20000)より前に呼ばれた場合に、
        /// 前回ブレンドのオフセットが新ブレンドに影響しないようにする。
        /// </summary>
        private void RestoreCleanIfActive()
        {
            if (_isOffsetApplied && _bones != null)
            {
                for (int i = 0; i < _boneCount; i++)
                {
                    _bones[i].transform.localPosition = _bones[i].cleanLocalPosition;
                    _bones[i].transform.localRotation = _bones[i].cleanLocalRotation;
                }
                _isOffsetApplied = false;
            }
        }

        /// <summary>
        /// 旧IBがアクティブな場合、各ボーンの現在のビジュアル状態
        /// （clean + 旧IBオフセット）を_oldVisualStateに保存する。
        /// RestoreCleanIfActiveの前に呼び出し、新IBの「前ポーズ」として使用することで、
        /// 旧IBオフセット消失による視覚的ジャンプを防止する。
        /// </summary>
        private void CaptureVisualStateIfActive()
        {
            _oldVisualState.Clear();
            if (_isOffsetApplied && _bones != null)
            {
                for (int i = 0; i < _boneCount; i++)
                {
                    _oldVisualState[_bones[i].transform] = (
                        _bones[i].transform.localPosition,
                        _bones[i].transform.localRotation
                    );
                }
            }
        }

        /// <summary>
        /// 現在のボーン状態を_lastCleanPoseとしてスナップショットする。
        /// AdditiveOverrideHelper停止前に呼ぶことで、AO補正込みの実際の表示ポーズを
        /// 次回StartInertialBlendの「前のポーズ」として使用できる。
        ///
        /// 通常_lastCleanPoseはLateUpdate(20000)でAnimator出力（AO適用前）を保存するが、
        /// ForceStopThinking/Emote時にAOを停止してPlayStateすると、IBが_lastCleanPose
        /// （AO補正なし=例えば立ちポーズ）をソースとして使い、AO補正済み表示ポーズ
        /// （座りポーズ）との差が一瞬見えるポーズフラッシュが発生する。
        /// この問題を回避するため、AO停止前にAO補正込みのポーズをキャプチャする。
        ///
        /// 注: 次回LateUpdate/PrePassのUpdateCleanPoseCacheで正しい値に上書きされるため
        /// 一時的な上書きに留まる。
        /// </summary>
        public void SnapshotCurrentPoseAsClean()
        {
            UpdateCleanPoseCache();
        }

        /// <summary>
        /// 全Humanoidボーンのクリーンポーズキャッシュを更新。
        /// LateUpdateの先頭（Animator評価直後・他コンポーネントの適用前）で毎フレーム呼ばれる。
        /// ブレンド非動作時も継続更新することで、次回StartInertialBlend時にStale値を防ぐ。
        /// </summary>
        private void UpdateCleanPoseCache()
        {
            if (_boneTransformCache == null) return;
            foreach (var kvp in _boneTransformCache)
            {
                var t = kvp.Value;
                if (t != null)
                {
                    _lastCleanPose[t] = (t.localPosition, t.localRotation);
                }
            }
        }

        /// <summary>
        /// ブレンドを完了し、全ボーンをクリーン位置に戻してアイドル状態に遷移する。
        /// PrePassパスと通常パスの両方から呼ばれる共通完了処理。
        /// </summary>
        private void CompleteBlend()
        {
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].transform.localPosition = _bones[i].cleanLocalPosition;
                _bones[i].transform.localRotation = _bones[i].cleanLocalRotation;
            }
            _isOffsetApplied = false;
            _state = State.Idle;
            _isActive = false;
            Debug.Log("[InertialBlendHelper] Blend completed");
        }

        /// <summary>
        /// ブレンド状態の初期化（共通処理）
        /// </summary>
        private void BeginBlend(float duration)
        {
            _blendDuration = duration > 0f ? duration : DEFAULT_BLEND_DURATION;
            _omega = LN_100 / _blendDuration;
            _elapsedTime = 0f;
            _isOffsetApplied = false;

            _state = State.WaitingFirstFrame;
            _isActive = true;

            Debug.Log($"[InertialBlendHelper] StartInertialBlend - " +
                $"boneCount={_boneCount}, duration={_blendDuration}, frame={Time.frameCount}");
        }

        /// <summary>
        /// Update: フレームの最初（Animatorが走る前）に、前フレームで適用したオフセットをリセット
        /// </summary>
        private void Update()
        {
            // 前フレームで慣性補間を適用していた場合、
            // Animatorが計算を始める前に「クリーンな位置」に戻しておく
            if (_isOffsetApplied && _bones != null)
            {
                for (int i = 0; i < _boneCount; i++)
                {
                    _bones[i].transform.localPosition = _bones[i].cleanLocalPosition;
                    _bones[i].transform.localRotation = _bones[i].cleanLocalRotation;
                }
                _isOffsetApplied = false;
            }
        }

        /// <summary>
        /// LateUpdate: Animatorが新しいポーズを決定した直後に、慣性補間を適用
        ///
        /// 【重要】クリーンポーズキャッシュは常時更新する。
        /// ブレンド非動作中も更新し続けないと、次回StartInertialBlend時に
        /// 数千フレーム前のStale値を「前のポーズ」として使ってしまう。
        /// </summary>
        private void LateUpdate()
        {
            // PrePassが処理を完了済みの場合、
            // クリーンキャッシュ更新・ポーズ適用済みなのでステート遷移・完了チェックのみ行う
            if (_prePassApplied)
            {
                _prePassApplied = false;
                if (_state == State.WaitingFirstFrame)
                {
                    _state = State.WaitingSecondFrame;
                }
                else if (_state == State.WaitingSecondFrame)
                {
                    // PrePassがクリーン保存・前ポーズ適用済み。ブレンド初期化と最初のオフセット適用を行う。
                    InitializeBlend();
                    _state = State.Blending;
                    CalculateAndApplyOffset();
                }
                else if (_state == State.Blending)
                {
                    // PrePassがクリーン保存・_elapsedTime更新・オフセット適用済み。
                    // 完了チェックのみ行う。
                    if (_elapsedTime >= _blendDuration)
                    {
                        // オフセット適用済みの状態を維持したまま終了
                        // （CompleteBlend()のスナップによる1Fポップを回避）
                        _state = State.Idle;
                        _isActive = false;
                        Debug.Log("[InertialBlendHelper] Blend completed");
                    }
                }
                return;
            }

            // 常にクリーンポーズキャッシュを更新（ブレンド状態に関係なく）
            // この時点はAnimator評価直後・AdditiveOverride/LookAt適用前のため、
            // 真のAnimator出力値（クリーン値）が取得できる
            UpdateCleanPoseCache();

            if (_state == State.Idle) return;
            if (_bones == null || _boneCount == 0) return;

            // ブレンド対象ボーンのクリーン値をBoneBlendDataにも保存
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].cleanLocalPosition = _bones[i].transform.localPosition;
                _bones[i].cleanLocalRotation = _bones[i].transform.localRotation;
            }

            switch (_state)
            {
                case State.WaitingFirstFrame:
                    // 1フレーム目 - Animatorが新ポーズを適用済みだが、初期化は次フレームで行う
                    // ちらつき防止: 前のポーズを維持してそのまま描画させる
                    for (int i = 0; i < _boneCount; i++)
                    {
                        _bones[i].transform.localPosition = _bones[i].previousLocalPosition;
                        _bones[i].transform.localRotation = _bones[i].previousLocalRotation;
                    }
                    _isOffsetApplied = true;
                    _state = State.WaitingSecondFrame;
                    break;

                case State.WaitingSecondFrame:
                    // 2フレーム目 - この時点でAnimatorは確実に新しいアニメーションのポーズを適用済み
                    InitializeBlend();
                    _state = State.Blending;
                    // 初期化直後、すぐにオフセットを計算・適用
                    CalculateAndApplyOffset();
                    break;

                case State.Blending:
                    // オフセットを計算・適用
                    _elapsedTime += Time.deltaTime;
                    CalculateAndApplyOffset();

                    // ブレンド完了チェック
                    if (_elapsedTime >= _blendDuration)
                    {
                        // オフセット適用済みの状態を維持したまま終了
                        // 次フレームのUpdate()でclean positionに復元され、
                        // LateUpdate()ではIdle状態のため追加オフセットなし → 自然に収束
                        // （CompleteBlend()のスナップによる1Fポップを回避）
                        _state = State.Idle;
                        _isActive = false;
                        Debug.Log("[InertialBlendHelper] Blend completed");
                    }
                    break;
            }
        }

        #region Public Query

        /// <summary>
        /// 慣性補間が動作中かどうか
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// 現在アクティブなブレンドの残り時間（非アクティブ時は0）
        /// </summary>
        public float RemainingDuration => _isActive ? Mathf.Max(0f, _blendDuration - _elapsedTime) : 0f;

        /// <summary>
        /// 動作中の慣性補間をキャンセルする。
        /// オフセットが適用済みの場合はクリーン値に復元してから停止する。
        /// </summary>
        public void CancelBlend()
        {
            if (_state == State.Idle) return;

            // オフセットが適用済みならクリーン値に復元
            RestoreCleanIfActive();

            _state = State.Idle;
            _isActive = false;
            Debug.Log("[InertialBlendHelper] Blend cancelled");
        }

        /// <summary>
        /// PrePass用: FastSpringBoneService(11010)前にポーズ補正を適用する。
        /// InertialBlendPrePass(11005)から毎フレーム呼ばれる。
        ///
        /// 実行順序: Vrm10Instance(11000) → PrePass(11005) → FastSpringBoneService(11010)
        ///
        /// Vrm10InstanceのControlRigがAnimator出力をボーンに適用した後、
        /// SpringBone計算前にIB補正済みポーズを適用する。これにより：
        /// - WaitingFirstFrame/SecondFrame: SpringBoneが新Timelineの突然のポーズ変化に反応しない
        /// - Blending: SpringBoneがIBオフセット適用済みの滑らかなポーズを見る
        ///   （PrePass無しではSpringBoneが生ポーズを見てしまい、髪・揺れ物がポップする）
        ///
        /// 注: 10999（Vrm10Instance前）ではControlRigが上書きするため無効。
        /// 11005（Vrm10Instance後・FastSpringBone前）で実行する必要がある。
        /// </summary>
        public void ApplyPrePassIfNeeded()
        {
            if (_bones == null || _boneCount == 0) return;

            // Blending中: IBオフセット適用済みポーズをSpringBone前に適用
            if (_state == State.Blending)
            {
                // クリーンポーズキャッシュを更新（ControlRig適用後 = 新Timelineの生ポーズ）
                UpdateCleanPoseCache();

                // ブレンド対象ボーンのクリーン値を保存
                for (int i = 0; i < _boneCount; i++)
                {
                    _bones[i].cleanLocalPosition = _bones[i].transform.localPosition;
                    _bones[i].cleanLocalRotation = _bones[i].transform.localRotation;
                }

                // 経過時間を更新してIBオフセットを計算・適用
                // （IB.LateUpdate(20000)と同じ計算をSpringBone前に実行）
                _elapsedTime += Time.deltaTime;
                CalculateAndApplyOffset();

                _prePassApplied = true;
                return;
            }

            // WaitingFirstFrame/WaitingSecondFrame: 前ポーズを適用
            if (_state != State.WaitingFirstFrame && _state != State.WaitingSecondFrame) return;

            // クリーンポーズキャッシュを更新（ControlRig適用後の値 = 新Timelineのポーズ）
            UpdateCleanPoseCache();

            // ブレンド対象ボーンのクリーン値を保存
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].cleanLocalPosition = _bones[i].transform.localPosition;
                _bones[i].cleanLocalRotation = _bones[i].transform.localRotation;
            }

            // 前ポーズを適用（SpringBoneが正しいポーズで計算できるように）
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].transform.localPosition = _bones[i].previousLocalPosition;
                _bones[i].transform.localRotation = _bones[i].previousLocalRotation;
            }
            _isOffsetApplied = true;

            _prePassApplied = true;
        }

        /// <summary>
        /// 指定ボーンのクリーンポーズ（Animator出力値）を取得。
        /// LateUpdate(20000)で保存した値を返すため、他コンポーネントが
        /// IB/AO/LookAtオフセット適用前の真のAnimator出力を取得できる。
        /// CharacterLookAtController等がPre-Update Restoreパターンで使用。
        /// </summary>
        public bool TryGetCleanPose(Transform bone, out Vector3 cleanLocalPos, out Quaternion cleanLocalRot)
        {
            if (bone != null && _lastCleanPose.TryGetValue(bone, out var pose))
            {
                cleanLocalPos = pose.localPos;
                cleanLocalRot = pose.localRot;
                return true;
            }
            cleanLocalPos = bone != null ? bone.localPosition : Vector3.zero;
            cleanLocalRot = bone != null ? bone.localRotation : Quaternion.identity;
            return false;
        }

        #endregion

        #region Core Logic

        /// <summary>
        /// 慣性補間の初期化（ローカル座標）
        /// 全ボーンのオフセットを計算
        /// </summary>
        private void InitializeBlend()
        {
            for (int i = 0; i < _boneCount; i++)
            {
                // オフセット = 前のポーズ - 新しいアニメーションのポーズ（ローカル座標）
                _bones[i].initialPositionOffset = _bones[i].previousLocalPosition - _bones[i].cleanLocalPosition;
                _bones[i].initialRotationOffset = Quaternion.Inverse(_bones[i].cleanLocalRotation) * _bones[i].previousLocalRotation;
            }

            Debug.Log($"[InertialBlendHelper] InitializeBlend - " +
                $"posOffset={_bones[0].initialPositionOffset}, rotOffset={_bones[0].initialRotationOffset.eulerAngles}, " +
                $"frame={Time.frameCount}");
        }

        /// <summary>
        /// 慣性補間のオフセットを計算して適用
        /// </summary>
        private void CalculateAndApplyOffset()
        {
            // Critical Damping減衰を計算（1回だけ）
            // x(t) = x₀ · (1 + ω·t) · e^(-ω·t)
            float t = _elapsedTime;
            float expDecay = Mathf.Exp(-_omega * t);
            float decay = (1f + _omega * t) * expDecay;

            // 全ボーンに適用
            for (int i = 0; i < _boneCount; i++)
            {
                // 位置オフセット
                Vector3 posOffset = _bones[i].initialPositionOffset * decay;

                // 回転オフセット（Slerpで減衰）
                Quaternion rotOffset = Quaternion.Slerp(
                    Quaternion.identity,
                    _bones[i].initialRotationOffset,
                    decay
                );

                // オフセットを適用（ローカル座標）
                _bones[i].transform.localPosition = _bones[i].cleanLocalPosition + posOffset;
                _bones[i].transform.localRotation = _bones[i].cleanLocalRotation * rotOffset;
            }
            _isOffsetApplied = true;
        }

        #endregion
    }
}
