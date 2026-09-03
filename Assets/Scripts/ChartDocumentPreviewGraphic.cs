using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // A static, read-only document view. It intentionally stays separate from
    // the gameplay render path, timing clock, judgment state, and audio state.
    public sealed class ChartDocumentPreviewGraphic : MaskableGraphic
    {
        public const float DocumentUnitsPerSecond = 150f;

        const float HalfLaneRange = 6f;
        const float HorizontalInset = 44f;
        const float TopInset = 34f;
        const float BottomInset = 34f;
        const double TimePaddingSeconds = .5d;
        const double MinorGridSeconds = .5d;
        const int MaxGridLines = 4096;
        const double BeatsPerMeasure = 4d;
        const double BeatsPerColumn = 16d;
        const float MeasureLabelWidth = 26f;
        const float ColumnTrackWidth = 118f;
        // Leave a real visual gutter between unfolded pages.  The former 36px
        // gap let the outer rails and wide button caps read as one crowded row.
        const float ColumnGap = 60f;
        const float PreviewNoteCapRatio = 93f / 354f;
        const float PreviewTapHeight = 7f;
        const float PreviewFlickHeight = 8f;
        const float FlickArrowScale = .82f;
        // Each decoration batch emits four vertices per span.  Keep a broad
        // margin below Unity UI's 65k-vertex limit per Graphic.
        const int MaxPreviewGuideSegmentsPerBatch = 8000;
        static readonly float[] FlickLogicalSizes = { 44f, 66f, 96f, 128f, 159f, 190f };

        RuntimeChart chart;
        double firstTime;
        double lastTime = 1d;
        float chartWidth = 1f;
        double firstBeat;
        double lastBeat = 1d;
        double documentStartBeat;
        int columnCount = 1;
        readonly Dictionary<string, Texture2D> buttonTextures = new(StringComparer.Ordinal);
        readonly Dictionary<string, Texture2D> traceTextures = new(StringComparer.Ordinal);
        readonly Texture2D[] flickNormalCenterTextures = new Texture2D[6];
        readonly Texture2D[] flickNormalSideTextures = new Texture2D[6];
        readonly Texture2D[] flickCriticalCenterTextures = new Texture2D[6];
        readonly Texture2D[] flickCriticalSideTextures = new Texture2D[6];
        readonly List<PreviewNoteView> previewNoteViews = new();
        readonly List<PreviewRibbon> previewRibbonPool = new();
        readonly List<ChartPreviewGuideGraphic> previewGuideBatches = new();
        RectTransform previewArtworkRoot;
        RectTransform previewNoteRoot;
        HoldBatchGraphic previewGreenHoldBatch;
        HoldBatchGraphic previewYellowHoldBatch;
        Texture2D damageTexture;
        Texture2D holdGreenConnectorTexture;
        Texture2D holdYellowConnectorTexture;
        Texture2D holdMidMintTexture;
        Texture2D holdMidYellowTexture;
        Texture2D traceDiamondMintTexture;
        Texture2D traceDiamondPinkTexture;
        Texture2D traceDiamondYellowTexture;
        int activePreviewNoteViews;
        int activePreviewRibbons;
        int activePreviewGuideBatches;
        int activePreviewGuideSegments;
        bool previewArtworkLoaded;
        bool noteArtworkReady;
        bool holdArtworkReady;

        sealed class PreviewNoteView
        {
            public HorizontalSlicedRawImage Body;
            public RawImage Particle;
            public RawImage FlickArrow;
        }

        sealed class PreviewRibbon
        {
            public readonly List<Vector2> Centers = new(8);
            public readonly List<float> Widths = new(8);
            public bool Critical;
            public int ColumnIndex;

            public void Reset(bool critical, int columnIndex)
            {
                Critical = critical;
                ColumnIndex = columnIndex;
                Centers.Clear();
                Widths.Clear();
            }

            public void AddPoint(Vector2 center, float width)
            {
                if (Centers.Count > 0 && (Centers[Centers.Count - 1] - center).sqrMagnitude < .0001f)
                {
                    Widths[Widths.Count - 1] = width;
                    return;
                }
                Centers.Add(center);
                Widths.Add(Mathf.Max(.001f, width));
            }
        }

        public float ContentWidth => HorizontalInset * 2f +
            columnCount * (MeasureLabelWidth + ColumnTrackWidth) +
            Mathf.Max(0, columnCount - 1) * ColumnGap;

        public void SetChart(RuntimeChart value)
        {
            chart = value;
            FindTimeRange(value, out firstTime, out lastTime);
            chartWidth = ChartPreviewLayout.ContentWidth(
                firstTime - TimePaddingSeconds, lastTime + TimePaddingSeconds, DocumentUnitsPerSecond);
            FindBeatRange(value, out firstBeat, out lastBeat);
            documentStartBeat = Math.Floor(firstBeat / BeatsPerColumn) * BeatsPerColumn;
            var documentEndBeat = Math.Ceiling(lastBeat / BeatsPerColumn) * BeatsPerColumn;
            if (documentEndBeat - documentStartBeat < BeatsPerColumn)
                documentEndBeat = documentStartBeat + BeatsPerColumn;
            columnCount = Math.Max(1, (int)Math.Ceiling((documentEndBeat - documentStartBeat) / BeatsPerColumn));
            // The first Canvas rebuild happens before RefreshArtwork has a
            // settled ScrollRect size.  Establish child artwork ownership now
            // so that temporary rebuild cannot submit the entire chart to the
            // parent UI mesh and exceed its vertex limit.
            EnsurePreviewArtworkLayers();
            ClearPreviewArtwork();
            noteArtworkReady = false;
            holdArtworkReady = false;
            SetVerticesDirty();
        }

        public void ClearChart()
        {
            chart = null;
            firstTime = 0d;
            lastTime = 1d;
            chartWidth = 1f;
            firstBeat = 0d;
            lastBeat = 1d;
            documentStartBeat = 0d;
            columnCount = 1;
            ClearPreviewArtwork();
            noteArtworkReady = false;
            holdArtworkReady = false;
            SetVerticesDirty();
        }

        /// <summary>
        /// Builds the static texture layer after its ScrollRect has settled on
        /// its final size.  This keeps the document mesh responsible for the
        /// grid while real note art remains ordinary child UI graphics.
        /// </summary>
        public void RefreshArtwork()
        {
            var rect = rectTransform.rect;
            if (chart == null || rect.width <= 0f || rect.height <= 0f)
            {
                ClearPreviewArtwork();
                noteArtworkReady = false;
                holdArtworkReady = false;
                SetVerticesDirty();
                return;
            }

            EnsurePreviewArtworkLayers();
            EnsurePreviewArtworkLoaded();
            var bottom = rect.yMin + BottomInset;
            var top = rect.yMax - TopInset;

            BuildPreviewGuideArtwork(rect, bottom, top);

            noteArtworkReady = buttonTextures.Count > 0 || traceTextures.Count > 0 || damageTexture != null;
            if (noteArtworkReady) BuildPreviewNoteArtwork(rect, bottom, top);
            else DeactivatePreviewNotes();

            holdArtworkReady = holdGreenConnectorTexture != null && holdYellowConnectorTexture != null;
            if (holdArtworkReady) BuildPreviewHoldArtwork(rect, bottom, top);
            else ClearPreviewHoldArtwork();

            SetVerticesDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            // The preview lives inside a resizable ScrollRect.  Rebuild the
            // child artwork when the final document rect changes so centering
            // and the equal outer margins remain correct after a resize or
            // device rotation.
            if (chart != null && previewArtworkRoot != null && isActiveAndEnabled)
                RefreshArtwork();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            var rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            AddRect(vertices, rect.xMin, rect.yMin, rect.width, rect.height, new Color(.025f, .04f, .085f, .99f));
            var bottom = rect.yMin + BottomInset;
            var top = rect.yMax - TopInset;
            DrawColumnGrid(vertices, rect, bottom, top);
            if (chart == null) return;

            if (!holdArtworkReady && previewArtworkRoot == null)
            {
                foreach (var path in chart.HoldPaths) DrawColumnHoldPath(vertices, rect, bottom, top, path);
                foreach (var connector in chart.FallbackConnectors) DrawColumnConnector(vertices, rect, bottom, top, connector);
            }
            foreach (var simLine in chart.SimLines) DrawColumnSimLine(vertices, rect, bottom, top, simLine);
            if (!noteArtworkReady && previewArtworkRoot == null)
                foreach (var note in chart.Notes) DrawColumnNote(vertices, rect, bottom, top, note);
        }

        float ColumnsWidth =>
            columnCount * (MeasureLabelWidth + ColumnTrackWidth) +
            Mathf.Max(0, columnCount - 1) * ColumnGap;

        float ColumnsLeft(Rect rect) =>
            rect.xMin + Mathf.Max(HorizontalInset, (rect.width - ColumnsWidth) * .5f);

        float ColumnTrackLeft(Rect rect, int columnIndex) =>
            ColumnsLeft(rect) + columnIndex * (MeasureLabelWidth + ColumnTrackWidth + ColumnGap) + MeasureLabelWidth;

        void EnsurePreviewArtworkLayers()
        {
            if (previewArtworkRoot != null) return;
            previewArtworkRoot = CreatePreviewLayer("Chart Preview Artwork", rectTransform);
            previewGreenHoldBatch = CreatePreviewHoldBatch("Chart Preview Hold Green", previewArtworkRoot);
            previewYellowHoldBatch = CreatePreviewHoldBatch("Chart Preview Hold Yellow", previewArtworkRoot);
            previewNoteRoot = CreatePreviewLayer("Chart Preview Notes", previewArtworkRoot);
        }

        static RectTransform CreatePreviewLayer(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var layer = go.GetComponent<RectTransform>();
            layer.SetParent(parent, false);
            StretchToParent(layer);
            return layer;
        }

        static HoldBatchGraphic CreatePreviewHoldBatch(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HoldBatchGraphic));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            StretchToParent(rect);
            var batch = go.GetComponent<HoldBatchGraphic>();
            batch.raycastTarget = false;
            batch.color = new Color(1f, 1f, 1f, .62f);
            batch.sourceUvInset = GugarhythmLandscapePrototype.HoldConnectorSourceUvInset;
            return batch;
        }

        static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        void EnsurePreviewArtworkLoaded()
        {
            if (previewArtworkLoaded) return;
            previewArtworkLoaded = true;

            foreach (var name in new[] { "purple", "cyan", "mint", "white", "pink", "yellow" })
            {
                var texture = Resources.Load<Texture2D>("Gugarhythm/official/buttons/button-" + name) ??
                    Resources.Load<Texture2D>("Gugarhythm/buttons/button-" + name);
                if (texture != null) buttonTextures[name] = texture;
            }
            foreach (var name in new[] { "mint", "pink", "yellow" })
            {
                var texture = Resources.Load<Texture2D>("Gugarhythm/official/traces/trace-" + name) ??
                    Resources.Load<Texture2D>("Gugarhythm/traces/trace-" + name);
                if (texture != null) traceTextures[name] = texture;
            }
            damageTexture = Resources.Load<Texture2D>("Gugarhythm/official/damage/damage-purple") ??
                Resources.Load<Texture2D>("Gugarhythm/damage/damage-purple");
            for (var index = 0; index < FlickLogicalSizes.Length; index++)
            {
                var suffix = (index + 1).ToString();
                flickNormalCenterTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-normal-center-" + suffix);
                flickNormalSideTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-normal-side-" + suffix);
                flickCriticalCenterTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-critical-center-" + suffix);
                flickCriticalSideTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-critical-side-" + suffix);
            }
            holdGreenConnectorTexture = Resources.Load<Texture2D>("Gugarhythm/connectors/hold-green");
            holdYellowConnectorTexture = Resources.Load<Texture2D>("Gugarhythm/connectors/hold-yellow");
            holdMidMintTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/slide-tick-mint") ??
                Resources.Load<Texture2D>("Gugarhythm/particles/hold-mid-mint");
            holdMidYellowTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/slide-tick-yellow") ??
                Resources.Load<Texture2D>("Gugarhythm/particles/hold-mid-yellow");
            traceDiamondMintTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/trace-diamond-mint");
            traceDiamondPinkTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/trace-diamond-pink");
            traceDiamondYellowTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/trace-diamond-yellow");
        }

        void ClearPreviewArtwork()
        {
            ClearPreviewGuideArtwork();
            DeactivatePreviewNotes();
            ClearPreviewHoldArtwork();
        }

        void DeactivatePreviewNotes()
        {
            activePreviewNoteViews = 0;
            foreach (var visual in previewNoteViews)
                if (visual?.Body != null) visual.Body.gameObject.SetActive(false);
        }

        void ClearPreviewHoldArtwork()
        {
            activePreviewRibbons = 0;
            ClearPreviewHoldBatch(previewGreenHoldBatch);
            ClearPreviewHoldBatch(previewYellowHoldBatch);
        }

        static void ClearPreviewHoldBatch(HoldBatchGraphic batch)
        {
            if (batch == null) return;
            batch.BeginFrame();
            batch.EndFrame();
        }

        void BuildPreviewNoteArtwork(Rect rect, float bottom, float top)
        {
            activePreviewNoteViews = 0;
            foreach (var note in chart.Notes)
            {
                if (note == null || !note.Visible || note.HoldCheckpointSource == HoldCheckpointSource.Auto) continue;
                ConfigurePreviewNoteArtwork(AcquirePreviewNoteView(), rect, bottom, top, note);
            }
            for (var index = activePreviewNoteViews; index < previewNoteViews.Count; index++)
                previewNoteViews[index].Body.gameObject.SetActive(false);
        }

        PreviewNoteView AcquirePreviewNoteView()
        {
            if (activePreviewNoteViews < previewNoteViews.Count)
            {
                var pooled = previewNoteViews[activePreviewNoteViews++];
                pooled.Body.gameObject.SetActive(true);
                pooled.Body.transform.SetAsLastSibling();
                return pooled;
            }

            var go = new GameObject("Chart Preview Note", typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalSlicedRawImage));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(previewNoteRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            var body = go.GetComponent<HorizontalSlicedRawImage>();
            body.raycastTarget = false;
            body.color = Color.white;
            body.capRatio = PreviewNoteCapRatio;
            var particle = CreatePreviewRawImage("Trace Particle", body.rectTransform);
            var flickArrow = CreatePreviewRawImage("Flick Arrow", body.rectTransform);
            body.TraceParticle = particle;
            body.FlickArrow = flickArrow;
            var visual = new PreviewNoteView { Body = body, Particle = particle, FlickArrow = flickArrow };
            previewNoteViews.Add(visual);
            activePreviewNoteViews++;
            return visual;
        }

        static RawImage CreatePreviewRawImage(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            var image = go.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;
            image.gameObject.SetActive(false);
            return image;
        }

        void ConfigurePreviewNoteArtwork(PreviewNoteView visual, Rect rect, float bottom, float top, RuntimeNote note)
        {
            var columnIndex = ColumnIndexForBeat(note.Beat);
            var left = ColumnTrackLeft(rect, columnIndex);
            var center = ColumnPoint(left, bottom, top, columnIndex, note.Beat, note.Lane);
            var height = note.Kind == RuntimeNoteKind.Flick ? PreviewFlickHeight : PreviewTapHeight;
            var bodyWidth = ColumnLaneSpan(note.Size);
            var renderWidth = GugarhythmLandscapePrototype.NoteRenderQuadWidth(bodyWidth, height, note);
            var body = visual.Body;
            body.rectTransform.anchoredPosition = center;
            body.rectTransform.sizeDelta = new Vector2(renderWidth, height);
            body.ClearSurfaceQuad();
            body.capRatio = PreviewNoteCapRatio;

            // This deliberately mirrors the runtime note-art resolver.  The
            // document owns a static layout only; it must not invent a second
            // colour language for the same authored chart objects.
            var archetype = note.Archetype ?? string.Empty;
            var trace = IsTrace(note);
            var holdMid = IsHoldMid(note);
            var damage = IsDamage(note);
            var flick = note.Kind == RuntimeNoteKind.Flick;
            var traceKey = note.Critical ? "yellow" :
                archetype.IndexOf("Flick", StringComparison.OrdinalIgnoreCase) >= 0 ? "pink" : "mint";
            var buttonKey = note.Critical ? "yellow" :
                archetype.IndexOf("Slide", StringComparison.OrdinalIgnoreCase) >= 0 ? "mint" :
                note.Kind == RuntimeNoteKind.Sustain ? "mint" : "cyan";

            if (damage) body.texture = damageTexture;
            else if (holdMid) body.texture = null;
            else if (flick && trace) body.texture = TextureFor(traceTextures, traceKey);
            else if (flick) body.texture = TextureFor(buttonTextures, note.Critical ? "yellow" : "pink");
            else if (trace) body.texture = TextureFor(traceTextures, traceKey);
            else body.texture = TextureFor(buttonTextures, buttonKey);
            body.color = holdMid || body.texture == null ? Color.clear : Color.white;

            var particle = visual.Particle;
            particle.texture = holdMid
                ? note.Critical ? holdMidYellowTexture : holdMidMintTexture
                : traceKey == "yellow" ? traceDiamondYellowTexture :
                traceKey == "pink" ? traceDiamondPinkTexture : traceDiamondMintTexture;
            var showParticle = particle.texture != null && note.Visible && (trace || holdMid);
            particle.gameObject.SetActive(showParticle);
            if (showParticle)
            {
                var aspect = particle.texture.width / (float)Mathf.Max(1, particle.texture.height);
                particle.rectTransform.sizeDelta = new Vector2(height * aspect, height);
                particle.rectTransform.anchoredPosition = Vector2.zero;
                particle.color = Color.white;
                particle.uvRect = new Rect(0, 0, 1, 1);
            }

            var arrow = visual.FlickArrow;
            Texture2D arrowTexture = null;
            if (flick)
            {
                var index = FlickSpriteIndex(note.Size);
                var side = note.Direction != 0;
                arrowTexture = note.Critical
                    ? side ? flickCriticalSideTextures[index] : flickCriticalCenterTextures[index]
                    : side ? flickNormalSideTextures[index] : flickNormalCenterTextures[index];
            }
            arrow.texture = arrowTexture;
            arrow.gameObject.SetActive(arrowTexture != null);
            arrow.uvRect = note.Direction > 0 ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
            if (arrowTexture != null)
            {
                var index = FlickSpriteIndex(note.Size);
                var arrowBaseWidth = ColumnLaneSpan(Mathf.Min(note.Size, 3f) * .5f) * FlickArrowScale;
                var logicalSize = FlickLogicalSizes[index];
                var arrowWidth = arrowBaseWidth * arrowTexture.width / logicalSize;
                var arrowHeight = arrowBaseWidth * arrowTexture.height / logicalSize;
                var laneUnit = ColumnLaneSpan(.5f);
                arrow.rectTransform.sizeDelta = new Vector2(arrowWidth, arrowHeight);
                // Unlike play, the document has no animation clock.  Keep the
                // official arrow attached just above its source button, with
                // the authored left/right direction retained through uvRect.
                arrow.rectTransform.anchoredPosition = new Vector2(
                    note.Direction * laneUnit * .42f,
                    height * .45f + arrowHeight * .28f);
                arrow.color = Color.white;
            }
        }

        static Texture2D TextureFor(Dictionary<string, Texture2D> textures, string key) =>
            textures.TryGetValue(key, out var texture) ? texture : null;

        static bool IsTrace(RuntimeNote note)
        {
            var archetype = note.Archetype ?? string.Empty;
            return archetype.IndexOf("Trace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                archetype.StartsWith("USC Trace", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsHoldMid(RuntimeNote note) =>
            (note.Archetype ?? string.Empty).EndsWith("SlideTickNote", StringComparison.OrdinalIgnoreCase);

        static bool IsDamage(RuntimeNote note) =>
            (note.Archetype ?? string.Empty).IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0;

        static int FlickSpriteIndex(float size) => Mathf.Clamp(Mathf.RoundToInt(size * 2f), 1, 6) - 1;

        void BuildPreviewHoldArtwork(Rect rect, float bottom, float top)
        {
            activePreviewRibbons = 0;
            foreach (var path in chart.HoldPaths) BuildPreviewHoldPath(rect, bottom, top, path);
            foreach (var connector in chart.FallbackConnectors) BuildPreviewConnector(rect, bottom, top, connector);
            PopulatePreviewHoldBatches();
        }

        void BuildPreviewHoldPath(Rect rect, float bottom, float top, RuntimeHoldPath path)
        {
            if (path?.Segments == null || path.Evaluator == null) return;
            PreviewRibbon active = null;
            for (var segmentIndex = 0; segmentIndex < path.Segments.Count; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                if (segment?.Start == null || segment.End == null) continue;
                var sampleCount = Mathf.Clamp(Mathf.CeilToInt((float)Math.Abs(segment.End.Beat - segment.Start.Beat) * 2f), 4, 48);
                var previous = path.Evaluator.EvaluateSegment(segmentIndex, 0f);
                var previousBeat = segment.Start.Beat;
                for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
                {
                    var progress = sampleIndex / (float)sampleCount;
                    var current = path.Evaluator.EvaluateSegment(segmentIndex, progress);
                    var currentBeat = segment.Start.Beat + (segment.End.Beat - segment.Start.Beat) * progress;
                    AppendPreviewRibbonSpan(rect, bottom, top,
                        previousBeat, currentBeat, previous.Lane, current.Lane, previous.Size, current.Size,
                        segment.Critical, ref active);
                    previous = current;
                    previousBeat = currentBeat;
                }
            }
        }

        void BuildPreviewConnector(Rect rect, float bottom, float top, RuntimeConnector connector)
        {
            if (connector?.Start == null || connector.End == null) return;
            PreviewRibbon active = null;
            AppendPreviewRibbonSpan(rect, bottom, top,
                connector.Start.Beat, connector.End.Beat,
                connector.Start.Lane, connector.End.Lane,
                connector.Start.Size, connector.End.Size,
                connector.Critical, ref active);
        }

        void AppendPreviewRibbonSpan(Rect rect, float bottom, float top,
            double startBeat, double endBeat, float startLane, float endLane, float startSize, float endSize,
            bool critical, ref PreviewRibbon active)
        {
            if (!double.IsFinite(startBeat) || !double.IsFinite(endBeat)) return;
            if (endBeat < startBeat)
            {
                (startBeat, endBeat) = (endBeat, startBeat);
                (startLane, endLane) = (endLane, startLane);
                (startSize, endSize) = (endSize, startSize);
            }
            var duration = endBeat - startBeat;
            if (duration <= 1e-8d)
            {
                var columnIndex = ColumnIndexForBeat(startBeat);
                AppendPreviewRibbonPoint(rect, bottom, top, columnIndex, startBeat, startLane, startSize, critical, ref active);
                return;
            }

            var cursorBeat = startBeat;
            var cursorLane = startLane;
            var cursorSize = startSize;
            for (var splitIndex = 0; splitIndex <= columnCount + 1; splitIndex++)
            {
                var columnIndex = ColumnIndexForBeat(cursorBeat + 1e-7d);
                var columnEndBeat = documentStartBeat + (columnIndex + 1) * BeatsPerColumn;
                var splitBeat = Math.Min(endBeat, columnEndBeat);
                if (splitBeat <= cursorBeat + 1e-8d) return;
                var progress = (float)((splitBeat - startBeat) / duration);
                var splitLane = Mathf.Lerp(startLane, endLane, progress);
                var splitSize = Mathf.Lerp(startSize, endSize, progress);
                AppendPreviewRibbonPoint(rect, bottom, top, columnIndex, cursorBeat, cursorLane, cursorSize, critical, ref active);
                AppendPreviewRibbonPoint(rect, bottom, top, columnIndex, splitBeat, splitLane, splitSize, critical, ref active);
                if (splitBeat >= endBeat - 1e-8d) return;
                cursorBeat = splitBeat;
                cursorLane = splitLane;
                cursorSize = splitSize;
            }
        }

        void AppendPreviewRibbonPoint(Rect rect, float bottom, float top, int columnIndex,
            double beat, float lane, float size, bool critical, ref PreviewRibbon active)
        {
            if (active == null || active.Critical != critical || active.ColumnIndex != columnIndex)
                active = AcquirePreviewRibbon(critical, columnIndex);
            var left = ColumnTrackLeft(rect, columnIndex);
            var center = ColumnPoint(left, bottom, top, columnIndex, beat, lane);
            var width = GugarhythmLandscapePrototype.HoldConnectorRenderWidth(ColumnLaneSpan(size));
            active.AddPoint(center, width);
        }

        PreviewRibbon AcquirePreviewRibbon(bool critical, int columnIndex)
        {
            if (activePreviewRibbons >= previewRibbonPool.Count)
                previewRibbonPool.Add(new PreviewRibbon());
            var ribbon = previewRibbonPool[activePreviewRibbons++];
            ribbon.Reset(critical, columnIndex);
            return ribbon;
        }

        void PopulatePreviewHoldBatches()
        {
            var greenCount = 0;
            var yellowCount = 0;
            var greenMaxPoints = 2;
            var yellowMaxPoints = 2;
            for (var index = 0; index < activePreviewRibbons; index++)
            {
                var ribbon = previewRibbonPool[index];
                if (ribbon.Centers.Count < 2) continue;
                if (ribbon.Critical)
                {
                    yellowCount++;
                    yellowMaxPoints = Mathf.Max(yellowMaxPoints, ribbon.Centers.Count);
                }
                else
                {
                    greenCount++;
                    greenMaxPoints = Mathf.Max(greenMaxPoints, ribbon.Centers.Count);
                }
            }
            ConfigurePreviewHoldBatch(previewGreenHoldBatch, holdGreenConnectorTexture, greenCount, greenMaxPoints);
            ConfigurePreviewHoldBatch(previewYellowHoldBatch, holdYellowConnectorTexture, yellowCount, yellowMaxPoints);

            previewGreenHoldBatch.BeginFrame();
            previewYellowHoldBatch.BeginFrame();
            for (var index = 0; index < activePreviewRibbons; index++)
            {
                var ribbon = previewRibbonPool[index];
                if (ribbon.Centers.Count < 2) continue;
                var batch = ribbon.Critical ? previewYellowHoldBatch : previewGreenHoldBatch;
                batch.BeginPath(ribbon.Centers.Count);
                for (var pointIndex = 0; pointIndex < ribbon.Centers.Count; pointIndex++)
                    batch.SetPathPoint(pointIndex, ribbon.Centers[pointIndex], ribbon.Widths[pointIndex]);
                batch.EndPath();
            }
            previewGreenHoldBatch.EndFrame();
            previewYellowHoldBatch.EndFrame();
        }

        static void ConfigurePreviewHoldBatch(HoldBatchGraphic batch, Texture2D texture, int pathCount, int maxPoints)
        {
            batch.texture = texture;
            batch.SetMaterialDirty();
            batch.Prepare(pathCount, maxPoints);
        }

        void DrawColumnGrid(VertexHelper vertices, Rect rect, float bottom, float top)
        {
            var trackHeight = Mathf.Max(1f, top - bottom);
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var left = ColumnTrackLeft(rect, columnIndex);
                AddRect(vertices, left, bottom, ColumnTrackWidth, trackHeight, new Color(.012f, .026f, .052f, .98f));

                var railTint = (columnIndex % 3) switch
                {
                    0 => new Color(.42f, .18f, 1f, .72f),
                    1 => new Color(.12f, .76f, 1f, .72f),
                    _ => new Color(1f, .20f, .68f, .72f),
                };
                AddRect(vertices, left - 5f, bottom, 2f, trackHeight, railTint);
                AddRect(vertices, left - 2f, bottom, 1f, trackHeight, new Color(.72f, .46f, 1f, .45f));
                AddRect(vertices, left + ColumnTrackWidth + 1f, bottom, 1f, trackHeight, new Color(.58f, .88f, 1f, .45f));
                AddRect(vertices, left + ColumnTrackWidth + 3f, bottom, 2f, trackHeight, railTint);

                // Thirteen boundaries from -6 through +6 form twelve cells.
                for (var boundaryIndex = 0; boundaryIndex <= (int)(HalfLaneRange * 2f); boundaryIndex++)
                {
                    var lane = -HalfLaneRange + boundaryIndex;
                    var x = ColumnLaneX(left, lane);
                    var edge = boundaryIndex == 0 || boundaryIndex == (int)(HalfLaneRange * 2f);
                    var center = boundaryIndex == (int)HalfLaneRange;
                    var tint = edge
                        ? new Color(.39f, .67f, .94f, .52f)
                        : center ? new Color(.32f, .57f, .82f, .30f)
                        : new Color(.29f, .47f, .68f, .17f);
                    AddRect(vertices, x - .5f, bottom, edge ? 1.5f : 1f, trackHeight, tint);
                }

                for (var beatIndex = 0; beatIndex <= (int)BeatsPerColumn; beatIndex++)
                {
                    var beat = documentStartBeat + columnIndex * BeatsPerColumn + beatIndex;
                    var y = ColumnBeatY(bottom, top, columnIndex, beat);
                    var measure = beatIndex % (int)BeatsPerMeasure == 0;
                    var tint = measure
                        ? new Color(.45f, .66f, .86f, .38f)
                        : new Color(.31f, .49f, .68f, .14f);
                    var thickness = measure ? 1.5f : 1f;
                    AddRect(vertices, left, y - thickness * .5f, ColumnTrackWidth, thickness, tint);
                }

                for (var measureIndex = 0; measureIndex <= (int)(BeatsPerColumn / BeatsPerMeasure); measureIndex++)
                {
                    var beat = documentStartBeat + columnIndex * BeatsPerColumn + measureIndex * BeatsPerMeasure;
                    var measureNumber = Math.Max(0, (int)Math.Floor(beat / BeatsPerMeasure) + 1);
                    DrawMeasureNumber(vertices, measureNumber, left - 7f,
                        ColumnBeatY(bottom, top, columnIndex, beat), new Color(.62f, .65f, .70f, .92f));
                }
            }
        }

        void DrawColumnHoldPath(VertexHelper vertices, Rect rect, float bottom, float top, RuntimeHoldPath path)
        {
            if (path?.Segments == null || path.Evaluator == null) return;
            for (var segmentIndex = 0; segmentIndex < path.Segments.Count; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                if (segment?.Start == null || segment.End == null) continue;
                var beatSpan = Math.Abs(segment.End.Beat - segment.Start.Beat);
                var sampleCount = Mathf.Clamp(Mathf.CeilToInt((float)beatSpan * 2f), 4, 48);
                var previous = path.Evaluator.EvaluateSegment(segmentIndex, 0f);
                var previousBeat = segment.Start.Beat;
                for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
                {
                    var progress = sampleIndex / (float)sampleCount;
                    var current = path.Evaluator.EvaluateSegment(segmentIndex, progress);
                    var currentBeat = segment.Start.Beat + (segment.End.Beat - segment.Start.Beat) * progress;
                    var critical = segment.Critical;
                    DrawRibbonAcrossColumns(vertices, rect, bottom, top,
                        previousBeat, currentBeat, previous.Lane, current.Lane, previous.Size, current.Size,
                        critical ? new Color(1f, .72f, .20f, .28f) : new Color(.10f, .88f, 1f, .25f),
                        critical ? new Color(1f, .89f, .34f, .88f) : new Color(.25f, .96f, 1f, .82f));
                    previous = current;
                    previousBeat = currentBeat;
                }
            }
        }

        void DrawColumnConnector(VertexHelper vertices, Rect rect, float bottom, float top, RuntimeConnector connector)
        {
            if (connector?.Start == null || connector.End == null) return;
            DrawRibbonAcrossColumns(vertices, rect, bottom, top,
                connector.Start.Beat, connector.End.Beat,
                connector.Start.Lane, connector.End.Lane, connector.Start.Size, connector.End.Size,
                connector.Critical ? new Color(1f, .72f, .20f, .22f) : new Color(.18f, .74f, 1f, .20f),
                connector.Critical ? new Color(1f, .89f, .34f, .72f) : new Color(.38f, .84f, 1f, .62f));
        }

        void BuildPreviewGuideArtwork(Rect rect, float bottom, float top)
        {
            activePreviewGuideBatches = 0;
            activePreviewGuideSegments = 0;
            foreach (var guide in chart.Guides)
            {
                if (guide == null || !double.IsFinite(guide.Head.Beat) || !double.IsFinite(guide.Tail.Beat)) continue;

                // Start and End are Catmull-Rom controls.  The actual Guide
                // starts at Head and ends at Tail, so neither control point is
                // allowed to create an escaping decoration segment.
                var sampleCount = Mathf.Clamp(Mathf.CeilToInt((float)Math.Abs(guide.Tail.Beat - guide.Head.Beat) * 2f), 8, 64);
                var previousBeat = guide.Head.Beat;
                var previousLane = EvaluateGuideCurve(guide, guide.Start.Lane, guide.Head.Lane, guide.Tail.Lane, guide.End.Lane, 0f);
                var previousSize = EvaluateGuideCurve(guide, guide.Start.Size, guide.Head.Size, guide.Tail.Size, guide.End.Size, 0f);
                var previousTint = PreviewGuideColor(guide.Color,
                    GuideStackOptimizer.CompositeAlpha(guide.HeadOpacity, Mathf.Max(1, guide.StackCount)));
                for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
                {
                    var progress = sampleIndex / (float)sampleCount;
                    var currentBeat = LerpBeat(guide.Head.Beat, guide.Tail.Beat, progress);
                    var currentLane = EvaluateGuideCurve(guide, guide.Start.Lane, guide.Head.Lane, guide.Tail.Lane, guide.End.Lane, progress);
                    var currentSize = EvaluateGuideCurve(guide, guide.Start.Size, guide.Head.Size, guide.Tail.Size, guide.End.Size, progress);
                    var currentTint = PreviewGuideColor(guide.Color, GuideStackOptimizer.CompositeAlpha(
                        Mathf.Lerp(guide.HeadOpacity, guide.TailOpacity, progress), Mathf.Max(1, guide.StackCount)));
                    AppendPreviewGuideSpan(rect, bottom, top,
                        previousBeat, currentBeat, previousLane, currentLane, previousSize, currentSize,
                        previousTint, currentTint);
                    previousBeat = currentBeat;
                    previousLane = currentLane;
                    previousSize = currentSize;
                    previousTint = currentTint;
                }
            }

            for (var index = activePreviewGuideBatches; index < previewGuideBatches.Count; index++)
                previewGuideBatches[index].gameObject.SetActive(false);
            EndPreviewGuideArtwork();
        }

        void AppendPreviewGuideSpan(Rect rect, float bottom, float top,
            double startBeat, double endBeat, float startLane, float endLane, float startSize, float endSize,
            Color startTint, Color endTint)
        {
            if (endBeat < startBeat)
            {
                (startBeat, endBeat) = (endBeat, startBeat);
                (startLane, endLane) = (endLane, startLane);
                (startSize, endSize) = (endSize, startSize);
                (startTint, endTint) = (endTint, startTint);
            }

            var duration = endBeat - startBeat;
            if (duration <= 1e-8d) return;
            var cursorBeat = startBeat;
            var cursorLane = startLane;
            var cursorSize = startSize;
            var cursorTint = startTint;
            for (var splitIndex = 0; splitIndex <= columnCount + 1; splitIndex++)
            {
                var columnIndex = ColumnIndexForBeat(cursorBeat + 1e-7d);
                var columnEndBeat = documentStartBeat + (columnIndex + 1) * BeatsPerColumn;
                var splitBeat = Math.Min(endBeat, columnEndBeat);
                if (splitBeat <= cursorBeat + 1e-8d) return;
                var progress = (float)((splitBeat - startBeat) / duration);
                var splitLane = Mathf.Lerp(startLane, endLane, progress);
                var splitSize = Mathf.Lerp(startSize, endSize, progress);
                var splitTint = Color.Lerp(startTint, endTint, progress);
                var left = ColumnTrackLeft(rect, columnIndex);
                AcquirePreviewGuideBatch().AddSegment(
                    ColumnPoint(left, bottom, top, columnIndex, cursorBeat, cursorLane),
                    ColumnPoint(left, bottom, top, columnIndex, splitBeat, splitLane),
                    ColumnLaneSpan(Mathf.Max(.01f, cursorSize * .16f)),
                    ColumnLaneSpan(Mathf.Max(.01f, splitSize * .16f)),
                    cursorTint, splitTint);
                activePreviewGuideSegments++;
                if (splitBeat >= endBeat - 1e-8d) return;
                cursorBeat = splitBeat;
                cursorLane = splitLane;
                cursorSize = splitSize;
                cursorTint = splitTint;
            }
        }

        ChartPreviewGuideGraphic AcquirePreviewGuideBatch()
        {
            if (activePreviewGuideBatches == 0 || activePreviewGuideSegments >= MaxPreviewGuideSegmentsPerBatch)
            {
                ChartPreviewGuideGraphic batch;
                if (activePreviewGuideBatches < previewGuideBatches.Count)
                    batch = previewGuideBatches[activePreviewGuideBatches];
                else
                {
                    var go = new GameObject("Chart Preview Guide Batch", typeof(RectTransform), typeof(CanvasRenderer), typeof(ChartPreviewGuideGraphic));
                    var transform = go.GetComponent<RectTransform>();
                    transform.SetParent(previewArtworkRoot, false);
                    StretchToParent(transform);
                    batch = go.GetComponent<ChartPreviewGuideGraphic>();
                    batch.raycastTarget = false;
                    previewGuideBatches.Add(batch);
                }
                batch.gameObject.SetActive(true);
                batch.rectTransform.SetSiblingIndex(activePreviewGuideBatches);
                batch.BeginFrame();
                activePreviewGuideBatches++;
                activePreviewGuideSegments = 0;
            }
            return previewGuideBatches[activePreviewGuideBatches - 1];
        }

        void ClearPreviewGuideArtwork()
        {
            activePreviewGuideBatches = 0;
            activePreviewGuideSegments = 0;
            foreach (var batch in previewGuideBatches)
            {
                if (batch == null) continue;
                batch.BeginFrame();
                batch.EndFrame();
                batch.gameObject.SetActive(false);
            }
        }

        void EndPreviewGuideArtwork()
        {
            for (var index = 0; index < activePreviewGuideBatches; index++)
                previewGuideBatches[index].EndFrame();
        }

        static double LerpBeat(double start, double end, float progress) => start + (end - start) * Mathf.Clamp01(progress);

        static Color PreviewGuideColor(int color, float opacity)
        {
            var tint = color switch
            {
                4 => new Color(214f / 255f, 179f / 255f, 98f / 255f),
                3 => new Color(214f / 255f, 115f / 255f, 123f / 255f),
                2 => new Color(115f / 255f, 165f / 255f, 214f / 255f),
                1 => new Color(214f / 255f, 115f / 255f, 205f / 255f),
                5 => new Color(115f / 255f, 214f / 255f, 205f / 255f),
                6 => new Color(28f / 255f, 34f / 255f, 48f / 255f),
                _ => new Color(115f / 255f, 214f / 255f, 157f / 255f),
            };
            tint.a = Mathf.Clamp01(opacity);
            return tint;
        }

        static float EvaluateGuideCurve(RuntimeGuide guide, float p0, float p1, float p2, float p3, float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (guide.Ease != -1)
                return Mathf.Lerp(p1, p2, HoldPathMath.EaseProgress(progress, guide.Ease));

            var squared = progress * progress;
            var cubed = squared * progress;
            return .5f * ((2f * p1) + (-p0 + p2) * progress +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * squared +
                (-p0 + 3f * p1 - 3f * p2 + p3) * cubed);
        }

        void DrawColumnSimLine(VertexHelper vertices, Rect rect, float bottom, float top, RuntimeSimLine simLine)
        {
            if (simLine?.A == null || simLine.B == null) return;
            var columnIndex = ColumnIndexForBeat(simLine.A.Beat);
            if (columnIndex != ColumnIndexForBeat(simLine.B.Beat)) return;
            var left = ColumnTrackLeft(rect, columnIndex);
            AddColumnRibbonSegment(vertices,
                ColumnPoint(left, bottom, top, columnIndex, simLine.A.Beat, simLine.A.Lane),
                ColumnPoint(left, bottom, top, columnIndex, simLine.B.Beat, simLine.B.Lane),
                2f, 2f, new Color(.82f, .90f, 1f, .52f));
        }

        void DrawColumnNote(VertexHelper vertices, Rect rect, float bottom, float top, RuntimeNote note)
        {
            if (note == null || !note.Visible || note.HoldCheckpointSource == HoldCheckpointSource.Auto) return;
            var columnIndex = ColumnIndexForBeat(note.Beat);
            var left = ColumnTrackLeft(rect, columnIndex);
            var center = ColumnPoint(left, bottom, top, columnIndex, note.Beat, note.Lane);
            var width = ColumnLaneSpan(note.Size);
            var height = note.Kind == RuntimeNoteKind.Flick ? 8f : 6f;
            var tint = NoteTint(note);
            AddRect(vertices, center.x - width * .5f, center.y - height * .5f, width, height, tint);
            AddRect(vertices, center.x - width * .42f, center.y + height * .5f - 1.5f, width * .84f, 1.5f,
                new Color(1f, 1f, 1f, note.Critical ? .94f : .64f));
        }

        void DrawRibbonAcrossColumns(VertexHelper vertices, Rect rect, float bottom, float top,
            double startBeat, double endBeat, float startLane, float endLane, float startSize, float endSize,
            Color fill, Color outline)
        {
            if (!double.IsFinite(startBeat) || !double.IsFinite(endBeat)) return;
            if (endBeat < startBeat)
            {
                (startBeat, endBeat) = (endBeat, startBeat);
                (startLane, endLane) = (endLane, startLane);
                (startSize, endSize) = (endSize, startSize);
            }
            var duration = endBeat - startBeat;
            if (duration <= 1e-8d)
            {
                var columnIndex = ColumnIndexForBeat(startBeat);
                DrawRibbonInColumn(vertices, rect, bottom, top, columnIndex,
                    startBeat, endBeat, startLane, endLane, startSize, endSize, fill, outline);
                return;
            }

            var cursorBeat = startBeat;
            var cursorLane = startLane;
            var cursorSize = startSize;
            for (var splitIndex = 0; splitIndex <= columnCount; splitIndex++)
            {
                var columnIndex = ColumnIndexForBeat(cursorBeat + 1e-7d);
                var columnEndBeat = documentStartBeat + (columnIndex + 1) * BeatsPerColumn;
                var splitBeat = Math.Min(endBeat, columnEndBeat);
                var progress = (float)((splitBeat - startBeat) / duration);
                var splitLane = Mathf.Lerp(startLane, endLane, progress);
                var splitSize = Mathf.Lerp(startSize, endSize, progress);
                DrawRibbonInColumn(vertices, rect, bottom, top, columnIndex,
                    cursorBeat, splitBeat, cursorLane, splitLane, cursorSize, splitSize, fill, outline);
                if (splitBeat >= endBeat - 1e-8d) break;
                cursorBeat = splitBeat;
                cursorLane = splitLane;
                cursorSize = splitSize;
            }
        }

        void DrawRibbonInColumn(VertexHelper vertices, Rect rect, float bottom, float top, int columnIndex,
            double startBeat, double endBeat, float startLane, float endLane, float startSize, float endSize,
            Color fill, Color outline)
        {
            var left = ColumnTrackLeft(rect, columnIndex);
            var start = ColumnPoint(left, bottom, top, columnIndex, startBeat, startLane);
            var end = ColumnPoint(left, bottom, top, columnIndex, endBeat, endLane);
            var startWidth = ColumnLaneSpan(startSize);
            var endWidth = ColumnLaneSpan(endSize);
            AddColumnRibbonSegment(vertices, start, end, startWidth, endWidth, fill);
            var startOffset = Vector2.right * Mathf.Max(1f, startWidth * .5f);
            var endOffset = Vector2.right * Mathf.Max(1f, endWidth * .5f);
            AddColumnRibbonSegment(vertices, start - startOffset, end - endOffset, 2f, 2f, outline);
            AddColumnRibbonSegment(vertices, start + startOffset, end + endOffset, 2f, 2f, outline);
        }

        static void AddColumnRibbonSegment(VertexHelper vertices, Vector2 start, Vector2 end, float startWidth, float endWidth, Color tint)
        {
            if (Mathf.Abs(end.y - start.y) < .0001f)
            {
                var left = Mathf.Min(start.x - startWidth * .5f, end.x - endWidth * .5f);
                var right = Mathf.Max(start.x + startWidth * .5f, end.x + endWidth * .5f);
                AddRect(vertices, left, start.y - 2f, right - left, 4f, tint);
                return;
            }
            var startOffset = Vector2.right * Mathf.Max(1f, startWidth * .5f);
            var endOffset = Vector2.right * Mathf.Max(1f, endWidth * .5f);
            AddQuad(vertices, start - startOffset, start + startOffset, end + endOffset, end - endOffset, tint);
        }

        int ColumnIndexForBeat(double beat)
        {
            if (!double.IsFinite(beat)) return 0;
            var index = (int)Math.Floor((beat - documentStartBeat) / BeatsPerColumn + 1e-9d);
            return Mathf.Clamp(index, 0, columnCount - 1);
        }

        static float ColumnLaneX(float left, float lane)
        {
            if (!float.IsFinite(lane)) lane = 0f;
            var normalized = Mathf.InverseLerp(-HalfLaneRange, HalfLaneRange, Mathf.Clamp(lane, -HalfLaneRange, HalfLaneRange));
            return left + ColumnTrackWidth * normalized;
        }

        static float ColumnLaneSpan(float halfWidth)
        {
            if (!float.IsFinite(halfWidth)) halfWidth = 1f;
            return Mathf.Max(3f, ColumnTrackWidth * Mathf.Clamp(halfWidth, .25f, HalfLaneRange) / HalfLaneRange);
        }

        float ColumnBeatY(float bottom, float top, int columnIndex, double beat)
        {
            var columnStartBeat = documentStartBeat + columnIndex * BeatsPerColumn;
            var progress = Mathf.Clamp01((float)((beat - columnStartBeat) / BeatsPerColumn));
            return Mathf.Lerp(bottom, top, progress);
        }

        Vector2 ColumnPoint(float left, float bottom, float top, int columnIndex, double beat, float lane) =>
            new(ColumnLaneX(left, lane), ColumnBeatY(bottom, top, columnIndex, beat));

        static void DrawMeasureNumber(VertexHelper vertices, int number, float right, float centerY, Color tint)
        {
            number = Mathf.Max(0, number);
            var digits = number >= 100 ? 3 : 2;
            const float scale = 1.05f;
            const float digitAdvance = 7f * scale;
            var left = right - digits * digitAdvance;
            for (var digitIndex = 0; digitIndex < digits; digitIndex++)
            {
                var divisor = digitIndex switch { 0 when digits == 3 => 100, 0 => 10, 1 when digits == 3 => 10, _ => 1 };
                var digit = number / divisor % 10;
                DrawDigit(vertices, digit, left + digitIndex * digitAdvance, centerY - 4f * scale, scale, tint);
            }
        }

        static void DrawDigit(VertexHelper vertices, int digit, float x, float y, float scale, Color tint)
        {
            var mask = digit switch
            {
                0 => 0x3f, 1 => 0x06, 2 => 0x5b, 3 => 0x4f, 4 => 0x66,
                5 => 0x6d, 6 => 0x7d, 7 => 0x07, 8 => 0x7f, 9 => 0x6f,
                _ => 0,
            };
            var width = 5f * scale;
            var height = 8f * scale;
            var thickness = 1.1f * scale;
            var half = height * .5f;
            if ((mask & 0x01) != 0) AddRect(vertices, x + thickness, y + height - thickness, width - thickness * 2f, thickness, tint);
            if ((mask & 0x02) != 0) AddRect(vertices, x + width - thickness, y + half, thickness, half - thickness, tint);
            if ((mask & 0x04) != 0) AddRect(vertices, x + width - thickness, y + thickness, thickness, half - thickness, tint);
            if ((mask & 0x08) != 0) AddRect(vertices, x + thickness, y, width - thickness * 2f, thickness, tint);
            if ((mask & 0x10) != 0) AddRect(vertices, x, y + thickness, thickness, half - thickness, tint);
            if ((mask & 0x20) != 0) AddRect(vertices, x, y + half, thickness, half - thickness, tint);
            if ((mask & 0x40) != 0) AddRect(vertices, x + thickness, y + half - thickness * .5f, width - thickness * 2f, thickness, tint);
        }

        void DrawUnfoldedGrid(VertexHelper vertices, float left, float bottom, float top)
        {
            var trackHeight = UnfoldedTrackHeight(bottom, top);
            AddRect(vertices, left, bottom, chartWidth, trackHeight, new Color(.035f, .08f, .15f, .94f));

            // -6 and +6 are the outer boundaries. The thirteen lines make
            // twelve playable cells and retain the same lane semantics as play.
            for (var boundaryIndex = 0; boundaryIndex <= (int)(HalfLaneRange * 2f); boundaryIndex++)
            {
                var lane = -HalfLaneRange + boundaryIndex;
                var y = UnfoldedLaneY(bottom, top, lane);
                var edge = boundaryIndex == 0 || boundaryIndex == (int)(HalfLaneRange * 2f);
                var center = Mathf.Abs(lane) < .0001f;
                var tint = edge
                    ? new Color(.38f, .66f, .92f, .55f)
                    : center ? new Color(.32f, .58f, .84f, .36f)
                    : new Color(.30f, .50f, .70f, .18f);
                var thickness = edge ? 2f : 1f;
                AddRect(vertices, left, y - thickness * .5f, chartWidth, thickness, tint);
            }

            var documentStart = firstTime - TimePaddingSeconds;
            var documentEnd = lastTime + TimePaddingSeconds;
            var firstGrid = Math.Ceiling(documentStart / MinorGridSeconds) * MinorGridSeconds;
            for (var gridIndex = 0; gridIndex < MaxGridLines; gridIndex++)
            {
                var time = firstGrid + gridIndex * MinorGridSeconds;
                if (time > documentEnd + .0001d) break;
                var minorIndex = (long)Math.Round(time / MinorGridSeconds);
                var major = Math.Abs(minorIndex) % 4L == 0L;
                var tint = major
                    ? new Color(.42f, .68f, .94f, .36f)
                    : new Color(.32f, .52f, .75f, .15f);
                var thickness = major ? 1.5f : 1f;
                var x = UnfoldedX(left, time);
                AddRect(vertices, x - thickness * .5f, bottom, thickness, trackHeight, tint);
            }
        }

        void DrawUnfoldedHoldPath(VertexHelper vertices, float left, float bottom, float top, RuntimeHoldPath path)
        {
            if (path?.Segments == null || path.Evaluator == null) return;
            for (var segmentIndex = 0; segmentIndex < path.Segments.Count; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                if (segment?.Start == null || segment.End == null) continue;
                var previous = path.Evaluator.EvaluateSegment(segmentIndex, 0f);
                var previousTime = segment.Start.Time;
                for (var sampleIndex = 1; sampleIndex <= 4; sampleIndex++)
                {
                    var progress = sampleIndex / 4f;
                    var current = path.Evaluator.EvaluateSegment(segmentIndex, progress);
                    var currentTime = segment.Start.Time + (segment.End.Time - segment.Start.Time) * progress;
                    var critical = segment.Critical;
                    DrawUnfoldedRibbon(vertices,
                        UnfoldedPoint(left, bottom, top, previousTime, previous.Lane), UnfoldedPoint(left, bottom, top, currentTime, current.Lane),
                        UnfoldedLaneSpan(bottom, top, previous.Size), UnfoldedLaneSpan(bottom, top, current.Size),
                        critical ? new Color(1f, .79f, .23f, .28f) : new Color(.30f, .96f, .50f, .22f),
                        critical ? new Color(1f, .89f, .35f, .84f) : new Color(.43f, 1f, .61f, .76f));
                    previous = current;
                    previousTime = currentTime;
                }
            }
        }

        void DrawUnfoldedConnector(VertexHelper vertices, float left, float bottom, float top, RuntimeConnector connector)
        {
            if (connector?.Start == null || connector.End == null) return;
            DrawUnfoldedRibbon(vertices,
                UnfoldedPoint(left, bottom, top, connector.Start.Time, connector.Start.Lane), UnfoldedPoint(left, bottom, top, connector.End.Time, connector.End.Lane),
                UnfoldedLaneSpan(bottom, top, connector.Start.Size), UnfoldedLaneSpan(bottom, top, connector.End.Size),
                connector.Critical ? new Color(1f, .79f, .23f, .22f) : new Color(.30f, .76f, 1f, .18f),
                connector.Critical ? new Color(1f, .89f, .35f, .68f) : new Color(.47f, .82f, 1f, .56f));
        }

        void DrawUnfoldedGuide(VertexHelper vertices, float left, float bottom, float top, RuntimeGuide guide)
        {
            if (guide == null) return;
            var tint = new Color(.65f, .48f, 1f, .44f);
            AddUnfoldedGuideSegment(vertices, left, bottom, top, guide.Start, guide.Head, tint);
            AddUnfoldedGuideSegment(vertices, left, bottom, top, guide.Head, guide.Tail, tint);
            AddUnfoldedGuideSegment(vertices, left, bottom, top, guide.Tail, guide.End, tint);
        }

        void AddUnfoldedGuideSegment(VertexHelper vertices, float left, float bottom, float top, RuntimeGuidePoint start, RuntimeGuidePoint end, Color tint) =>
            AddUnfoldedRibbonSegment(vertices,
                UnfoldedPoint(left, bottom, top, start.Time, start.Lane), UnfoldedPoint(left, bottom, top, end.Time, end.Lane),
                Mathf.Max(2f, UnfoldedLaneSpan(bottom, top, start.Size) * .16f), Mathf.Max(2f, UnfoldedLaneSpan(bottom, top, end.Size) * .16f), tint);

        void DrawUnfoldedNote(VertexHelper vertices, float left, float bottom, float top, RuntimeNote note)
        {
            if (note == null || !note.Visible || note.HoldCheckpointSource == HoldCheckpointSource.Auto) return;
            var center = UnfoldedPoint(left, bottom, top, note.Time, note.Lane);
            // Document symbols stay compact even for wide gameplay notes: the
            // translucent Hold band carries the full lane width, while these
            // markers remain readable in a densely unfolded score.
            var height = Mathf.Clamp(UnfoldedLaneSpan(bottom, top, note.Size) * .16f, 6f, 18f);
            var width = note.Kind == RuntimeNoteKind.Flick ? 24f : 20f;
            var tint = NoteTint(note);
            AddRect(vertices, center.x - width * .5f, center.y - height * .5f, width, height, tint);
            AddRect(vertices, center.x - (width - 6f) * .5f, center.y - 1.25f, width - 6f, 2.5f, new Color(1f, 1f, 1f, .84f));
            if (note.Critical)
                AddRect(vertices, center.x - width * .5f, center.y + height * .5f - 2f, width, 2f, new Color(1f, .94f, .58f, .96f));
        }

        Vector2 UnfoldedPoint(float left, float bottom, float top, double time, float lane) =>
            new(UnfoldedX(left, time), UnfoldedLaneY(bottom, top, lane));

        float UnfoldedX(float left, double time) =>
            left + ChartPreviewLayout.DocumentX(time, firstTime - TimePaddingSeconds, lastTime + TimePaddingSeconds, chartWidth);

        static float UnfoldedTrackHeight(float bottom, float top) => Mathf.Max(1f, top - bottom);

        static float UnfoldedLaneY(float bottom, float top, float lane)
        {
            if (!float.IsFinite(lane)) lane = 0f;
            var normalized = Mathf.InverseLerp(-HalfLaneRange, HalfLaneRange, Mathf.Clamp(lane, -HalfLaneRange, HalfLaneRange));
            return bottom + UnfoldedTrackHeight(bottom, top) * normalized;
        }

        static float UnfoldedLaneSpan(float bottom, float top, float halfWidth)
        {
            if (!float.IsFinite(halfWidth)) halfWidth = 1f;
            // Runtime note size is the distance from the lane centre to one
            // edge, so size 6 spans the complete -6 through +6 surface.
            return Mathf.Max(3f, UnfoldedTrackHeight(bottom, top) * Mathf.Clamp(halfWidth, .25f, HalfLaneRange) / HalfLaneRange);
        }

        static void DrawUnfoldedRibbon(VertexHelper vertices, Vector2 start, Vector2 end, float startWidth, float endWidth, Color fill, Color outline)
        {
            AddUnfoldedRibbonSegment(vertices, start, end, startWidth, endWidth, fill);
            var startOffset = Vector2.up * Mathf.Max(1f, startWidth * .5f);
            var endOffset = Vector2.up * Mathf.Max(1f, endWidth * .5f);
            AddUnfoldedRibbonSegment(vertices, start - startOffset, end - endOffset, 2.5f, 2.5f, outline);
            AddUnfoldedRibbonSegment(vertices, start + startOffset, end + endOffset, 2.5f, 2.5f, outline);
        }

        static void AddUnfoldedRibbonSegment(VertexHelper vertices, Vector2 start, Vector2 end, float startWidth, float endWidth, Color tint)
        {
            if (Mathf.Abs(end.x - start.x) < .0001f)
            {
                var lower = Mathf.Min(start.y - startWidth * .5f, end.y - endWidth * .5f);
                var upper = Mathf.Max(start.y + startWidth * .5f, end.y + endWidth * .5f);
                AddRect(vertices, start.x - 2f, lower, 4f, upper - lower, tint);
                return;
            }
            // Lane width is vertical at every time slice. Keeping offsets
            // vertical preserves a score-document ribbon instead of rotating
            // it into a diagonal wedge when a Slide changes lanes.
            var startOffset = Vector2.up * Mathf.Max(1f, startWidth * .5f);
            var endOffset = Vector2.up * Mathf.Max(1f, endWidth * .5f);
            AddQuad(vertices, start - startOffset, start + startOffset, end + endOffset, end - endOffset, tint);
        }

        static Color NoteTint(RuntimeNote note)
        {
            var tint = note.Kind switch
            {
                RuntimeNoteKind.Flick => new Color(.98f, .43f, .55f, .96f),
                RuntimeNoteKind.Sustain => new Color(.38f, .92f, .70f, .96f),
                RuntimeNoteKind.Release => new Color(1f, .79f, .31f, .96f),
                _ => new Color(.42f, .80f, 1f, .96f),
            };
            return note.Critical ? Color.Lerp(tint, Color.white, .35f) : tint;
        }

        static void AddRect(VertexHelper vertices, float x, float y, float width, float height, Color tint) =>
            AddQuad(vertices, new Vector2(x, y), new Vector2(x, y + height), new Vector2(x + width, y + height), new Vector2(x + width, y), tint);

        static void AddQuad(VertexHelper vertices, Vector2 firstPoint, Vector2 secondPoint, Vector2 thirdPoint, Vector2 fourthPoint, Color tint)
        {
            var first = vertices.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.position = firstPoint; vertices.AddVert(vertex);
            vertex.position = secondPoint; vertices.AddVert(vertex);
            vertex.position = thirdPoint; vertices.AddVert(vertex);
            vertex.position = fourthPoint; vertices.AddVert(vertex);
            vertices.AddTriangle(first, first + 1, first + 2);
            vertices.AddTriangle(first, first + 2, first + 3);
        }

        static void FindTimeRange(RuntimeChart source, out double first, out double last)
        {
            first = double.PositiveInfinity;
            last = double.NegativeInfinity;
            if (source != null)
            {
                foreach (var note in source.Notes) ConsiderTime(note?.Time ?? double.NaN, ref first, ref last);
                foreach (var path in source.HoldPaths)
                    if (path != null)
                        foreach (var note in path.Nodes) ConsiderTime(note?.Time ?? double.NaN, ref first, ref last);
                foreach (var connector in source.FallbackConnectors)
                {
                    ConsiderTime(connector?.Start?.Time ?? double.NaN, ref first, ref last);
                    ConsiderTime(connector?.End?.Time ?? double.NaN, ref first, ref last);
                }
                foreach (var guide in source.Guides)
                {
                    if (guide == null) continue;
                    ConsiderTime(guide.Start.Time, ref first, ref last);
                    ConsiderTime(guide.Head.Time, ref first, ref last);
                    ConsiderTime(guide.Tail.Time, ref first, ref last);
                    ConsiderTime(guide.End.Time, ref first, ref last);
                }
            }
            if (!double.IsFinite(first) || !double.IsFinite(last))
            {
                first = 0d;
                last = 1d;
            }
            else if (last - first < 1e-6d)
            {
                last = first + 1d;
            }
        }

        static void FindBeatRange(RuntimeChart source, out double first, out double last)
        {
            first = double.PositiveInfinity;
            last = double.NegativeInfinity;
            if (source != null)
            {
                foreach (var note in source.Notes) ConsiderTime(note?.Beat ?? double.NaN, ref first, ref last);
                foreach (var path in source.HoldPaths)
                    if (path != null)
                        foreach (var note in path.Nodes) ConsiderTime(note?.Beat ?? double.NaN, ref first, ref last);
                foreach (var connector in source.FallbackConnectors)
                {
                    ConsiderTime(connector?.Start?.Beat ?? double.NaN, ref first, ref last);
                    ConsiderTime(connector?.End?.Beat ?? double.NaN, ref first, ref last);
                }
                foreach (var guide in source.Guides)
                {
                    if (guide == null) continue;
                    ConsiderTime(guide.Start.Beat, ref first, ref last);
                    ConsiderTime(guide.Head.Beat, ref first, ref last);
                    ConsiderTime(guide.Tail.Beat, ref first, ref last);
                    ConsiderTime(guide.End.Beat, ref first, ref last);
                }
            }
            if (!double.IsFinite(first) || !double.IsFinite(last))
            {
                first = 0d;
                last = BeatsPerColumn;
            }
            else if (last - first < 1e-6d)
            {
                last = first + BeatsPerColumn;
            }
        }

        static void ConsiderTime(double value, ref double first, ref double last)
        {
            if (!double.IsFinite(value)) return;
            if (value < first) first = value;
            if (value > last) last = value;
        }
    }
}
