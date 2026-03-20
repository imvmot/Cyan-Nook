using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using CyanNook.Character;
using CyanNook.CameraControl;
using CyanNook.UI;
using CyanNook.DebugTools;
using TMPro;

namespace CyanNook.Editor
{
    /// <summary>
    /// VRMテストシーンのセットアップ用エディタスクリプト
    /// </summary>
    public static class VrmTestSceneSetup
    {
        // フォントアセットパス
        private const string FontPath_UI = "Assets/Fonts/Mplus2-SemiBold SDF.asset";
        private const string FontPath_Code = "Assets/Fonts/Mplus1Code-Regular SDF.asset";

        [MenuItem("CyanNook/Setup VRM Test Scene")]
        public static void SetupTestScene()
        {
            // 新規シーン作成
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // カメラの設定
            var mainCamera = Camera.main;
            DynamicCameraController cameraController = null;
            if (mainCamera != null)
            {
                mainCamera.transform.position = new Vector3(0, 1.2f, 2f);
                mainCamera.transform.rotation = Quaternion.Euler(0, 180, 0);
                mainCamera.backgroundColor = new Color(0.2f, 0.2f, 0.25f);

                // DynamicCameraControllerを追加
                cameraController = mainCamera.gameObject.AddComponent<DynamicCameraController>();
                cameraController.enableFovControl = true;
                cameraController.minDistance = 1.5f;
                cameraController.maxDistance = 5.0f;
                cameraController.minFov = 30f;
                cameraController.maxFov = 60f;
                cameraController.fovSmoothSpeed = 5f;
                cameraController.enableLookAt = false;
                cameraController.lookAtRotationSpeed = 2f;
                cameraController.lookAtDelay = 0.2f;
                // targetCharacterは後で設定
            }

            // 床を作成
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(2, 1, 2);

            // マテリアル設定
            var floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = new Color(0.3f, 0.3f, 0.35f);
                floorRenderer.material = material;
            }

            // ライトの調整
            var directionalLight = GameObject.Find("Directional Light");
            if (directionalLight != null)
            {
                directionalLight.transform.rotation = Quaternion.Euler(50, -30, 0);
                var light = directionalLight.GetComponent<Light>();
                if (light != null)
                {
                    light.intensity = 1.2f;
                }
            }

            // キャラクターのルートオブジェクト作成
            var characterRoot = new GameObject("Character");
            characterRoot.transform.position = Vector3.zero;
            characterRoot.transform.rotation = Quaternion.identity;

            // VrmLoaderを追加
            var vrmLoader = characterRoot.AddComponent<VrmLoader>();
            vrmLoader.vrmFolderName = "VRM";
            vrmLoader.showMeshesOnLoad = true;

            // サブコントローラーを追加
            var animController = characterRoot.AddComponent<CharacterAnimationController>();
            var expressionController = characterRoot.AddComponent<CharacterExpressionController>();
            var lookAtController = characterRoot.AddComponent<CharacterLookAtController>();

            // テストコンポーネントを追加
            var characterSetup = characterRoot.AddComponent<CharacterSetup>();
            characterSetup.vrmFileName = "chr001_w001_model.vrm";
            characterSetup.vrmLoader = vrmLoader;
            characterSetup.animationController = animController;
            characterSetup.expressionController = expressionController;
            characterSetup.lookAtController = lookAtController;
            characterSetup.lookAtCameraOnLoad = true;

            // CameraControllerにキャラクターを設定
            if (cameraController != null)
            {
                cameraController.targetCharacter = characterRoot.transform;
            }

            // EventSystem作成（新Input System対応）
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            // デバッグUI作成
            var debugUI = CreateDebugUI(animController, expressionController);

            // 選択状態にする
            Selection.activeGameObject = characterRoot;

            // シーンを保存
            string scenePath = "Assets/Scenes/VrmTestScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log($"[VrmTestSceneSetup] Test scene created and saved to: {scenePath}");
            Debug.Log("[VrmTestSceneSetup] Press Play to test VRM loading!");
            Debug.Log("[VrmTestSceneSetup] NOTE: Assign TimelineBindingData to VrmLoader for animation playback.");
            Debug.Log("[VrmTestSceneSetup] Create it via: CyanNook > Animation > Create Timelines for Character");
        }

