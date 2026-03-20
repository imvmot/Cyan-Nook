using UnityEngine;
using UnityEditor;
using System.IO;
using System.Reflection;

[InitializeOnLoad]
public static class FileExtensions {

	public static Color color;

	private static string prevGuid;
	private static GUIStyle labelStyle;
	private static bool isPro;
	private static EditorWindow projectWindow;
	private static float size = 2.0f;
	private static float halfSize = 1.0f;
	private static int columnsCount = 0;

	const int ONE_COLUMN = 0;
	const int TWO_COLUMNS = 1;

	static FileExtensions() {
		if (EditorGUIUtility.isProSkin) {
			color = Color.grey;
		}
		else {
			color = Color.white;
		}

		EditorApplication.projectWindowItemOnGUI += ListItemOnGUI;
	}

	private static void ListItemOnGUI(string guid, Rect rect) {
		if (Event.current.type == EventType.Repaint) {
			if (prevGuid != guid) {
				prevGuid = guid;

				if (labelStyle == null) {
					labelStyle = new GUIStyle(EditorStyles.boldLabel);
					labelStyle.normal.textColor = color;
				}

				string assetPath = AssetDatabase.GUIDToAssetPath(guid);
				Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

				if (obj != null && AssetDatabase.IsMainAsset(obj) && !(obj is DefaultAsset && !AssetDatabase.IsForeignAsset(obj))) {
					string extension = Path.GetExtension(assetPath);

					if (IsThumbnailsView) {
						labelStyle.normal.textColor = Color.black;

						rect.x -= halfSize;
						EditorGUI.LabelField(rect, extension, labelStyle);

						rect.x += size;
						EditorGUI.LabelField(rect, extension, labelStyle);

						rect.x -= halfSize;
						rect.y -= halfSize;
						EditorGUI.LabelField(rect, extension, labelStyle);

						rect.y += size;
						EditorGUI.LabelField(rect, extension, labelStyle);

						rect.y -= halfSize;
						labelStyle.normal.textColor = Color.white;
						EditorGUI.LabelField(rect, extension, labelStyle);
						labelStyle.normal.textColor = color;
					}
					else {
						string fileName = Path.GetFileNameWithoutExtension(assetPath);

						GUIContent content = new GUIContent(fileName);

#if UNITY_5_5
						rect.x += (16f + GUI.skin.label.CalcSize(content).x);
						rect.y += 1f;
						EditorGUI.LabelField(rect, extension, labelStyle);
#else
						rect.x += (16f + GUI.skin.label.CalcSize(content).x - GUI.skin.label.margin.left * (TWO_COLUMNS - columnsCount));
						rect.y += 1f;
						EditorGUI.LabelField(rect, extension, labelStyle);
#endif
					}
				}
			}
		}
	}

	private static bool IsThumbnailsView {
		get {
			projectWindow = GetProjectWindow();
			PropertyInfo gridSize = projectWindow.GetType().GetProperty("listAreaGridSize", BindingFlags.Instance | BindingFlags.Public);
			columnsCount = (int)projectWindow.GetType().GetField("m_ViewMode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(projectWindow);
			return columnsCount == TWO_COLUMNS && (float)gridSize.GetValue(projectWindow, null) > 16f;
		}
	}

	private static EditorWindow GetProjectWindow() {
		if (EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.titleContent.text == "Project") {
			return EditorWindow.focusedWindow;
		}

		return GetExistingWindowByName("Project");
	}

	private static EditorWindow GetExistingWindowByName(string name) {
		EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
		foreach (EditorWindow item in windows) {
			if (item.titleContent.text == name) {
				return item;
			}
		}

		return default(EditorWindow);
	}
}
