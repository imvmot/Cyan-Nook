using UnityEngine;
using UniVRM10;
using CyanNook.Timeline;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターの視線制御を担当
    /// Timeline LookAtTrack と連携し、Clipが存在する期間のみ視線追従を行う
    ///
    /// アーキテクチャ（Pre-Update Restoreパターン）:
    /// 1. Update: 前フレームで適用したHead/Chestオフセットをリセット + ターゲット位置ブレンド
    /// 2. Animator: クリーンな状態から新しいポーズを計算
    /// 3. ProcessFrame (Mixer): Timeline Clipの設定をSetTimelineLookAtState()で伝達
    /// 4. LateUpdate: クリーン回転を保存 → Eye/Head/Chestの回転を適用
    ///
    /// LookAtTrackが無いTimelineに切り替わった場合:
    ///   ProcessFrameが呼ばれなくなるため、フレームカウントで未更新を検出し
    ///   _targetWeight を 0 に落とす → _effectiveWeight が徐々に減衰して自然に解除
    /// </summary>
    // InertialBlendHelper(20000)の後、かつAdditiveOverrideHelper(20050)の後に実行
    // LookAtの「クリーン」スナップショットがInertialBlendオフセットに汚染されないよう、
    // InertialBlendHelperがクリーン保存→オフセット適用を完了した後にLookAtを処理する
    [DefaultExecutionOrder(20100)]
    public class CharacterLookAtController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("プレイヤー（カメラ）のTransform")]
        public Transform playerTarget;

        [Tooltip("InertialBlendHelper（クリーンポーズ取得用）")]
        public InertialBlendHelper inertialBlendHelper;

        [Header("Settings")]
        [Tooltip("視線追従の速度")]
        public float lookAtSpeed = 3f;

        [Tooltip("視線追従を有効にするか（マスタースイッチ）")]
        public bool enableLookAt = true;

        [Header("Bone Rotation Smoothing")]
        [Tooltip("ボーン回転のスムージングフレーム数（角度制限付近でのフリップ防止）")]
        public int boneSmoothFrames = 10;

        [Header("Microsaccade")]
        [Tooltip("マイクロサッケード周期（秒）")]
        public float microsaccadeInterval = 0.5f;

        [Tooltip("マイクロサッケード量（0~1）")]
        [Range(0f, 1f)]
        public float microsaccadeAmount = 0.01f;

        [Header("State")]
        [SerializeField]
        private LookAtTarget _currentTargetType = LookAtTarget.None;

        [SerializeField]
        private Vector3 _targetPosition;

        [SerializeField]
        private Vector3 _currentLookAtPosition;

        private Vrm10Instance _vrmInstance;
        private Transform _headBone;
        private Transform _chestBone;
        private Transform _trackingTransform;  // LookAtTransform用

        // --- Timeline LookAt 設定（Mixerから毎フレーム更新） ---
        private bool _eyeEnabled;
        // head/chestのenabled/disabledは_headTargetFactor/_chestTargetFactorで管理
        // （上記のボーン別ブレンドファクターセクション参照）
        private float _headAngleLimitX = 45f;
        private float _headAngleLimitY = 45f;
        private float _chestAngleLimitX = 10f;
        private float _chestAngleLimitY = 10f;

        // --- Weight 補間 ---
        private float _targetWeight;       // Mixerから受け取る目標weight
        private float _effectiveWeight;    // 実際に適用する補間済みweight
        private int _blendFrames = 60;     // 補間フレーム数
        private float _blendSpeed;         // 1フレームあたりのweight変化量

        // --- フレーム更新検出 ---
        private int _lastSetFrame = -1;    // SetTimelineLookAtStateが最後に呼ばれたフレーム

        // --- Microsaccade ---
        private float _microsaccadeTimer;
        private Vector3 _microsaccadeOffset;

        // --- ボーン別ブレンドファクター ---
        // enabled/disabledの切替時に1F popを防ぐためスムーズに補間
        private float _headTargetFactor = 1f;
        private float _headEffectiveFactor = 1f;
        private float _chestTargetFactor = 1f;
        private float _chestEffectiveFactor = 1f;

        // --- ボーン回転スムージング用 ---
        // クランプ後のワールドオフセット回転を前フレームからSlerpで補間し、
        // 角度制限付近での瞬時フリップ（-45°→+45°等）を防ぐ
        private Quaternion _headSmoothedOffset = Quaternion.identity;
        private Quaternion _chestSmoothedOffset = Quaternion.identity;

        // --- Pre-Update Restore用 ---
        private Quaternion _headCleanLocalRotation;
        private Quaternion _chestCleanLocalRotation;
        private bool _isLookAtOffsetApplied;

        private void Update()
        {
            // Pre-Update Restore: 前フレームで適用したHead/Chestオフセットをリセット
            if (_isLookAtOffsetApplied)
            {
                if (_headBone != null)
                    _headBone.localRotation = _headCleanLocalRotation;
                if (_chestBone != null)
                    _chestBone.localRotation = _chestCleanLocalRotation;
                _isLookAtOffsetApplied = false;
            }

            // Mixerが2フレーム以上呼ばれていない → LookAtTrackが無いTimelineに切り替わった
            // 1フレームの猶予を設ける理由:
            // Timeline切替時（director.Stop()→director.Play()）にProcessFrameが
            // 1フレームだけ呼ばれないギャップが発生する。この1フレームで_targetWeight=0に
            // してしまうと_effectiveWeightが一瞬下がり、SpringBoneが揺れるポップの原因になる。
            if (_lastSetFrame >= 0 && _lastSetFrame < Time.frameCount - 1)
            {
                _targetWeight = 0f;
                _headTargetFactor = 0f;
                _chestTargetFactor = 0f;
            }

            if (!enableLookAt) return;

            // effectiveWeight > 0 またはtargetWeight > 0 の場合はターゲット更新を続ける
            if (_effectiveWeight <= 0f && _targetWeight <= 0f) return;

            UpdateTargetPosition();
            BlendLookAtPosition();
            UpdateMicrosaccade();
        }

        private void LateUpdate()
        {
            if (!enableLookAt) return;

            // effectiveWeight を targetWeight に向けて補間
            if (_blendFrames > 0)
            {
                _blendSpeed = 1f / _blendFrames;
            }
            else
            {
                _blendSpeed = 1f;
            }
            _effectiveWeight = Mathf.MoveTowards(_effectiveWeight, _targetWeight, _blendSpeed);

            // ボーン別ファクターも同じ速度で補間（enabled/disabled切替時のポップ防止）
            _headEffectiveFactor = Mathf.MoveTowards(_headEffectiveFactor, _headTargetFactor, _blendSpeed);
            _chestEffectiveFactor = Mathf.MoveTowards(_chestEffectiveFactor, _chestTargetFactor, _blendSpeed);

            // effectiveWeight が 0 かつ全ファクターも 0 なら何もしない
            if (_effectiveWeight <= 0f && _headEffectiveFactor <= 0f && _chestEffectiveFactor <= 0f)
            {
                // 完全に無効化されたら Eye もクリア
                ClearEyeLookAt();
                return;
            }

            float weight = _effectiveWeight;

            // クリーン回転を保存（Animatorが設定した値）
            // InertialBlendHelper(20000)がオフセットを適用済みのため、
            // ボーンの現在値ではなくIBのクリーンポーズキャッシュから取得する。
            // これにより、次フレームのUpdateでIBオフセットを含まない真のAnimator出力値に復元できる。
            if (_headBone != null)
            {
                if (inertialBlendHelper != null)
                    inertialBlendHelper.TryGetCleanPose(_headBone, out _, out _headCleanLocalRotation);
                else
                    _headCleanLocalRotation = _headBone.localRotation;
            }
            if (_chestBone != null)
            {
                if (inertialBlendHelper != null)
                    inertialBlendHelper.TryGetCleanPose(_chestBone, out _, out _chestCleanLocalRotation);
                else
                    _chestCleanLocalRotation = _chestBone.localRotation;
            }

            // Eye: VRM LookAt API
            if (_eyeEnabled)
            {
                ApplyEyeLookAt(weight);
            }
            else
            {
                ClearEyeLookAt();
            }

            // Chest（Headより先に適用。体幹→頭の順）
            // _chestEffectiveFactorを乗算し、enabled/disabled遷移をスムーズ化
            float chestWeight = weight * _chestEffectiveFactor;
            if (chestWeight > 0f && _chestBone != null)
            {
                ApplyBoneLookAt(_chestBone, _chestAngleLimitX, _chestAngleLimitY, chestWeight, ref _chestSmoothedOffset);
            }

            // Head
            float headWeight = weight * _headEffectiveFactor;
            if (headWeight > 0f && _headBone != null)
            {
                ApplyBoneLookAt(_headBone, _headAngleLimitX, _headAngleLimitY, headWeight, ref _headSmoothedOffset);
            }

            _isLookAtOffsetApplied = headWeight > 0f || chestWeight > 0f;
        }

        #region Public API

        /// <summary>
        /// プレイヤーを見る
        /// </summary>
        public void LookAtPlayer()
        {
            _currentTargetType = LookAtTarget.Player;
            _trackingTransform = null;
        }

        /// <summary>
        /// 指定位置を見る（スナップショット、以降は追従しない）
        /// </summary>
        public void LookAtPosition(Vector3 position)
        {
            _currentTargetType = LookAtTarget.Position;
            _targetPosition = position;
            _trackingTransform = null;
        }

        /// <summary>
        /// 指定Transformを毎フレーム追跡して見る
        /// </summary>
        public void LookAtTransform(Transform target)
        {
            _currentTargetType = LookAtTarget.Position;
            _trackingTransform = target;
            if (target != null)
            {
                _targetPosition = target.position;
            }
        }

        /// <summary>
        /// 視線追従を停止（前を向く）
        /// </summary>
        public void LookForward()
        {
            _currentTargetType = LookAtTarget.None;
            _trackingTransform = null;
        }

        /// <summary>
        /// Timeline Mixer から毎フレーム呼び出される
        /// クリップの有無とパラメータを設定
        /// </summary>
        public void SetTimelineLookAtState(
            bool active, float weight,
            bool eye, bool head, float headLimitX, float headLimitY,
            bool chest, float chestLimitX, float chestLimitY,
            int blendFrames)
        {
            _lastSetFrame = Time.frameCount;
            _targetWeight = active ? weight : 0f;

            // active=false（Clip非存在）の場合はパラメータを更新しない
            // ブレンドアウト中は前のClip設定を維持して自然に減衰させる
            if (active)
            {
                _eyeEnabled = eye;
                // head/chestのenabled/disabledはboolフラグではなくターゲットファクター経由で
                // スムーズにブレンドする（1Fポップ防止）
                _headTargetFactor = head ? 1f : 0f;
                _headAngleLimitX = headLimitX;
                _headAngleLimitY = headLimitY;
                _chestTargetFactor = chest ? 1f : 0f;
                _chestAngleLimitX = chestLimitX;
                _chestAngleLimitY = chestLimitY;
                _blendFrames = Mathf.Max(1, blendFrames);
            }
        }

        /// <summary>
        /// VRM Instanceを設定（VRM読み込み時に呼び出し）
        /// </summary>
        public void SetVrmInstance(Vrm10Instance vrmInstance)
        {
            _vrmInstance = vrmInstance;

            if (vrmInstance != null)
            {
                var animator = vrmInstance.GetComponent<Animator>();
                if (animator != null)
                {
                    _headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                    _chestBone = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                              ?? animator.GetBoneTransform(HumanBodyBones.Chest);
                }
            }

            Debug.Log($"[CharacterLookAtController] VRM LookAt initialized (Head={_headBone != null}, Chest={_chestBone != null})");
        }

        /// <summary>
        /// プレイヤーターゲットを設定
        /// </summary>
        public void SetPlayerTarget(Transform target)
        {
            playerTarget = target;
        }

        #endregion

        #region Private Methods

        private void UpdateMicrosaccade()
        {
            if (microsaccadeAmount <= 0f || !_eyeEnabled) return;

            _microsaccadeTimer += Time.deltaTime;
            if (_microsaccadeTimer >= microsaccadeInterval)
            {
                _microsaccadeTimer -= microsaccadeInterval;
                GenerateMicrosaccadeOffset();
            }
        }

        private void GenerateMicrosaccadeOffset()
        {
            // 視線方向に垂直な平面上でランダム方向にオフセット
            Transform refBone = _headBone ?? transform;
            Vector3 gazeDir = (_currentLookAtPosition - refBone.position).normalized;

            if (gazeDir.sqrMagnitude < 0.001f)
            {
                _microsaccadeOffset = Vector3.zero;
                return;
            }

            Vector3 right = Vector3.Cross(Vector3.up, gazeDir).normalized;
            Vector3 up = Vector3.Cross(gazeDir, right).normalized;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            _microsaccadeOffset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * microsaccadeAmount;
        }

        private void UpdateTargetPosition()
        {
            switch (_currentTargetType)
            {
                case LookAtTarget.Player:
                    if (playerTarget != null)
                    {
                        _targetPosition = playerTarget.position;
                    }
                    break;

                case LookAtTarget.Position:
                    // Transformが設定されていれば毎フレーム追跡
                    if (_trackingTransform != null)
                    {
                        _targetPosition = _trackingTransform.position;
                    }
                    break;

                case LookAtTarget.None:
                    // 正面を向く
                    _targetPosition = transform.position + transform.forward * 10f;
                    break;
            }
        }

        private void BlendLookAtPosition()
        {
            _currentLookAtPosition = Vector3.Lerp(
                _currentLookAtPosition,
                _targetPosition,
                Time.deltaTime * lookAtSpeed
            );
        }

        /// <summary>
        /// VRM LookAt API で目の視線を適用
        /// </summary>
        private void ApplyEyeLookAt(float weight)
        {
            if (_vrmInstance == null || _vrmInstance.Runtime == null || _vrmInstance.Runtime.LookAt == null) return;

            // マイクロサッケード: 通常のLookAt位置にオフセットを加算
            Vector3 lookAtPos = _currentLookAtPosition + _microsaccadeOffset;

            if (weight >= 1f)
            {
                _vrmInstance.Runtime.LookAt.LookAtInput = new LookAtInput { WorldPosition = lookAtPos };
            }
            else
            {
                // weight < 1: ターゲット位置を正面とブレンド
                Vector3 forwardPos = _headBone != null
                    ? _headBone.position + _headBone.forward * 10f
                    : transform.position + transform.forward * 10f;
                Vector3 blendedPos = Vector3.Lerp(forwardPos, lookAtPos, weight);
                _vrmInstance.Runtime.LookAt.LookAtInput = new LookAtInput { WorldPosition = blendedPos };
            }
        }

        /// <summary>
        /// VRM LookAt の視線をクリア（正面を向く）
        /// </summary>
        private void ClearEyeLookAt()
        {
            if (_vrmInstance == null || _vrmInstance.Runtime == null || _vrmInstance.Runtime.LookAt == null) return;
            // 正面方向を向かせてリセット（LookAtInputはvalue typeのためnull不可）
            Vector3 forwardPos = _headBone != null
                ? _headBone.position + _headBone.forward * 10f
                : transform.position + transform.forward * 10f;
            _vrmInstance.Runtime.LookAt.LookAtInput = new LookAtInput { WorldPosition = forwardPos };
        }

        /// <summary>
        /// ボーン（Head/Chest共通）をターゲット方向に回転させる
        ///
        /// 方式:
        /// 1. ワールド空間でアニメーション回転→ターゲット方向へのオフセットを計算
        /// 2. ワールド空間のオイラー角（Pitch/Yaw）でクランプ
        /// 3. クランプ後のオフセットをスムージング（前フレームからSlerp補間でフリップ防止）
        /// 4. スムージング済みオフセットをローカルに変換して適用
        ///
        /// Hipsに角度がある場合でもワールド基準で角度計算するため正しい方向を向く
        /// </summary>
        private void ApplyBoneLookAt(Transform bone, float limitX, float limitY, float weight, ref Quaternion smoothedOffset)
        {
            Vector3 direction = _currentLookAtPosition - bone.position;
            if (direction.sqrMagnitude < 0.001f) return;

            // ターゲット方向のワールド回転
            Quaternion targetWorldRotation = Quaternion.LookRotation(direction, Vector3.up);

            // アニメーションが設定したボーンの現在のワールド回転
            Quaternion animWorldRotation = bone.rotation;

            // ワールド空間でのオフセット回転（アニメーション姿勢→ターゲット方向）
            Quaternion worldOffset = targetWorldRotation * Quaternion.Inverse(animWorldRotation);

            // ワールド空間のオイラー角で角度制限を適用
            Vector3 euler = worldOffset.eulerAngles;
            euler.x = ClampAngle(euler.x, -limitX, limitX);
            euler.y = ClampAngle(euler.y, -limitY, limitY);
            euler.z = 0f; // Rollは無効

            Quaternion clampedWorldOffset = Quaternion.Euler(euler);

            // スムージング: 前フレームのオフセットからSlerpで補間
            // 角度制限付近でクランプ値が瞬時にフリップ（-45°→+45°等）するのを防ぐ
            float smoothT = boneSmoothFrames > 1 ? (1f / boneSmoothFrames) : 1f;
            smoothedOffset = Quaternion.Slerp(smoothedOffset, clampedWorldOffset, smoothT);

            // スムージング済みオフセットをアニメーション回転に適用してワールド回転を得る
            Quaternion smoothedWorldRotation = smoothedOffset * animWorldRotation;

            // ワールド回転をローカル回転に変換
            Quaternion parentRotation = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
            Quaternion smoothedLocalRotation = Quaternion.Inverse(parentRotation) * smoothedWorldRotation;

            // アニメーション回転とブレンド（weight考慮）
            Quaternion animationRotation = bone.localRotation;
            bone.localRotation = Quaternion.Slerp(animationRotation, smoothedLocalRotation, weight);
        }

        private float ClampAngle(float angle, float min, float max)
        {
            if (angle > 180f) angle -= 360f;
            return Mathf.Clamp(angle, min, max);
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            // 視線ターゲットを表示
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_currentLookAtPosition, 0.1f);

            if (_headBone != null)
            {
                Gizmos.DrawLine(_headBone.position, _currentLookAtPosition);
            }
            else
            {
                Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, _currentLookAtPosition);
            }
        }
    }

    public enum LookAtTarget
    {
        None,       // 視線追従なし
        Player,     // プレイヤーを追従
        Position    // 指定位置を注視
    }
}