        [MenuItem("CyanNook/Add VRM Test Components to Selected")]
        public static void AddTestComponentsToSelected()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a GameObject first.", "OK");
                return;
            }

            // コンポーネントを追加（なければ）
            var vrmLoader = selected.GetComponent<VrmLoader>();
            if (vrmLoader == null)
            {
                vrmLoader = selected.AddComponent<VrmLoader>();
            }

            var animController = selected.GetComponent<CharacterAnimationController>();
            if (animController == null)
            {
                animController = selected.AddComponent<CharacterAnimationController>();
            }

            var expressionController = selected.GetComponent<CharacterExpressionController>();
            if (expressionController == null)
            {
                expressionController = selected.AddComponent<CharacterExpressionController>();
            }

            var lookAtController = selected.GetComponent<CharacterLookAtController>();
            if (lookAtController == null)
            {
                lookAtController = selected.AddComponent<CharacterLookAtController>();
            }

            var characterSetup = selected.GetComponent<CharacterSetup>();
            if (characterSetup == null)
            {
                characterSetup = selected.AddComponent<CharacterSetup>();
            }

            // 参照を設定
            characterSetup.vrmLoader = vrmLoader;
            characterSetup.animationController = animController;
            characterSetup.expressionController = expressionController;
            characterSetup.lookAtController = lookAtController;

            Debug.Log($"[VrmTestSceneSetup] Test components added to: {selected.name}");
        }

        /// <summary>
        /// デバッグUIを作成
        /// </summary>
        private static GameObject CreateDebugUI(
            CharacterAnimationController animController,
            CharacterExpressionController expressionController)
        {
            // フォントをロード
            var fontUI = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath_UI);
            var fontCode = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath_Code);

            if (fontUI == null)
            {
                Debug.LogWarning($"[VrmTestSceneSetup] UI font not found: {FontPath_UI}");
            }
            if (fontCode == null)
            {
                Debug.LogWarning($"[VrmTestSceneSetup] Code font not found: {FontPath_Code}");
            }

            // Canvas
            var canvasObj = new GameObject("UICanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasObj.AddComponent<GraphicRaycaster>();

            // UIController
            var uiController = canvasObj.AddComponent<UIController>();
            uiController.animationController = animController;
            uiController.expressionController = expressionController;
            uiController.chatFont = fontUI;
            uiController.codeFont = fontCode;

            // 入力フィールド（画面下部）
            var inputFieldObj = CreateInputField(canvasObj.transform, "ChatInputField", fontUI);
            var inputRect = inputFieldObj.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0, 0.15f);
            inputRect.anchorMax = new Vector2(1, 0.3f);
            inputRect.offsetMin = new Vector2(10, 5);
            inputRect.offsetMax = new Vector2(-10, -5);
            uiController.chatInputField = inputFieldObj.GetComponent<TMP_InputField>();

            // メッセージ表示（画面最下部）
            var messagePanel = CreatePanel(canvasObj.transform, "MessagePanel");
            var msgPanelRect = messagePanel.GetComponent<RectTransform>();
            msgPanelRect.anchorMin = new Vector2(0, 0);
            msgPanelRect.anchorMax = new Vector2(1, 0.15f);
            msgPanelRect.offsetMin = new Vector2(10, 10);
            msgPanelRect.offsetMax = new Vector2(-10, -10);
            var msgPanelImage = messagePanel.GetComponent<Image>();
            msgPanelImage.color = new Color(0, 0, 0, 0.7f);

            var messageText = CreateText(messagePanel.transform, "MessageText", fontUI);
            var msgTextRect = messageText.GetComponent<RectTransform>();
            msgTextRect.anchorMin = Vector2.zero;
            msgTextRect.anchorMax = Vector2.one;
            msgTextRect.offsetMin = new Vector2(10, 5);
            msgTextRect.offsetMax = new Vector2(-10, -5);
            var tmpText = messageText.GetComponent<TMP_Text>();
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.fontSize = 24;
            uiController.messageText = tmpText;

            return canvasObj;
        }

        private static GameObject CreatePanel(Transform parent, string name)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);

            var rect = panel.AddComponent<RectTransform>();
            var image = panel.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            return panel;
        }

        private static GameObject CreateInputField(Transform parent, string name, TMP_FontAsset font = null)
        {
            // TMP_InputFieldをプログラムで作成
            var inputFieldObj = new GameObject(name);
            inputFieldObj.transform.SetParent(parent, false);

            var rect = inputFieldObj.AddComponent<RectTransform>();
            var image = inputFieldObj.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            var inputField = inputFieldObj.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;

            // TextArea
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(inputFieldObj.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(5, 5);
            textAreaRect.offsetMax = new Vector2(-5, -5);
            textArea.AddComponent<RectMask2D>();

            // Placeholder
            var placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);
            var phRect = placeholder.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = Vector2.zero;
            phRect.offsetMax = Vector2.zero;
            var phText = placeholder.AddComponent<TextMeshProUGUI>();
            phText.text = "Enter JSON here...";
            phText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            phText.fontSize = 14;
            if (font != null) phText.font = font;
            inputField.placeholder = phText;

            // Text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(textArea.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var tmpText = textObj.AddComponent<TextMeshProUGUI>();
            tmpText.color = Color.white;
            tmpText.fontSize = 14;
            if (font != null) tmpText.font = font;
            inputField.textComponent = tmpText;
            inputField.textViewport = textAreaRect;

            return inputFieldObj;
        }

        private static GameObject CreateButton(Transform parent, string name, string label, TMP_FontAsset font = null)
        {
            var buttonObj = new GameObject(name);
            buttonObj.transform.SetParent(parent, false);

            var rect = buttonObj.AddComponent<RectTransform>();
            var image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.4f, 0.8f, 1f);

            var button = buttonObj.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.3f, 0.5f, 0.9f, 1f);
            colors.pressedColor = new Color(0.15f, 0.3f, 0.6f, 1f);
            button.colors = colors;

            // Text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 18;
            if (font != null) text.font = font;

            return buttonObj;
        }

        private static GameObject CreateText(Transform parent, string name, TMP_FontAsset font = null)
        {
            var textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);

            var rect = textObj.AddComponent<RectTransform>();
            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.color = Color.white;
            text.fontSize = 18;
            if (font != null) text.font = font;

            return textObj;
        }
    }
}
