using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using QuestSystem.EditorTools;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace QuestSystem.Tests
{
    /** <summary>The quest graph canvas's maths: the view, wire curves, and placing wire labels where they stay readable.</summary> */
    public sealed class QuestGraphCanvasTests
    {
        private static void AssertNear(Vector2 actual, Vector2 expected) => Assert.That(Vector2.Distance(actual, expected), Is.LessThan(1e-3f), $"{actual} vs {expected}");

        [Test]
        public void TheView_MapsBothWays_AndZoomsAboutAPointThatStaysPut()
        {
            var view = new CanvasView(new Vector2(30, -20), 0.5f);
            AssertNear(view.ToScreen(view.ToContent(new Vector2(123, 45))), new Vector2(123, 45));
            var cursor = new Vector2(400, 300);
            var under = view.ToContent(cursor);
            var zoomed = view.ZoomedAbout(cursor, 1.6f);
            AssertNear(zoomed.ToContent(cursor), under);
            Assert.That(zoomed.Zoom, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(view.ZoomedAbout(cursor, 100f).Zoom, Is.EqualTo(CanvasView.MaxZoom));
            Assert.That(view.ZoomedAbout(cursor, 0.001f).Zoom, Is.EqualTo(CanvasView.MinZoom));
            Assert.That(new CanvasView(Vector2.zero, 2f).Visible(new Vector2(800, 600)), Is.EqualTo(new Rect(0, 0, 400, 300)));
        }

        [Test]
        public void AViewBeforeLayout_IsNeverNaN()
        {
            var nan = new Vector2(float.NaN, float.NaN);
            Assert.That(CanvasView.HasSize(nan), Is.False, "an element's size is NaN before its first layout");
            Assert.That(CanvasView.HasSize(Vector2.zero), Is.False);
            Assert.That(CanvasView.HasSize(new Vector2(800, 600)));
            var view = new CanvasView(nan, float.NaN);
            Assert.That(view.Pan, Is.EqualTo(Vector2.zero));
            Assert.That(view.Zoom, Is.EqualTo(1f));
            var framed = CanvasView.FramingBelow(new Rect(0, 0, 500, 300), nan, 40f, 20f, CanvasView.MinZoom);
            Assert.That(float.IsFinite(framed.Zoom) && float.IsFinite(framed.Pan.x));
        }

        [Test]
        public void Framing_FitsTheContentBelowTheTop_AndNeverZoomsPast100Percent()
        {
            var viewport = new Vector2(1000, 800);
            var big = CanvasView.FramingBelow(new Rect(0, 0, 2000, 500), viewport, 60f, 20f, CanvasView.MinZoom);
            Assert.That(big.Zoom, Is.EqualTo(0.48f).Within(1e-4f), "960 wide fits 2000");
            AssertNear(big.ToScreen(new Vector2(0, 0)), new Vector2(20, 60));
            AssertNear(big.ToScreen(new Vector2(2000, 0)), new Vector2(980, 60));

            var small = CanvasView.FramingBelow(new Rect(100, 100, 200, 100), viewport, 60f, 20f, CanvasView.MinZoom);
            Assert.That(small.Zoom, Is.EqualTo(CanvasView.MaxFitZoom));
            AssertNear(small.ToScreen(new Vector2(100, 100)), new Vector2(400, 60));
            Assert.That(CanvasView.FramingBelow(new Rect(5, 5, 0, 0), viewport, 0f, 0f, CanvasView.MinZoom).Zoom, Is.EqualTo(CanvasView.MaxFitZoom));
        }

        [Test]
        public void Framing_WithAMinimumZoom_StartsFromTheLeftWhenItCantFit()
        {
            var view = CanvasView.FramingBelow(new Rect(-50, 0, 4000, 500), new Vector2(1000, 800), 60f, 20f, 0.8f);
            Assert.That(view.Zoom, Is.EqualTo(0.8f));
            AssertNear(view.ToScreen(new Vector2(-50, 0)), new Vector2(20, 60));
        }

        [Test]
        public void TheUnionOfRects_HoldsThemAll()
        {
            Assert.That(CanvasView.Union(new Rect[0]), Is.EqualTo(new Rect()));
            Assert.That(CanvasView.Union(new[] { new Rect(0, 0, 10, 10), new Rect(-5, 20, 10, 10) }), Is.EqualTo(Rect.MinMaxRect(-5, 0, 10, 30)));
        }

        [Test]
        public void Ports_SpreadEvenlyInsideTheEdge()
        {
            Assert.That(PortLayout.Spread(0, 0f, 52f), Is.Empty);
            Assert.That(PortLayout.Spread(1, 100f, 52f), Is.EqualTo(new[] { 126f }));
            var three = PortLayout.Spread(3, 0f, 52f);
            Assert.That(three[0], Is.GreaterThan(PortLayout.Inset));
            Assert.That(three[2], Is.LessThan(52f - PortLayout.Inset));
            Assert.That(three[1] - three[0], Is.EqualTo(three[2] - three[1]).Within(1e-4f));
        }

        [Test]
        public void Dragging_WaitsForTheThreshold_ScalesByZoom_AndSnaps()
        {
            var drag = new NodeDrag<string>();
            Assert.That(drag.Move(Vector2.zero, 1f), Is.Null, "nothing to move before a press");
            drag.Begin(new Dictionary<string, Vector2> { ["a"] = Vector2.zero, ["b"] = new Vector2(100, 0) }, new Vector2(10, 10));
            Assert.That(drag.Active);
            Assert.That(drag.Move(new Vector2(11, 11), 1f), Is.Null, "within the threshold it is still a click");
            Assert.That(drag.Moved, Is.False);
            var moved = drag.Move(new Vector2(40, 10), 0.5f);
            Assert.That(moved["a"], Is.EqualTo(new Vector2(60, 0)));
            Assert.That(moved["b"], Is.EqualTo(new Vector2(160, 0)));
            Assert.That(drag.Keys, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(drag.End(), "it became a drag");
            Assert.That(drag.Active, Is.False);
            Assert.That(drag.End(), Is.False);
        }

        [Test]
        public void StoryFlags_SitAboveTheirFirstReader_InRowsThatNeverOverlap()
        {
            var places = FlagRows.Place(new[] { 100f, 120f, 600f, 640f }, 200f);
            Assert.That(places[0], Is.EqualTo(new Vector2(100f - FlagRows.Lead, 200f - FlagRows.RowAbove)));
            Assert.That(places[1].y, Is.EqualTo(200f - FlagRows.RowAbove - FlagRows.RowStep), "too close to the first, so a row up");
            Assert.That(places[2].y, Is.EqualTo(200f - FlagRows.RowAbove), "far enough along to share the first row");
            for (var i = 0; i < places.Count; i++)
                for (var j = i + 1; j < places.Count; j++)
                    Assert.That(new Rect(places[i], new Vector2(FlagRows.FlagWidth, FlagRows.FlagHeight)).Overlaps(new Rect(places[j], new Vector2(FlagRows.FlagWidth, FlagRows.FlagHeight))), Is.False, $"flags {i} and {j}");
            Assert.That(FlagRows.Place(new float[0], 0f), Is.Empty);
        }

        [Test]
        public void Wires_RunEndToEnd_AndTheirBoundsHoldThem()
        {
            Vector2 from = new Vector2(0, 0), to = new Vector2(300, 120);
            AssertNear(WireCurve.Point(from, to, 0f), from);
            AssertNear(WireCurve.Point(from, to, 1f), to);
            var bounds = WireCurve.Bounds(from, to);
            var points = new List<Vector2>();
            WireCurve.Sample(from, to, 50, points);
            Assert.That(points, Has.Count.EqualTo(51));
            Assert.That(points.TrueForAll(p => bounds.Contains(p)));
            Assert.That(WireCurve.Distance(from, to, WireCurve.Point(from, to, 0.3f)), Is.LessThan(1f));
            Assert.That(WireCurve.Distance(from, to, new Vector2(0, 500)), Is.GreaterThan(300f));
        }

        /**
         * <summary>Nodes in columns, spaced as the web lays quests out, each wired to the next column's same row and
         * every other one fanning out to the row below. Ports spread along each node's edge with the web's own
         * <see cref="PortLayout" />, ordered by where the other end sits.</summary>
         */
        private static (List<Rect> nodes, List<LabelRequest> labels) Web(int columns, int rows)
        {
            var nodes = new List<Rect>();
            for (var c = 0; c < columns; c++)
                for (var r = 0; r < rows; r++)
                    nodes.Add(new Rect(WebLayout.Left + c * WebLayout.ColumnWidth, 40 + r * 120, WebLayout.NodeWidth, WebLayout.NodeHeight));
            var edges = new List<(int from, int to)>();
            for (var c = 0; c + 1 < columns; c++)
                for (var r = 0; r < rows; r++)
                {
                    edges.Add((c * rows + r, (c + 1) * rows + r));
                    if (r % 2 == 0 && r + 1 < rows) edges.Add((c * rows + r, (c + 1) * rows + r + 1));
                }
            float Port(int node, int edge, bool outgoing)
            {
                var mine = new List<int>();
                for (var i = 0; i < edges.Count; i++)
                    if ((outgoing ? edges[i].from : edges[i].to) == node) mine.Add(i);
                mine.Sort((a, b) => nodes[outgoing ? edges[a].to : edges[a].from].y.CompareTo(nodes[outgoing ? edges[b].to : edges[b].from].y));
                return PortLayout.Spread(mine.Count, nodes[node].y, nodes[node].height)[mine.IndexOf(edge)];
            }
            var labels = new List<LabelRequest>();
            for (var i = 0; i < edges.Count; i++)
                labels.Add(new LabelRequest(new Vector2(nodes[edges[i].from].xMax, Port(edges[i].from, i, true)), new Vector2(nodes[edges[i].to].xMin, Port(edges[i].to, i, false)), new Vector2(70, 16)));
            return (nodes, labels);
        }

        [Test]
        public void Labels_StayClearOfNodesLabelsWiresAndEnds_WhenThereIsRoom()
        {
            var (nodes, labels) = Web(4, 4);
            var centres = LabelPlacer.Place(labels, nodes);
            var boxes = new List<Rect>();
            for (var i = 0; i < labels.Count; i++)
            {
                var box = LabelPlacer.Box(centres[i], labels[i].Size);
                Assert.That(nodes.TrueForAll(n => !n.Overlaps(box)), $"label {i} covers a node");
                Assert.That(boxes.TrueForAll(b => !b.Overlaps(box)), $"label {i} covers a label");
                Assert.That(Vector2.Distance(centres[i], labels[i].To), Is.GreaterThanOrEqualTo(labels[i].Size.x / 2f + LabelPlacer.EndClearance - 0.01f), $"label {i} covers its arrowhead");
                for (var j = 0; j < labels.Count; j++)
                {
                    if (j == i) continue;
                    var points = new List<Vector2>();
                    WireCurve.Sample(labels[j].From, labels[j].To, 64, points);
                    Assert.That(points.TrueForAll(p => !box.Contains(p)), $"label {i} covers wire {j}");
                }
                boxes.Add(box);
            }
        }

        [Test]
        public void Placement_IsTheSameEveryTime()
        {
            var (nodes, labels) = Web(5, 4);
            Assert.That(LabelPlacer.Place(labels, nodes), Is.EqualTo(LabelPlacer.Place(labels, nodes)));
        }

        [Test]
        public void AWireTooShortToKeepClearOfItsEnds_StillGetsALabel_AtItsMiddle()
        {
            var labels = new List<LabelRequest> { new LabelRequest(new Vector2(0, 0), new Vector2(60, 0), new Vector2(70, 16)) };
            var centres = LabelPlacer.Place(labels, new List<Rect>());
            Assert.That(centres, Has.Length.EqualTo(1));
            AssertNear(centres[0], WireCurve.Point(labels[0].From, labels[0].To, 0.5f));
        }

        [Test]
        public void ALabel_MovesAlongItsWire_ToGetOffANode()
        {
            var labels = new List<LabelRequest> { new LabelRequest(new Vector2(0, 0), new Vector2(400, 0), new Vector2(40, 16)) };
            var node = new Rect(150, -20, 100, 40);
            var centre = LabelPlacer.Place(labels, new List<Rect> { node })[0];
            Assert.That(LabelPlacer.Box(centre, labels[0].Size).Overlaps(node), Is.False);
        }

        [Test]
        public void AWireCoveredEverywhere_StillGetsALabel_AtItsMiddle()
        {
            var labels = new List<LabelRequest> { new LabelRequest(new Vector2(0, 0), new Vector2(400, 0), new Vector2(40, 16)) };
            var centre = LabelPlacer.Place(labels, new List<Rect> { new Rect(-50, -50, 500, 100) })[0];
            AssertNear(centre, WireCurve.Point(labels[0].From, labels[0].To, 0.5f));
        }

        [Test]
        public void FiveHundredLabels_ArePlacedQuickly()
        {
            var (nodes, labels) = Web(10, 40);
            var watch = Stopwatch.StartNew();
            LabelPlacer.Place(labels, nodes);
            watch.Stop();
            Debug.Log($"Quest graph: placed {labels.Count} labels among {nodes.Count} nodes in {watch.ElapsedMilliseconds} ms");
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000));
        }
    }
}
