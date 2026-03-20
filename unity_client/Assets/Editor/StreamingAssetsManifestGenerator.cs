using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Collections.Generic;

namespace CyanNook.Editor
{
    /// <summary>
    /// StreamingAssetsのファイル一覧マニフェストを生成するエディタツール
    /// WebGLビルドではDirectory.GetFiles()が使えないため、
    /// ビルド前にマニフェストJSONを生成しておく必要がある
    /// </summary>
    public class StreamingAssetsManifestGenerator : IPreprocessBuildWithReport
    {
        private const string ManifestFileName = "file_manifest.json";

        public int callbackOrder => 0;

        /// <summary>
        /// ビルド前に自動実行
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            GenerateManifest();
        }

        [MenuItem("CyanNook/Generate StreamingAssets Manifest")]
        public static void GenerateManifest()
        {
            string streamingAssetsPath = Application.streamingAssetsPath;

            if (!Directory.Exists(streamingAssetsPath))
            {
                Debug.LogWarning("[ManifestGenerator] StreamingAssets folder not found");
                return;
            }

            var files = new List<string>();
            ScanDirectory(streamingAssetsPath, streamingAssetsPath, files);

            var manifest = new FileManifest { files = files.ToArray() };
            string json = JsonUtility.ToJson(manifest, true);

            string manifestPath = Path.Combine(streamingAssetsPath, ManifestFileName);
            File.WriteAllText(manifestPath, json);
            AssetDatabase.Refresh();

            Debug.Log($"[ManifestGenerator] Manifest generated: {files.Count} files → {manifestPath}");
        }

        private static void ScanDirectory(string rootPath, string currentPath, List<string> results)
        {
            foreach (var file in Directory.GetFiles(currentPath))
            {
                if (file.EndsWith(".meta") || Path.GetFileName(file) == ManifestFileName)
                    continue;

                // StreamingAssetsからの相対パス（フォワードスラッシュに統一）
                string relativePath = file
                    .Substring(rootPath.Length + 1)
                    .Replace("\\", "/");
                results.Add(relativePath);
            }

            foreach (var dir in Directory.GetDirectories(currentPath))
            {
                ScanDirectory(rootPath, dir, results);
            }
        }

        [System.Serializable]
        private class FileManifest
        {
            public string[] files;
        }
    }
}
