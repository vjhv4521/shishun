using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Haven.Camp.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Haven.Camp.Editor
{
    public static class MainMenuSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string LoadingScenePath = "Assets/Scenes/Loading.unity";
        private const string SinglePlayerScenePath = "Assets/Scenes/WorldGenMap.unity";
        private const string MultiplayerScenePath = "Assets/Scenes/FrameworkDemo.unity";
        private const string BackgroundPath = "Assets/Art/淘宝ui素材/RuntimeSprites/Auth/UI_Auth_TitleStart_Background.png";
        private const string ContrastPath = "Assets/Art/淘宝ui素材/RuntimeSprites/Auth/UI_Auth_TitleStart_Contrast.png";
        private const string ButtonPath = "Assets/Art/淘宝ui素材/RuntimeSprites/Auth/UI_Auth_TitleStart_ButtonStart_Bg_Background.png";
        private const string FontPath = "Assets/HavenCamp/Fonts/HavenChinese.asset";
        private const string LatinFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Haven/Main Menu/1. Create or Refresh")]
        public static void CreateOrRefresh()
        {
            var background = LoadRequired<Sprite>(BackgroundPath);
            var contrast = LoadRequired<Sprite>(ContrastPath);
            var buttonSprite = LoadRequired<Sprite>(ButtonPath);
            var font = LoadRequired<TMP_FontAsset>(FontPath);
            var latinFont = LoadRequired<TMP_FontAsset>(LatinFontPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvas = CreateCanvas();
            CreateFullScreenImage(canvas.transform, "Forest Background", background, Color.white);
            CreateFullScreenImage(canvas.transform, "Contrast Overlay", contrast, new Color(1f, 1f, 1f, 0.72f));

            CreateText(canvas.transform, "Game Title", "PROJECT HAVEN", latinFont, 66f, FontStyles.Bold,
                new Color(0.93f, 0.94f, 0.82f), new Vector2(-420f, 345f), new Vector2(720f, 140f));
            CreateText(canvas.transform, "Chinese Title", "避难所", font, 38f, FontStyles.Normal,
                new Color(0.67f, 0.94f, 0.56f), new Vector2(-420f, 260f), new Vector2(520f, 70f));
            CreateText(canvas.transform, "Tagline", "生存  ·  建造  ·  探索", font, 19f, FontStyles.Normal,
                new Color(0.76f, 0.81f, 0.75f), new Vector2(-420f, 215f), new Vector2(520f, 44f));

            var mainPanel = CreatePanel(canvas.transform, "Main Panel", new Vector2(-420f, -35f), new Vector2(520f, 330f));
            var startButton = CreateButton(mainPanel.transform, "Start Game Button", "开始游戏", font, buttonSprite, new Vector2(0f, 55f));
            var quitButton = CreateButton(mainPanel.transform, "Quit Game Button", "退出游戏", font, buttonSprite, new Vector2(0f, -45f));

            var modePanel = CreatePanel(canvas.transform, "Mode Panel", new Vector2(-420f, -40f), new Vector2(520f, 420f));
            CreateSectionTitle(modePanel.transform, "选择游戏模式", font, new Vector2(0f, 145f));
            var singleButton = CreateButton(modePanel.transform, "Single Player Button", "单人模式", font, buttonSprite, new Vector2(0f, 55f));
            var multiplayerButton = CreateButton(modePanel.transform, "Multiplayer Button", "联机模式", font, buttonSprite, new Vector2(0f, -45f));
            var modeBackButton = CreateSmallButton(modePanel.transform, "Mode Back Button", "返 回", font, buttonSprite, new Vector2(0f, -135f));

            var multiplayerPanel = CreatePanel(canvas.transform, "Multiplayer Panel", new Vector2(-420f, -35f), new Vector2(560f, 680f));
            CreateSectionTitle(multiplayerPanel.transform, "联机模式", font, new Vector2(0f, 140f));
            var createRoomButton = CreateButton(multiplayerPanel.transform, "Create Room Button", "创建房间", font, buttonSprite, new Vector2(0f, 50f));
            var joinRoomButton = CreateButton(multiplayerPanel.transform, "Join Room Button", "加入房间", font, buttonSprite, new Vector2(0f, -50f));
            CreateText(multiplayerPanel.transform, "Address Label", "服务器地址", font, 18f, FontStyles.Normal,
                new Color(0.78f, 0.83f, 0.76f), new Vector2(0f, -125f), new Vector2(430f, 36f));
            var addressInput = CreateInputField(multiplayerPanel.transform, font, new Vector2(0f, -175f));
            CreateText(multiplayerPanel.transform, "Lan Notice", "仅支持局域网 IP 直连 · 不提供大厅或房间列表", font, 15f, FontStyles.Normal,
                new Color(0.68f, 0.74f, 0.68f), new Vector2(0f, -230f), new Vector2(500f, 36f));
            var multiplayerBackButton = CreateSmallButton(multiplayerPanel.transform, "Multiplayer Back Button", "返 回", font, buttonSprite, new Vector2(0f, -310f));

            var status = CreateText(canvas.transform, "Status", string.Empty, font, 18f, FontStyles.Normal,
                new Color(0.82f, 0.91f, 0.82f), new Vector2(-420f, -420f), new Vector2(620f, 70f));
            var version = CreateText(canvas.transform, "Version", $"VERSION {Application.version}", font, 14f, FontStyles.Normal,
                new Color(0.62f, 0.67f, 0.62f), new Vector2(24f, 18f), new Vector2(260f, 34f));
            ConfigureBottomLeft(version.rectTransform);
            var escapeHint = CreateText(canvas.transform, "Escape Hint", "ESC  返回", font, 14f, FontStyles.Normal,
                new Color(0.62f, 0.67f, 0.62f), new Vector2(-24f, 18f), new Vector2(180f, 34f));
            ConfigureBottomRight(escapeHint.rectTransform);

            mainPanel.SetActive(true);
            modePanel.SetActive(false);
            multiplayerPanel.SetActive(false);
            status.gameObject.SetActive(false);

            var controllerObject = new GameObject("Main Menu Controller");
            var controller = controllerObject.AddComponent<MainMenuController>();
            ConfigureController(controller, mainPanel, modePanel, multiplayerPanel, startButton, quitButton,
                singleButton, multiplayerButton, createRoomButton, joinRoomButton, modeBackButton,
                multiplayerBackButton, addressInput, status);

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetAsLastSibling();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save {ScenePath}.");

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(LoadingScenePath))
                LoadingSceneBuilder.CreateOrRefresh();
            RegisterBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Haven] Main menu is ready: {ScenePath}");
        }

        [MenuItem("Haven/Main Menu/Validate")]
        public static void Validate()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (!scene)
                throw new InvalidOperationException("Main menu scene does not exist. Run Haven/Main Menu/1. Create or Refresh.");

            var paths = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
            if (paths.Length < 4 || paths[0] != ScenePath || paths[1] != LoadingScenePath ||
                !paths.Contains(SinglePlayerScenePath) || !paths.Contains(MultiplayerScenePath))
                throw new InvalidOperationException("Build Settings must start with MainMenu and Loading, then include WorldGenMap and FrameworkDemo.");

            var opened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (!opened.IsValid() || !controller || !canvas || !UnityEngine.Object.FindAnyObjectByType<EventSystem>())
                throw new InvalidOperationException("Main menu is missing its controller, canvas, or event system.");

            Debug.Log("[Haven] Main menu validation passed.");
        }

        public static void SetupMainMenuBatch()
        {
            CreateOrRefresh();
            Validate();
        }

        [MenuItem("Haven/Main Menu/Capture Preview")]
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (!canvas)
                throw new InvalidOperationException("Main menu canvas was not found.");

            var cameraObject = new GameObject("Preview Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;

            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            var previousTarget = RenderTexture.active;
            var pageTransforms = canvas.GetComponentsInChildren<Transform>(true);
            var mainPanel = pageTransforms.First(item => item.name == "Main Panel").gameObject;
            var modePanel = pageTransforms.First(item => item.name == "Mode Panel").gameObject;
            var multiplayerPanel = pageTransforms.First(item => item.name == "Multiplayer Panel").gameObject;
            var mainWasActive = mainPanel.activeSelf;
            var modeWasActive = modePanel.activeSelf;
            var multiplayerWasActive = multiplayerPanel.activeSelf;
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                mainPanel.SetActive(true);
                modePanel.SetActive(false);
                multiplayerPanel.SetActive(false);
                SavePreview(camera, target, "Logs/MainMenuPreview.png");

                mainPanel.SetActive(false);
                multiplayerPanel.SetActive(true);
                SavePreview(camera, target, "Logs/MainMenuMultiplayerPreview.png");
            }
            finally
            {
                mainPanel.SetActive(mainWasActive);
                modePanel.SetActive(modeWasActive);
                multiplayerPanel.SetActive(multiplayerWasActive);
                RenderTexture.active = previousTarget;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        public static void CapturePreviewBatch()
        {
            CapturePreview();
        }

        private static void SavePreview(Camera camera, RenderTexture target, string relativePath)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            texture.Apply();

            var output = Path.GetFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? "Logs");
            File.WriteAllBytes(output, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log($"[Haven] Main menu preview saved: {output}");
        }

        private static Canvas CreateCanvas()
        {
            var root = new GameObject("Main Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void CreateFullScreenImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            Stretch(image.rectTransform);
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return panel;
        }

        private static void CreateSectionTitle(Transform parent, string value, TMP_FontAsset font, Vector2 position)
        {
            CreateText(parent, "Section Title", value, font, 27f, FontStyles.Bold,
                new Color(0.91f, 0.93f, 0.82f), position, new Vector2(460f, 52f));
        }

        private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font, Sprite sprite, Vector2 position)
        {
            return CreateButtonInternal(parent, name, label, font, sprite, position, new Vector2(440f, 72f), 25f);
        }

        private static Button CreateSmallButton(Transform parent, string name, string label, TMP_FontAsset font, Sprite sprite, Vector2 position)
        {
            return CreateButtonInternal(parent, name, label, font, sprite, position, new Vector2(260f, 56f), 19f);
        }

        private static Button CreateButtonInternal(Transform parent, string name, string label, TMP_FontAsset font, Sprite sprite,
            Vector2 position, Vector2 size, float fontSize)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.sprite = sprite;
            image.type = Image.Type.Simple;

            var button = image.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.95f);
            colors.highlightedColor = new Color(0.72f, 1f, 0.62f, 1f);
            colors.pressedColor = new Color(0.46f, 0.76f, 0.39f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            var text = CreateText(image.transform, "Label", label, font, fontSize, FontStyles.Bold,
                new Color(0.94f, 0.94f, 0.86f), Vector2.zero, size - new Vector2(30f, 12f));
            text.raycastTarget = false;
            return button;
        }

        private static TMP_InputField CreateInputField(Transform parent, TMP_FontAsset font, Vector2 position)
        {
            var background = new GameObject("Server Address Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            background.transform.SetParent(parent, false);
            var rect = background.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(440f, 58f);
            var image = background.GetComponent<Image>();
            image.color = new Color(0.015f, 0.025f, 0.04f, 0.94f);

            var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(background.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(20f, 8f);
            viewportRect.offsetMax = new Vector2(-20f, -8f);

            var placeholder = CreateText(viewport.transform, "Placeholder", "例如：192.168.1.10:7770", font, 18f, FontStyles.Italic,
                new Color(0.5f, 0.56f, 0.5f, 0.85f), Vector2.zero, Vector2.zero);
            Stretch(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;

            var value = CreateText(viewport.transform, "Text", string.Empty, font, 19f, FontStyles.Normal,
                new Color(0.9f, 0.94f, 0.86f), Vector2.zero, Vector2.zero);
            Stretch(value.rectTransform);
            value.alignment = TextAlignmentOptions.MidlineLeft;

            var input = background.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = value;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 64;
            input.caretColor = new Color(0.7f, 1f, 0.58f);
            input.selectionColor = new Color(0.35f, 0.65f, 0.32f, 0.55f);
            return input;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string value, TMP_FontAsset font, float size,
            FontStyles style, Color color, Vector2 position, Vector2 dimensions)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void ConfigureController(MainMenuController controller, GameObject mainPanel, GameObject modePanel,
            GameObject multiplayerPanel, Button startButton, Button quitButton, Button singleButton, Button multiplayerButton,
            Button createButton, Button joinButton, Button modeBackButton, Button multiplayerBackButton,
            TMP_InputField addressInput, TMP_Text status)
        {
            var serialized = new SerializedObject(controller);
            Set(serialized, "mainPanel", mainPanel);
            Set(serialized, "modePanel", modePanel);
            Set(serialized, "multiplayerPanel", multiplayerPanel);
            Set(serialized, "startButton", startButton);
            Set(serialized, "quitButton", quitButton);
            Set(serialized, "singlePlayerButton", singleButton);
            Set(serialized, "multiplayerButton", multiplayerButton);
            Set(serialized, "createRoomButton", createButton);
            Set(serialized, "joinRoomButton", joinButton);
            Set(serialized, "modeBackButton", modeBackButton);
            Set(serialized, "multiplayerBackButton", multiplayerBackButton);
            Set(serialized, "addressInput", addressInput);
            Set(serialized, "statusLabel", status);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            serialized.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static void RegisterBuildScenes()
        {
            var priority = new[] { ScenePath, LoadingScenePath, SinglePlayerScenePath, MultiplayerScenePath };
            var remaining = EditorBuildSettings.scenes
                .Where(item => !priority.Contains(item.path))
                .ToList();
            var scenes = new List<EditorBuildSettingsScene>();
            scenes.AddRange(priority.Select(path => new EditorBuildSettingsScene(path, true)));
            scenes.AddRange(remaining);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset)
                throw new InvalidOperationException($"Required main menu asset is missing: {path}");
            return asset;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ConfigureBottomLeft(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(24f, 18f);
        }

        private static void ConfigureBottomRight(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-24f, 18f);
        }
    }
}
