using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>The canvas's grid, drawn in screen space so lines stay crisp at any zoom; colours from the stylesheet.</summary> */
    public sealed class CanvasGrid : VisualElement
    {
        #region Private Fields

        private static readonly CustomStyleProperty<Color> MinorColor = new CustomStyleProperty<Color>("--grid-minor");
        private static readonly CustomStyleProperty<Color> MajorColor = new CustomStyleProperty<Color>("--grid-major");

        /** <summary>Content units between minor and major lines.</summary> */
        private const float Minor = 20f, Major = 100f;

        /** <summary>Minor lines closer than this on screen aren't drawn.</summary> */
        private const float MinSpacing = 8f;

        private Color _minor = new Color(1f, 1f, 1f, 0.03f);
        private Color _major = new Color(1f, 1f, 1f, 0.06f);
        private CanvasView _view = new CanvasView(Vector2.zero, 1f);

        #endregion

        #region Public API

        /** <summary>Makes a grid.</summary> */
        public CanvasGrid()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("quest-graph__grid");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                evt.customStyle.TryGetValue(MinorColor, out _minor);
                evt.customStyle.TryGetValue(MajorColor, out _major);
                MarkDirtyRepaint();
            });
        }

        /** <summary>Follows the canvas's view.</summary> */
        public void SetView(CanvasView view)
        {
            _view = view;
            MarkDirtyRepaint();
        }

        #endregion

        #region Helpers

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var size = contentRect.size;
            painter.lineWidth = 1f;
            if (Minor * _view.Zoom >= MinSpacing) DrawLines(painter, size, Minor, _minor);
            DrawLines(painter, size, Major, _major);
        }

        private void DrawLines(Painter2D painter, Vector2 size, float step, Color color)
        {
            var spacing = step * _view.Zoom;
            painter.strokeColor = color;
            painter.BeginPath();
            for (var x = Mathf.Repeat(_view.Pan.x, spacing); x < size.x; x += spacing)
            {
                painter.MoveTo(new Vector2(Mathf.Round(x) + 0.5f, 0f));
                painter.LineTo(new Vector2(Mathf.Round(x) + 0.5f, size.y));
            }
            for (var y = Mathf.Repeat(_view.Pan.y, spacing); y < size.y; y += spacing)
            {
                painter.MoveTo(new Vector2(0f, Mathf.Round(y) + 0.5f));
                painter.LineTo(new Vector2(size.x, Mathf.Round(y) + 0.5f));
            }
            painter.Stroke();
        }

        #endregion
    }
}
