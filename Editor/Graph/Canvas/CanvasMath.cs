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

        /** <summary>Makes a view; zoom is kept within its limits.</summary> */
        public CanvasView(Vector2 pan, float zoom)
        {
            Pan = pan;
            Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        }

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

        /** <summary>The view that fits a content rect in a viewport with a margin, no closer than 100%.</summary> */
        public static CanvasView Framing(Rect content, Vector2 viewportSize, float margin = 40f)
        {
            if (content.width <= 0f || content.height <= 0f) return new CanvasView(viewportSize / 2f - content.center, 1f);
            var fit = Mathf.Min((viewportSize.x - margin * 2f) / content.width, (viewportSize.y - margin * 2f) / content.height);
            var zoom = Mathf.Clamp(fit, MinZoom, 1f);
            return new CanvasView(viewportSize / 2f - content.center * zoom, zoom);
        }

        #endregion
    }
}
