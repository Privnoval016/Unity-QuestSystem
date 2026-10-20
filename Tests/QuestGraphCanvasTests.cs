using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using QuestSystem.EditorTools;
using UnityEngine;

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
        public void Framing_FitsTheContent_AndNeverZoomsPast100Percent()
        {
            var big = CanvasView.Framing(new Rect(0, 0, 4000, 1000), new Vector2(1000, 800));
            var visible = big.Visible(new Vector2(1000, 800));
            Assert.That(visible.xMin, Is.LessThanOrEqualTo(0f));
            Assert.That(visible.xMax, Is.GreaterThanOrEqualTo(4000f));
            Assert.That(CanvasView.Framing(new Rect(0, 0, 100, 100), new Vector2(1000, 800)).Zoom, Is.EqualTo(1f));
            Assert.That(CanvasView.Framing(new Rect(5, 5, 0, 0), new Vector2(100, 100)).Zoom, Is.EqualTo(1f));
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
         * every other one fanning out to the row below. Ports spread along each node's edge, ordered by where the
         * other end sits, as the web draws them.</summary>
         */
        private static (List<Rect> nodes, List<LabelRequest> labels) Web(int columns, int rows)
        {
            var nodes = new List<Rect>();
            for (var c = 0; c < columns; c++)
                for (var r = 0; r < rows; r++)
                    nodes.Add(new Rect(100 + c * 320, 40 + r * 120, 196, 52));
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
                return nodes[node].y + nodes[node].height * (mine.IndexOf(edge) + 1) / (mine.Count + 1);
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
        public void ACrowdedWire_StillGetsALabel_AtTheSpotThatCoversLeast()
        {
            var labels = new List<LabelRequest> { new LabelRequest(new Vector2(0, 0), new Vector2(60, 0), new Vector2(70, 16)) };
            var centres = LabelPlacer.Place(labels, new List<Rect>());
            Assert.That(centres, Has.Length.EqualTo(1));
            AssertNear(centres[0], new Vector2(30, 0));
        }

        [Test]
        public void FiveHundredLabels_ArePlacedQuickly()
        {
            var (nodes, labels) = Web(10, 40);
            var watch = Stopwatch.StartNew();
            LabelPlacer.Place(labels, nodes);
            watch.Stop();
            UnityEngine.Debug.Log($"Quest graph: placed {labels.Count} labels among {nodes.Count} nodes in {watch.ElapsedMilliseconds} ms");
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000));
        }
    }
}
