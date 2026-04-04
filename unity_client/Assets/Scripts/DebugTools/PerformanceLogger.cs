using UnityEngine;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CyanNook.DebugTools
{
    /// <summary>
    /// パフォーマンスログ収集・エクスポート
    /// Application.logMessageReceivedを購読し、全ログをリングバッファに蓄積する。
    /// DebugSettingsPanelのExportLogボタンからテキストファイルとしてダウンロード可能。
    /// </summary>
    public class PerformanceLogger : MonoBehaviour
    {
        public static PerformanceLogger Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("リングバッファの最大エントリ数")]
        [SerializeField] private int bufferCapacity = 2000;

        private string[] _buffer;
        private int _writeIndex;
        private int _count;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FileIO_DownloadText(string filename, string content);
#else
        private static void FileIO_DownloadText(string filename, string content)
        {
            // エディター/スタンドアロン: ファイル書き出し
            var path = System.IO.Path.Combine(Application.persistentDataPath, filename);
            System.IO.File.WriteAllText(path, content);
            Debug.Log($"[PerformanceLogger] Log saved to: {path}");
        }
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _buffer = new string[bufferCapacity];
            _writeIndex = 0;
            _count = 0;
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnLogMessage(string logString, string stackTrace, LogType type)
        {
            string typeTag;
            switch (type)
            {
                case LogType.Error:
                    typeTag = "ERR";
                    break;
                case LogType.Warning:
                    typeTag = "WRN";
                    break;
                case LogType.Exception:
                    typeTag = "EXC";
                    break;
                default:
                    typeTag = "LOG";
                    break;
            }

            string entry = $"[{Time.frameCount}] {DateTime.Now:HH:mm:ss.fff} [{typeTag}] {logString}";

            // エラー/例外はスタックトレースの先頭1行も追加
            if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
            {
                int newlineIndex = stackTrace.IndexOf('\n');
                string firstLine = newlineIndex >= 0 ? stackTrace.Substring(0, newlineIndex) : stackTrace;
                entry += "\n  > " + firstLine.Trim();
            }

            _buffer[_writeIndex] = entry;
            _writeIndex = (_writeIndex + 1) % bufferCapacity;
            if (_count < bufferCapacity)
                _count++;
        }

        /// <summary>
        /// バッファ内の全ログを時系列順で結合して返す
        /// </summary>
        public string ExportLog()
        {
            if (_count == 0) return "(empty log)";

            var sb = new StringBuilder(_count * 100);
            sb.AppendLine($"=== Cyan-Nook Performance Log ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===");
            sb.AppendLine($"Platform: {Application.platform}, Entries: {_count}");
            sb.AppendLine();

            int startIndex = _count < bufferCapacity ? 0 : _writeIndex;
            for (int i = 0; i < _count; i++)
            {
                int index = (startIndex + i) % bufferCapacity;
                sb.AppendLine(_buffer[index]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// ログをテキストファイルとしてダウンロード（WebGL）/保存（エディター）
        /// </summary>
        public void DownloadLog()
        {
            string content = ExportLog();
            string filename = $"cyannook_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            FileIO_DownloadText(filename, content);
        }

        /// <summary>
        /// バッファをクリア
        /// </summary>
        public void ClearLog()
        {
            _writeIndex = 0;
            _count = 0;
            Debug.Log("[PerformanceLogger] Log buffer cleared");
        }
    }
}
