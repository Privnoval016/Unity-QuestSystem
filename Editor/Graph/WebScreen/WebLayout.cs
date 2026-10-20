using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>A band of the web: every quest of one kind.</summary> */
    public readonly struct WebBand
    {
        /** <summary>The kind; null for quests with none.</summary> */
        public QuestKind Kind { get; }

        /** <summary>Where the band starts.</summary> */
        public float Top { get; }

        /** <summary>How tall it is.</summary> */
        public float Height { get; }

        /** <summary>Makes a band.</summary> */
        public WebBand(QuestKind kind, float top, float height)
        {
            Kind = kind;
            Top = top;
            Height = height;
        }
    }

    /**
     * <summary>Lays the web out: a band per kind, in the kinds' order; within each, columns by how many quests lead to
     * a quest and rows in the order of the quests before them. Pinned quests stay where they were put. Loops are cut,
     * so this always finishes.</summary>
     */
    public static class WebLayout
    {
        #region Public API

        /** <summary>How wide a quest node is.</summary> */
        public const float NodeWidth = 196f;

        /** <summary>Space from one column's left edge to the next.</summary> */
        public const float ColumnWidth = 320f;

        /** <summary>Space from one row's top to the next.</summary> */
        public const float RowHeight = 74f;

        /** <summary>Space above a band's first row, for its name.</summary> */
        public const float BandHeader = 34f;

        /** <summary>Space below a band's last row.</summary> */
        public const float BandFooter = 14f;

        /** <summary>Where the first column starts, leaving room for band names.</summary> */
        public const float Left = 120f;

        /** <summary>The position of every quest, and the bands.</summary> */
        public static (Dictionary<QuestDefinition, Vector2> positions, List<WebBand> bands) Arrange(IReadOnlyCollection<QuestDefinition> quests, IReadOnlyList<WebLink> links)
        {
            var into = quests.ToDictionary(quest => quest, _ => new List<QuestDefinition>());
            foreach (var link in links)
                if (into.ContainsKey(link.To) && into.ContainsKey(link.From)) into[link.To].Add(link.From);

            var depth = new Dictionary<QuestDefinition, int>();
            var visiting = new HashSet<QuestDefinition>();
            int Depth(QuestDefinition quest)
            {
                if (depth.TryGetValue(quest, out var known)) return known;
                if (!visiting.Add(quest)) return 0;
                var value = into[quest].Count == 0 ? 0 : into[quest].Max(Depth) + 1;
                visiting.Remove(quest);
                return depth[quest] = value;
            }

            var positions = new Dictionary<QuestDefinition, Vector2>();
            var bands = new List<WebBand>();
            var rowOf = new Dictionary<QuestDefinition, float>();
            var top = 0f;
            foreach (var band in quests.GroupBy(quest => quest.Kind).OrderBy(group => group.Key == null ? int.MaxValue : group.Key.Order).ThenBy(group => group.Key == null ? "" : group.Key.name))
            {
                var rows = 1;
                foreach (var column in band.GroupBy(Depth).OrderBy(group => group.Key))
                {
                    var row = 0;
                    var ordered = column.OrderBy(quest => into[quest].Count == 0 ? 0f : into[quest].Average(before => rowOf.TryGetValue(before, out var y) ? y : 0f))
                        .ThenBy(quest => quest.Order).ThenBy(QuestStates.TitleOf);
                    foreach (var quest in ordered)
                    {
                        var at = new Vector2(Left + column.Key * ColumnWidth, top + BandHeader + row * RowHeight);
                        positions[quest] = quest.WebPinned ? quest.WebPosition : at;
                        rowOf[quest] = positions[quest].y;
                        row++;
                    }
                    rows = Mathf.Max(rows, row);
                }
                var height = BandHeader + rows * RowHeight + BandFooter;
                foreach (var quest in band.Where(quest => quest.WebPinned))
                    height = Mathf.Max(height, quest.WebPosition.y - top + RowHeight + BandFooter);
                bands.Add(new WebBand(band.Key, top, height));
                top += height;
            }
            return (positions, bands);
        }

        #endregion
    }
}
