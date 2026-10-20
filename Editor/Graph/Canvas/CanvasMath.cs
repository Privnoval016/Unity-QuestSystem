using System.Collections.Generic;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>The canvas's view: content drawn at <c>screen = content × zoom + pan</c>.</summary> */
    public readonly struct CanvasView
    {
        #region Properties

        /** <summary>Where the content's origin sits on screen.</summary> */
        public Vector2 Pan { get; }

        /** <summary>How large content is drawn.</summary> */
        public float Zoom { get; }

        #endregion

        #region Public API

        /** <summary>The smallest and largest zoom.</summary> */
        public const float MinZoom = 0.25f, MaxZoom = 2f;

        /** <summary>Framing never zooms in past this, so a small graph stays at its true size.</summary> */
        public const float MaxFitZoom = 1f;

        /** <summary>No pan, at 100%.</summary> */
        public static CanvasView Default => new CanvasView(Vector2.zero, 1f);

        /** <summary>Makes a view; zoom is kept within its limits, and a value that isn't a number falls back to no pan and 100%.</summary> */
        public CanvasView(Vector2 pan, float zoom)
        {
            Pan = float.IsFinite(pan.x) && float.IsFinite(pan.y) ? pan : Vector2.zero;
            Zoom = float.IsFinite(zoom) ? Mathf.Clamp(zoom, MinZoom, MaxZoom) : 1f;
        }

        /** <summary>True when a viewport has a real size; before its first layout it has none.</summary> */
        public static bool HasSize(Vector2 viewportSize) => float.IsFinite(viewportSize.x) && float.IsFinite(viewportSize.y) && viewportSize.x > 0f && viewportSize.y > 0f;

        /** <summary>A screen point in content space.</summary> */
        public Vector2 ToContent(Vector2 screen) => (screen - Pan) / Zoom;

        /** <summary>A content point on screen.</summary> */
        public Vector2 ToScreen(Vector2 content) => content * Zoom + Pan;

        /** <summary>The part of the content a viewport of this size shows.</summary> */
        public Rect Visible(Vector2 viewportSize) => new Rect(ToContent(Vector2.zero), viewportSize / Zoom);

        /** <summary>The view zoomed by a factor about a screen point, which stays where it is.</summary> */
        public CanvasView ZoomedAbout(Vector2 screen, float factor)
        {
            var zoom = Mathf.Clamp(Zoom * factor, MinZoom, MaxZoom);
            return new CanvasView(screen - (screen - Pan) * (zoom / Zoom), zoom);
        }

        /** <summary>The view moved by a screen distance.</summary> */
        public CanvasView Panned(Vector2 delta) => new CanvasView(Pan + delta, Zoom);

        /**
         * <summary>The view that shows a content rect below a top inset: as large as fits up to 100% and no smaller than
         * <paramref name="minZoom" />, centred across when it fits and from the left when it doesn't.</summary>
         */
        public static CanvasView FramingBelow(Rect content, Vector2 viewportSize, float top, float margin, float minZoom)
        {
            var area = new Vector2(viewportSize.x - margin * 2f, viewportSize.y - top - margin);
            var fit = content.width > 0f && content.height > 0f ? Mathf.Min(area.x / content.width, area.y / content.height) : MaxFitZoom;
            var zoom = Mathf.Clamp(fit, Mathf.Max(minZoom, MinZoom), MaxFitZoom);
            var width = content.width * zoom;
            var x = width <= area.x ? margin + (area.x - width) / 2f : margin;
            return new CanvasView(new Vector2(x - content.xMin * zoom, top - content.yMin * zoom), zoom);
        }

        /** <summary>The smallest rect holding every rect given; an empty rect when none is.</summary> */
        public static Rect Union(IEnumerable<Rect> rects)
        {
            var any = false;
            var union = new Rect();
            foreach (var rect in rects)
            {
                union = any ? Rect.MinMaxRect(Mathf.Min(union.xMin, rect.xMin), Mathf.Min(union.yMin, rect.yMin), Mathf.Max(union.xMax, rect.xMax), Mathf.Max(union.yMax, rect.yMax)) : rect;
                any = true;
            }
            return union;
        }

        #endregion
    }
}
