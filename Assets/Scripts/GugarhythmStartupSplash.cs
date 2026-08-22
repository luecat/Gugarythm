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

        public void Configure(Sprite splashSprite, float seconds)
        {
            splash = splashSprite;
            displaySeconds = NormalizeDisplaySeconds(seconds);
        }

        void Awake()
        {
            LandscapeOrientation.Lock();
            var canvasObject = new GameObject("Startup Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            var imageObject = new GameObject("GUGARHYTHM", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = imageObject.GetComponent<Image>();
            image.sprite = splash;
            image.color = Color.white;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }

        void Start()
        {
            StartCoroutine(ShowThenOpenLibrary());
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
