using System;
using System.Collections.Generic;
using System.Linq;
using Haven.Camp.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Haven.Camp.Editor
{
    public static class LoadingSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Loading.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string BackgroundPath = "Assets/Art/淘宝ui素材/RuntimeSprites/Auth/UI_Auth_TitleStart_Background.png";
        private const string ContrastPath = "Assets/Art/淘宝ui素材/RuntimeSprites/Auth/UI_Auth_TitleStart_Contrast.png";
        private const string FontPath = "Assets/HavenCamp/Fonts/HavenChinese.asset";
        private const string LatinFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [InitializeOnLoadMethod]
        private static void EnsureLoadingSceneExists()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                    return;
                CreateOrRefresh();
            };
        }

        [MenuItem("Haven/Loading Scene/Create or Refresh")]
        public static void CreateOrRefresh()
        {
            var background = LoadRequired<Sprite>(BackgroundPath);
            var contrast = LoadRequired<Sprite>(ContrastPath);
            var font = LoadRequired<TMP_FontAsset>(FontPath);
            var latinFont = LoadRequired<TMP_FontAsset>(LatinFontPath);

            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var canvas = CreateCanvas();
                CreateFullScreenImage(canvas.transform, "Forest Background", background, Color.white);
                CreateFullScreenImage(canvas.transform, "Contrast Overlay", contrast, new Color(1f, 1f, 1f, 0.78f));

                CreateText(canvas.transform, "Game Title", "PROJECT HAVEN", latinFont, 52f, FontStyles.Bold,
                    new Color(0.93f, 0.94f, 0.82f), new Vector2(0f, 245f), new Vector2(760f, 90f));
                CreateText(canvas.transform, "Loading Title", "正在前往避难所", font, 34f, FontStyles.Bold,
                    new Color(0.72f, 0.95f, 0.59f), new Vector2(0f, 155f), new Vector2(760f, 70f));
                CreateText(canvas.transform, "Loading Hint", "世界正在生成，请稍候", font, 18f, FontStyles.Normal,
                    new Color(0.76f, 0.82f, 0.75f), new Vector2(0f, 105f), new Vector2(700f, 44f));

                var track = CreateImage(canvas.transform, "Progress Track", new Color(0.015f, 0.025f, 0.04f, 0.9f),
                    new Vector2(0f, -160f), new Vector2(720f, 24f));
                var fill = CreateImage(track.transform, "Progress Fill", new Color(0.55f, 0.9f, 0.38f, 1f), Vector2.zero, Vector2.zero);
                Stretch(fill.rectTransform, new Vector2(3f, 3f), new Vector2(-3f, -3f));
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;
                fill.fillAmount = 0f;

                var status = CreateText(canvas.transform, "Loading Status", "正在准备…", font, 18f, FontStyles.Normal,
                    new Color(0.88f, 0.91f, 0.82f), new Vector2(0f, -215f), new Vector2(760f, 48f));
                var percent = CreateText(canvas.transform, "Loading Percent", "0%", latinFont, 18f, FontStyles.Bold,
                    new Color(0.72f, 0.95f, 0.59f), new Vector2(0f, -260f), new Vector2(220f, 42f));

                var controller = canvas.gameObject.AddComponent<LoadingSceneController>();
                controller.Configure(fill, status, percent);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException($"Could not save {ScenePath}.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(scene, true);
            }

            RegisterBuildScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Haven] Loading scene is ready: {ScenePath}");
        }

        [MenuItem("Haven/Loading Scene/Validate")]
        public static void Validate()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("Loading scene does not exist.");
            var scenes = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
            if (!scenes.Contains(ScenePath) || Array.IndexOf(scenes, ScenePath) <= Array.IndexOf(scenes, MainMenuScenePath))
                throw new InvalidOperationException("Loading scene must be enabled after MainMenu in Build Settings.");
            Debug.Log("[Haven] Loading scene validation passed.");
        }

        public static void SetupLoadingSceneBatch()
        {
            CreateOrRefresh();
            Validate();
        }

        private static Canvas CreateCanvas()
        {
            var root = new GameObject("Loading Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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
            Stretch(image.rectTransform, Vector2.zero, Vector2.zero);
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }

        private static Image CreateImage(Transform parent, string name, Color color, Vector2 position, Vector2 size)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string value, TMP_FontAsset font,
            float size, FontStyles style, Color color, Vector2 position, Vector2 dimensions)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = dimensions;
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static void RegisterBuildScene()
        {
            var priority = new[] { MainMenuScenePath, ScenePath };
            var remaining = EditorBuildSettings.scenes.Where(item => !priority.Contains(item.path)).ToList();
            var scenes = new List<EditorBuildSettingsScene>();
            scenes.AddRange(priority.Select(path => new EditorBuildSettingsScene(path, true)));
            scenes.AddRange(remaining);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset)
                throw new InvalidOperationException($"Required loading scene asset is missing: {path}");
            return asset;
        }

        private static void Stretch(RectTransform rect, Vector2 insetMin, Vector2 insetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = insetMin;
            rect.offsetMax = insetMax;
        }
    }
}
