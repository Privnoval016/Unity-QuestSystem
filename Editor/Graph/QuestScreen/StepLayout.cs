using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>Lays a quest's steps out left to right by what they follow, for a quest never arranged by hand and for Tidy.</summary> */
    public static class StepLayout
    {
        #region Public API

        /** <summary>Space between columns, from one node's left edge to the next.</summary> */
        public const float ColumnWidth = 280f;

        /** <summary>Where the first column and row start, below the summary bar.</summary> */
        public static readonly Vector2 Origin = new Vector2(40f, 110f);

        /** <summary>Space between nodes in a column.</summary> */
        public const float RowGap = 30f;

        /** <summary>True when the quest has never been arranged, so its steps are laid out automatically.</summary> */
        public static bool NeedsLayout(QuestDefinition quest) => !quest.GraphArranged;

        /**
         * <summary>A position for every step: a column for how many steps lead to it, and rows in the order of the steps
         * before them. Loops are cut, so this always finishes.</summary>
         */
        public static Dictionary<string, Vector2> Arrange(QuestDefinition quest, Func<string, float> heightOf)
        {
            var record = new QuestRecord(quest);
            var depth = new Dictionary<StepRecord, int>();
            var visiting = new HashSet<StepRecord>();
            int Depth(StepRecord step)
            {
                if (depth.TryGetValue(step, out var known)) return known;
                if (!visiting.Add(step)) return 0;
                var value = step.Follows.Count == 0 ? 0 : step.Follows.Max(Depth) + 1;
                visiting.Remove(step);
                return depth[step] = value;
            }

            var positions = new Dictionary<string, Vector2>();
            var rowOf = new Dictionary<StepRecord, float>();
            foreach (var column in record.Steps.GroupBy(Depth).OrderBy(group => group.Key))
            {
                var y = Origin.y;
                var ordered = column.OrderBy(step => step.Follows.Count == 0 ? 0f : step.Follows.Average(before => rowOf.TryGetValue(before, out var row) ? row : 0f))
                    .ThenBy(step => IndexOf(record, step));
                foreach (var step in ordered)
                {
                    rowOf[step] = y;
                    positions[step.Definition.Id] = new Vector2(Origin.x + column.Key * ColumnWidth, y);
                    y += heightOf(step.Definition.Id) + RowGap;
                }
            }
            return positions;
        }

        #endregion

        #region Helpers

        private static int IndexOf(QuestRecord record, StepRecord step)
        {
            for (var i = 0; i < record.Steps.Count; i++)
                if (record.Steps[i] == step) return i;
            return -1;
        }

        #endregion
    }
}
