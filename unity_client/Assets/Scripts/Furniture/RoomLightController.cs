using UnityEngine;

namespace CyanNook.Furniture
{
    /// <summary>
    /// 部屋のライトON/OFF制御 + マテリアルEmission連動
    /// 初回起動演出やSleep状態など、様々な場所から参照して使う。
    /// </summary>
    public class RoomLightController : MonoBehaviour
    {
        [Header("Light Targets")]
        [Tooltip("制御対象Light（空ならシーン内全Lightを自動取得）")]
        [SerializeField] private Light[] targetLights;

        [Header("Emission Targets")]
        [Tooltip("ON/OFF時にEmissionを切り替えるマテリアル")]
        [SerializeField] private EmissionTarget[] emissionTargets;

        [Header("Initial State")]
        [Tooltip("ONにすると起動時から消灯状態で開始する")]
        [SerializeField] private bool startOff;

        [Header("Lightmap")]
        [Tooltip("ライトOFF時にベイク済みライトマップも無効化する")]
        [SerializeField] private bool disableLightmapOnOff = true;

        private float[] _originalIntensities;
        private bool _isOn = true;
        private LightmapData[] _cachedLightmaps;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>ライトが点灯中かどうか</summary>
        public bool IsOn => _isOn;

        private void Awake()
        {
            CacheLights();
            _cachedLightmaps = LightmapSettings.lightmaps;

            if (startOff)
                SetLights(false);
        }

        // ─────────────────────────────────────
        // Public API
        // ─────────────────────────────────────

        /// <summary>ライト点灯 + Emission ON</summary>
        public void SetLightsOn()
        {
            SetLights(true);
        }

        /// <summary>ライト消灯 + Emission OFF</summary>
        public void SetLightsOff()
        {
            SetLights(false);
        }

        /// <summary>ライトON/OFF切替</summary>
        public void SetLights(bool on)
        {
            _isOn = on;
            ApplyLightIntensities(on);
            ApplyEmissionState(on);
            ApplyLightmapState(on);
        }

        /// <summary>
        /// ライトリストを再キャッシュ（シーン変更時など）
        /// </summary>
        public void Reinitialize()
        {
            CacheLights();
        }

        // ─────────────────────────────────────
        // Internal
        // ─────────────────────────────────────

        private void CacheLights()
        {
            if (targetLights == null || targetLights.Length == 0)
            {
                targetLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            }

            _originalIntensities = new float[targetLights.Length];
            for (int i = 0; i < targetLights.Length; i++)
            {
                _originalIntensities[i] = targetLights[i] != null ? targetLights[i].intensity : 0f;
            }
        }

        private void ApplyLightIntensities(bool on)
        {
            if (targetLights == null || _originalIntensities == null) return;

            for (int i = 0; i < targetLights.Length; i++)
            {
                if (targetLights[i] != null)
                    targetLights[i].intensity = on ? _originalIntensities[i] : 0f;
            }
        }

        private void ApplyLightmapState(bool on)
        {
            if (!disableLightmapOnOff) return;

            if (on)
            {
                if (_cachedLightmaps != null)
                    LightmapSettings.lightmaps = _cachedLightmaps;
            }
            else
            {
                LightmapSettings.lightmaps = new LightmapData[0];
            }
        }

        private void ApplyEmissionState(bool on)
        {
            if (emissionTargets == null) return;

            foreach (var target in emissionTargets)
            {
                if (target.targetRenderer == null) continue;

                var materials = target.targetRenderer.sharedMaterials;
                if (target.materialIndex < 0 || target.materialIndex >= materials.Length) continue;

                var mat = materials[target.materialIndex];
                var color = on ? target.onColor : target.offColor;
                mat.SetColor(EmissionColorId, color);

                if (color == Color.black)
                    mat.DisableKeyword("_EMISSION");
                else
                    mat.EnableKeyword("_EMISSION");
            }
        }
    }

    /// <summary>
    /// Emission制御対象の設定データ
    /// </summary>
    [System.Serializable]
    public class EmissionTarget
    {
        [Tooltip("対象Renderer")]
        public Renderer targetRenderer;

        [Tooltip("対象マテリアルのインデックス（複数マテリアルの場合）")]
        public int materialIndex;

        [Tooltip("ライトON時のEmissionカラー（HDR）")]
        [ColorUsage(false, true)]
        public Color onColor = Color.white;

        [Tooltip("ライトOFF時のEmissionカラー（HDR）")]
        [ColorUsage(false, true)]
        public Color offColor = Color.black;
    }
}
