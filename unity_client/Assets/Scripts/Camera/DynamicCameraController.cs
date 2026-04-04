using UnityEngine;

namespace CyanNook.CameraControl
{
    /// <summary>
    /// MainCameraの動的制御
    /// - FOV: キャラクターとの距離に応じて動的変化
    /// - LookAt: Y軸のみのルックアット（遅延追従）
    /// - Height: カメラ高さ設定・保存
    /// </summary>
    public class DynamicCameraController : MonoBehaviour
    {
        [Header("Target")]
        [System.NonSerialized]
        public Transform targetCharacter;

        [Header("FOV Control")]
        [Tooltip("FOV制御を有効化")]
        public bool enableFovControl = true;

        [Tooltip("最小距離（この距離でFOV最小値）")]
        public float minDistance = 1.5f;

        [Tooltip("最大距離（この距離でFOV最大値）")]
        public float maxDistance = 5.0f;

        [Tooltip("最小FOV（近距離時・望遠）")]
        public float minFov = 30f;

        [Tooltip("最大FOV（遠距離時・広角）")]
        public float maxFov = 60f;

        [Tooltip("FOV変化のスムージング速度")]
        public float fovSmoothSpeed = 5f;

        [Header("Look At Control")]
        [Tooltip("Y軸ルックアットを有効化")]
        public bool enableLookAt = false;

        [Tooltip("ルックアット回転速度")]
        public float lookAtRotationSpeed = 2f;

        [Tooltip("ルックアット遅延時間（秒）")]
        public float lookAtDelay = 0.2f;

        private UnityEngine.Camera _camera;
        private Vector3 _delayedTargetPosition;
        private Quaternion _targetRotation;

        private void Start()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            if (_camera == null)
            {
                Debug.LogError("[DynamicCameraController] Camera component not found");
                enabled = false;
                return;
            }

            _targetRotation = transform.rotation;

            if (targetCharacter != null)
            {
                _delayedTargetPosition = targetCharacter.position;
            }

            // PlayerPrefsから設定を読み込み
            LoadSettings();
        }

        /// <summary>
        /// PlayerPrefsから設定を読み込み
        /// </summary>
        private void LoadSettings()
        {
            // カメラ高さ
            if (PlayerPrefs.HasKey("camera_height"))
            {
                float savedHeight = PlayerPrefs.GetFloat("camera_height");
                SetCameraHeight(savedHeight);
                Debug.Log($"[DynamicCameraController] Loaded saved camera height: {savedHeight}");
            }

            // ルックアット有効/無効
            if (PlayerPrefs.HasKey("camera_lookAtEnabled"))
            {
                enableLookAt = PlayerPrefs.GetInt("camera_lookAtEnabled") == 1;
                Debug.Log($"[DynamicCameraController] Loaded saved camera look at: {enableLookAt}");
            }

            // FOV設定
            if (PlayerPrefs.HasKey("camera_minFov"))
            {
                minFov = PlayerPrefs.GetFloat("camera_minFov");
                Debug.Log($"[DynamicCameraController] Loaded saved min FOV: {minFov}");
            }

            if (PlayerPrefs.HasKey("camera_maxFov"))
            {
                maxFov = PlayerPrefs.GetFloat("camera_maxFov");
                Debug.Log($"[DynamicCameraController] Loaded saved max FOV: {maxFov}");
            }
        }

        private void LateUpdate()
        {
            if (targetCharacter == null) return;

            // FOV制御
            if (enableFovControl)
            {
                UpdateFov();
            }

            // ルックアット制御
            if (enableLookAt)
            {
                UpdateLookAt();
            }
        }

        /// <summary>
        /// キャラクターとの距離に応じてFOVを動的変更
        /// </summary>
        private void UpdateFov()
        {
            // XZ平面距離計算（Y軸を無視）
            float distance = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(targetCharacter.position.x, targetCharacter.position.z)
            );

            // 距離→FOVマッピング
            float normalizedDistance = Mathf.InverseLerp(minDistance, maxDistance, distance);
            float targetFov = Mathf.Lerp(minFov, maxFov, normalizedDistance);

            // スムージング適用
            _camera.fieldOfView = Mathf.Lerp(
                _camera.fieldOfView,
                targetFov,
                Time.deltaTime * fovSmoothSpeed
            );
        }

        /// <summary>
        /// Y軸のみのルックアット（遅延追従）
        /// </summary>
        private void UpdateLookAt()
        {
            // 遅延追従位置を更新
            _delayedTargetPosition = Vector3.Lerp(
                _delayedTargetPosition,
                targetCharacter.position,
                Time.deltaTime / Mathf.Max(lookAtDelay, 0.01f)
            );

            // Y軸のみの方向ベクトル計算
            Vector3 direction = _delayedTargetPosition - transform.position;
            direction.y = 0;  // Y成分を0にして水平方向のみ

            // ほぼ同じ位置なら回転しない
            if (direction.sqrMagnitude < 0.001f) return;

            // 目標回転を計算（Y軸のみ）
            Quaternion targetRotation = Quaternion.LookRotation(direction);

            // 現在のX軸回転を保持
            Vector3 currentEuler = transform.rotation.eulerAngles;
            Vector3 targetEuler = targetRotation.eulerAngles;
            targetEuler.x = currentEuler.x;  // X軸（ピッチ）は維持
            targetEuler.z = currentEuler.z;  // Z軸（ロール）も維持

            _targetRotation = Quaternion.Euler(targetEuler);

            // スムージング回転
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                _targetRotation,
                Time.deltaTime * lookAtRotationSpeed
            );
        }

        /// <summary>
        /// ルックアット有効/無効を外部から設定
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public void SetLookAtEnabled(bool enabled)
        {
            enableLookAt = enabled;

            // 無効化時は現在の回転を保持
            if (!enabled)
            {
                _targetRotation = transform.rotation;
            }
        }

        /// <summary>
        /// ルックアット有効状態を取得
        /// </summary>
        public bool IsLookAtEnabled => enableLookAt;

        /// <summary>
        /// カメラ高さを設定
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public void SetCameraHeight(float height)
        {
            var pos = transform.position;
            transform.position = new Vector3(pos.x, height, pos.z);
        }

        /// <summary>
        /// 現在のカメラ高さを取得
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public float GetCameraHeight()
        {
            return transform.position.y;
        }

        /// <summary>
        /// 最小FOVを取得
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public float GetMinFov()
        {
            return minFov;
        }

        /// <summary>
        /// 最大FOVを取得
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public float GetMaxFov()
        {
            return maxFov;
        }

        /// <summary>
        /// 最小FOVを設定
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public void SetMinFov(float fov)
        {
            minFov = Mathf.Clamp(fov, 1f, 179f);
        }

        /// <summary>
        /// 最大FOVを設定
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public void SetMaxFov(float fov)
        {
            maxFov = Mathf.Clamp(fov, 1f, 179f);
        }

        /// <summary>
        /// カメラ設定をPlayerPrefsに保存
        /// AvatarSettingsPanelから呼ばれる
        /// </summary>
        public void SaveSettings()
        {
            PlayerPrefs.SetFloat("camera_height", transform.position.y);
            PlayerPrefs.SetInt("camera_lookAtEnabled", enableLookAt ? 1 : 0);
            PlayerPrefs.SetFloat("camera_minFov", minFov);
            PlayerPrefs.SetFloat("camera_maxFov", maxFov);
            PlayerPrefs.Save();
            Debug.Log($"[DynamicCameraController] Settings saved - Height: {transform.position.y}, LookAt: {enableLookAt}, MinFOV: {minFov}, MaxFOV: {maxFov}");
        }
    }
}
