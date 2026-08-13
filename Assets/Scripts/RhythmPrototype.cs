using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gugarythm
{
    public sealed class RhythmPrototype : MonoBehaviour
    {
        private const int LaneCount = 4;
        private const float ApproachSeconds = 1.8f;
        private const float PerfectWindow = .055f;
        private const float GreatWindow = .11f;

        private readonly List<RuntimeNote> notes = new();
        private readonly Color[] laneColors =
        {
            new Color(.28f, .80f, 1f), new Color(.35f, 1f, .69f),
            new Color(1f, .78f, .25f), new Color(1f, .38f, .72f),
        };

        private RectTransform stage;
        private Text scoreLabel;
        private Text comboLabel;
        private Text judgmentLabel;
        private float songTime;
        private int score;
        private int combo;
        private bool playing;

        private void Awake()
        {
            Application.targetFrameRate = 120;
            BuildInterface();
            SeedDemoChart();
        }

        private void Update()
        {
            if (!playing) return;
            songTime += Time.unscaledDeltaTime;
            UpdateNotes();
            ReadInput();
        }

        private void ReadInput()
        {
            if (Input.GetMouseButtonDown(0)) Tap(ScreenPointToLane(Input.mousePosition));
            for (var i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began) Tap(ScreenPointToLane(touch.position));
            }
        }

        private int ScreenPointToLane(Vector2 point)
        {
            var normalized = Mathf.Clamp01(point.x / Screen.width);
            return Mathf.Clamp(Mathf.FloorToInt(normalized * LaneCount), 0, LaneCount - 1);
        }

        private void Tap(int lane)
        {
            RuntimeNote candidate = null;
            var bestDelta = float.MaxValue;
            foreach (var note in notes)
            {
                if (note.resolved || note.lane != lane) continue;
                var delta = Mathf.Abs(note.time - songTime);
                if (delta < bestDelta) { bestDelta = delta; candidate = note; }
            }

            if (candidate == null || bestDelta > GreatWindow)
            {
                combo = 0;
                ShowJudgment("MISS", new Color(1f, .35f, .55f));
                return;
            }

            candidate.resolved = true;
            candidate.view.gameObject.SetActive(false);
            combo++;
            score += bestDelta <= PerfectWindow ? 1000 : 650;
            ShowJudgment(bestDelta <= PerfectWindow ? "PERFECT" : "GREAT", bestDelta <= PerfectWindow ? Color.cyan : new Color(1f, .85f, .3f));
            RefreshHud();
        }

        private void UpdateNotes()
        {
            foreach (var note in notes)
            {
                if (note.resolved) continue;
                var phase = (note.time - songTime) / ApproachSeconds;
                note.view.anchoredPosition = new Vector2(note.lane * 270f - 405f, Mathf.Lerp(-600f, 700f, phase));
                if (songTime - note.time > GreatWindow)
                {
                    note.resolved = true;
                    note.view.gameObject.SetActive(false);
                    combo = 0;
                    ShowJudgment("MISS", new Color(1f, .35f, .55f));
                    RefreshHud();
                }
            }
        }

        private void SeedDemoChart()
        {
            var pattern = new[] { 0, 1, 2, 3, 2, 1, 0, 3, 1, 2, 0, 3 };
            for (var i = 0; i < 48; i++) AddNote(pattern[i % pattern.Length], 2f + i * .43f);
        }

        private void AddNote(int lane, float time)
        {
            var note = CreatePanel("Note", stage, laneColors[lane], new Vector2(244, 52), new Vector2(lane * 270f - 405f, 700));
            AddOutline(note.gameObject, Color.white, 4);
            var gloss = CreatePanel("Highlight", note, new Color(1, 1, 1, .35f), new Vector2(208, 7), new Vector2(0, 12));
            gloss.GetComponent<Image>().raycastTarget = false;
            notes.Add(new RuntimeNote { lane = lane, time = time, view = note });
        }

        private void BuildInterface()
        {
            var canvasObject = new GameObject("Rhythm Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 1;

            var root = canvasObject.GetComponent<RectTransform>();
            CreatePanel("Backdrop", root, new Color(.025f, .04f, .14f), Vector2.zero, Vector2.zero, stretch: true);
            BuildBackground(root);
            BuildHud(root);
            stage = CreatePanel("Four Lane Stage", root, new Color(.04f, .08f, .23f, .72f), new Vector2(930, 1380), new Vector2(0, -90));
            AddOutline(stage.gameObject, new Color(.52f, .28f, 1f, .85f), 6);
            BuildLanes(stage);
            BuildTouchTargets(root);
            BuildStartOverlay(root);
        }

        private void BuildBackground(RectTransform root)
        {
            for (var i = 0; i < 35; i++)
            {
                var height = 35 + (i * 53 % 180);
                var x = -500 + i * 30;
                CreatePanel("Equalizer", root, new Color(.05f, .55f, 1f, .2f), new Vector2(14, height), new Vector2(x, -400));
            }
            for (var i = 0; i < 6; i++)
            {
                var card = CreatePanel("Floating window", root, new Color(.18f, .15f, .46f, .36f), new Vector2(250, 165), new Vector2(-330 + (i % 3) * 330, 500 - (i / 3) * 820));
                AddOutline(card.gameObject, new Color(.85f, .28f, .85f, .38f), 3);
            }
        }

        private void BuildHud(RectTransform root)
        {
            var scorePanel = CreatePanel("Score panel", root, new Color(.12f, .15f, .36f, .9f), new Vector2(390, 150), new Vector2(-320, 790));
            AddOutline(scorePanel.gameObject, new Color(.45f, .72f, 1f, .55f), 3);
            scoreLabel = CreateText("SCORE\n0000000", scorePanel, 42, TextAnchor.MiddleLeft);
            scoreLabel.rectTransform.offsetMin = new Vector2(25, 12); scoreLabel.rectTransform.offsetMax = new Vector2(-15, -12);

            var lifePanel = CreatePanel("Life panel", root, new Color(.12f, .15f, .36f, .9f), new Vector2(390, 150), new Vector2(320, 790));
            AddOutline(lifePanel.gameObject, new Color(.45f, .72f, 1f, .55f), 3);
            var life = CreateText("LIFE  1000\n━━━━━━━━", lifePanel, 36, TextAnchor.MiddleCenter); life.color = new Color(.38f, 1f, .64f);
            life.rectTransform.offsetMin = Vector2.zero; life.rectTransform.offsetMax = Vector2.zero;

            var comboPanel = CreatePanel("Combo panel", root, new Color(.12f, .15f, .36f, .96f), new Vector2(170, 126), new Vector2(0, 520));
            AddOutline(comboPanel.gameObject, new Color(.9f, .4f, 1f, .9f), 4);
            comboLabel = CreateText("COMBO\n0", comboPanel, 32, TextAnchor.MiddleCenter); comboLabel.rectTransform.offsetMin = Vector2.zero; comboLabel.rectTransform.offsetMax = Vector2.zero;

            judgmentLabel = CreateText("", root, 58, TextAnchor.MiddleCenter);
            judgmentLabel.rectTransform.sizeDelta = new Vector2(650, 100); judgmentLabel.rectTransform.anchoredPosition = new Vector2(0, -645);
        }

        private void BuildLanes(RectTransform parent)
        {
            for (var i = 0; i < LaneCount; i++)
            {
                var lane = CreatePanel("Lane " + (i + 1), parent, new Color(.18f, .2f, .43f, .24f), new Vector2(258, 1360), new Vector2(i * 270f - 405f, 0));
                AddOutline(lane.gameObject, new Color(.68f, .72f, 1f, .25f), 2);
                var key = CreateText((i + 1).ToString(), lane, 30, TextAnchor.MiddleCenter); key.color = new Color(1, 1, 1, .58f);
                key.rectTransform.sizeDelta = new Vector2(80, 60); key.rectTransform.anchoredPosition = new Vector2(0, -600);
            }
            var line = CreatePanel("Judgment line", parent, new Color(.9f, .4f, 1f), new Vector2(900, 12), new Vector2(0, -595));
            AddOutline(line.gameObject, new Color(.75f, .8f, 1f), 3);
        }

        private void BuildTouchTargets(RectTransform root)
        {
            for (var i = 0; i < LaneCount; i++)
            {
                var pad = CreatePanel("Touch lane " + (i + 1), root, new Color(laneColors[i].r, laneColors[i].g, laneColors[i].b, .2f), new Vector2(250, 190), new Vector2(i * 270f - 405f, -790));
                AddOutline(pad.gameObject, laneColors[i], 4);
                var label = CreateText("TAP", pad, 26, TextAnchor.MiddleCenter); label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = Vector2.zero;
            }
        }

        private void BuildStartOverlay(RectTransform root)
        {
            var button = CreatePanel("Start", root, new Color(.12f, .72f, .82f, .96f), new Vector2(420, 120), new Vector2(0, -150));
            AddOutline(button.gameObject, Color.white, 4);
            var label = CreateText("開始試玩", button, 42, TextAnchor.MiddleCenter); label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = Vector2.zero;
            button.GetComponent<Image>().raycastTarget = true;
            var click = button.gameObject.AddComponent<Button>();
            click.onClick.AddListener(() => { button.gameObject.SetActive(false); playing = true; ShowJudgment("READY", Color.white); });
        }

        private void ShowJudgment(string text, Color color)
        {
            judgmentLabel.text = text; judgmentLabel.color = color;
        }

        private void RefreshHud()
        {
            scoreLabel.text = "SCORE\n" + score.ToString("0000000");
            comboLabel.text = "COMBO\n" + combo;
        }

        private static RectTransform CreatePanel(string name, RectTransform parent, Color color, Vector2 size, Vector2 position, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            if (stretch) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
            else { rect.sizeDelta = size; rect.anchoredPosition = position; }
            return rect;
        }

        private static void AddOutline(GameObject go, Color color, int width)
        {
            var outline = go.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(width, -width);
        }

        private static Text CreateText(string content, RectTransform parent, int size, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var text = go.GetComponent<Text>(); text.rectTransform.SetParent(parent, false); text.text = content; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.fontStyle = FontStyle.Bold; text.alignment = alignment; text.color = Color.white; text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private sealed class RuntimeNote { public int lane; public float time; public bool resolved; public RectTransform view; }
    }
}
