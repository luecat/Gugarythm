using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public sealed class GugarhythmStartupSplash : MonoBehaviour
    {
        public const float DefaultDisplaySeconds = 1.5f;

        [SerializeField] Sprite splash;
        [SerializeField] float displaySeconds = DefaultDisplaySeconds;
        bool transitioning;
        Canvas splashCanvas;

        public void Configure(Sprite splashSprite, float seconds)
        {
            splash = splashSprite;
            displaySeconds = NormalizeDisplaySeconds(seconds);
        }

        void Awake()
        {
            LandscapeOrientation.Lock();
            if (Screen.width > 0 && Screen.height > Screen.width)
                Screen.orientation = ScreenOrientation.LandscapeLeft;

            // Keep the first painted frames black until landscape metrics are stable,
            // so players never see a pre-stretch portrait/wrong-aspect splash.
            Camera.main?.gameObject.SetActive(false);
            splashCanvas = BuildSplashCanvas(visible: false);
        }

        IEnumerator Start()
        {
            yield return WaitForStableLandscapePresentation();
            if (splashCanvas != null)
            {
                splashCanvas.enabled = true;
                Canvas.ForceUpdateCanvases();
            }

            yield return ShowThenOpenLibrary();
        }

        Canvas BuildSplashCanvas(bool visible)
        {
            var canvasObject = new GameObject("Startup Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.enabled = visible;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = .5f;

            var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var background = backgroundObject.GetComponent<Image>();
            background.color = Color.black;
            background.raycastTarget = false;

            var imageObject = new GameObject("GUGARHYTHM", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(AspectRatioFitter));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.5f, .5f);
            rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = Vector2.zero;
            var image = imageObject.GetComponent<Image>();
            image.sprite = splash;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var aspect = imageObject.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = splash != null
                ? splash.rect.width / Mathf.Max(.01f, splash.rect.height)
                : 16f / 9f;
            return canvas;
        }

        IEnumerator WaitForStableLandscapePresentation()
        {
            LandscapeOrientation.Lock();
            var deadline = Time.realtimeSinceStartup + 2f;
            var lastWidth = -1;
            var lastHeight = -1;
            var stableFrames = 0;

            while (Time.realtimeSinceStartup < deadline)
            {
                LandscapeOrientation.Lock();
                var width = Screen.width;
                var height = Screen.height;
                if (width > 0 && height > 0 && width >= height)
                {
                    if (width == lastWidth && height == lastHeight) stableFrames++;
                    else stableFrames = 0;
                    lastWidth = width;
                    lastHeight = height;
                    if (stableFrames >= 2) yield break;
                }
                else
                {
                    if (height > width)
                        Screen.orientation = ScreenOrientation.LandscapeLeft;
                    stableFrames = 0;
                    lastWidth = width;
                    lastHeight = height;
                }

                yield return null;
            }
        }

        IEnumerator ShowThenOpenLibrary()
        {
            var displayStartedAt = Time.realtimeSinceStartup;
            yield return BundledChartLibraryImporter.ImportAll();
            var remainingSeconds = displaySeconds - (Time.realtimeSinceStartup - displayStartedAt);
            if (remainingSeconds > 0f) yield return new WaitForSecondsRealtime(remainingSeconds);
            if (transitioning) yield break;
            transitioning = true;
            GugarhythmSceneRouter.OpenLibrary();
        }

        static float NormalizeDisplaySeconds(float seconds)
        {
            return float.IsFinite(seconds) ? Mathf.Max(0f, seconds) : DefaultDisplaySeconds;
        }
    }
}
