using UnityEngine;
using System.Collections.Generic;

namespace CyanNook.Character
{
    /// <summary>
    /// シーン内の名前付きターゲット（mirror, window 等）を管理
    /// 子GameObjectの名前をキーとして登録し、移動先・LookAt先として使用
    ///
    /// シーン階層:
    ///   RoomTargets (本コンポーネント)
    ///   ├── mirror
    ///   │   └── mirror_lookattarget
    ///   └── window
    ///       └── window_lookattarget
    ///
    /// LLMが target.type に子の名前（"mirror" 等）を指定すると、
    /// CharacterControllerがその位置へ移動・LookAtを実行する
    /// </summary>
    public class RoomTargetManager : MonoBehaviour
    {
        private const string LookAtSuffix = "_lookattarget";

        private Dictionary<string, RoomTargetData> _targets = new Dictionary<string, RoomTargetData>();

        /// <summary>
        /// 登録済みターゲット名のコレクション（GetTargetType解決用）
        /// </summary>
        public ICollection<string> TargetNames => _targets.Keys;

        private void Awake()
        {
            ScanTargets();
        }

        /// <summary>
        /// 子GameObjectをスキャンしてターゲットを登録
        /// </summary>
        private void ScanTargets()
        {
            _targets.Clear();

            foreach (Transform child in transform)
            {
                string targetName = child.name.ToLower();

                // lookattarget 子オブジェクトを検索
                Transform lookAtTarget = null;
                string lookAtName = targetName + LookAtSuffix;
                foreach (Transform grandChild in child)
                {
                    if (grandChild.name.ToLower() == lookAtName)
                    {
                        lookAtTarget = grandChild;
                        break;
                    }
                }

                _targets[targetName] = new RoomTargetData
                {
                    name = targetName,
                    transform = child,
                    lookAtTarget = lookAtTarget
                };

                Debug.Log($"[RoomTargetManager] Registered: {targetName}" +
                    (lookAtTarget != null ? $" (lookAt: {lookAtTarget.name})" : " (no lookAt)"));
            }

            Debug.Log($"[RoomTargetManager] Total targets: {_targets.Count}");
        }

        /// <summary>
        /// 指定名のターゲットが存在するか
        /// </summary>
        public bool HasTarget(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return _targets.ContainsKey(name.ToLower());
        }

        /// <summary>
        /// 指定名のターゲットを取得
        /// </summary>
        public RoomTargetData GetTarget(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _targets.TryGetValue(name.ToLower(), out var target);
            return target;
        }

        /// <summary>
        /// LLMプロンプト用のターゲットリストを生成
        /// </summary>
        public string GenerateTargetListForPrompt()
        {
            if (_targets.Count == 0) return "  - なし";

            var lines = new List<string>();
            foreach (var kvp in _targets)
            {
                lines.Add($"  - {kvp.Key}");
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// RoomTarget の位置・LookAt情報
    /// </summary>
    public class RoomTargetData
    {
        public string name;
        public Transform transform;
        public Transform lookAtTarget;
    }
}
