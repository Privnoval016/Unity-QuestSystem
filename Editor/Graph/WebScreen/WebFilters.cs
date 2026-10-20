using System;
using System.Collections.Generic;
using System.Linq;

namespace QuestSystem.EditorTools
{
    /** <summary>What the web shows: kinds, properties and a search, all optional. Nothing set shows every quest.</summary> */
    public sealed class WebFilters
    {
        #region Properties

        /** <summary>The kinds shown; none means every kind.</summary> */
        public HashSet<QuestKind> Kinds { get; } = new HashSet<QuestKind>();

        /** <summary>True to show only repeatable quests.</summary> */
        public bool Repeatable { get; set; }

        /** <summary>True to show only quests players see before they are given.</summary> */
        public bool ShownEarly { get; set; }

        /** <summary>True to show only quests with problems.</summary> */
        public bool Problems { get; set; }

        /** <summary>Text to search titles, step text and step ids for; empty for none.</summary> */
        public string Search { get; set; } = "";

        /** <summary>True when nothing filters the web.</summary> */
        public bool IsEmpty => Kinds.Count == 0 && !Repeatable && !ShownEarly && !Problems;

        #endregion

        #region Public API

        /** <summary>True when the kind and property filters let a quest show.</summary> */
        public bool Shows(QuestDefinition quest, QuestSummary summary) =>
            (Kinds.Count == 0 || (quest.Kind != null && Kinds.Contains(quest.Kind)))
            && (!Repeatable || quest.Repeatable)
            && (!ShownEarly || !quest.HiddenUntilGiven)
            && (!Problems || (summary != null && summary.Errors + summary.Warnings > 0));

        /** <summary>True when a search is set and the quest's title, a step's text or a step's id matches it.</summary> */
        public bool Matches(QuestDefinition quest)
        {
            var term = Search?.Trim() ?? "";
            if (term.Length == 0) return false;
            return QuestStates.TitleOf(quest).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                || quest.Steps.Any(step => step != null && ((step.Text ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || step.Id == term));
        }

        /** <summary>True when a search is set.</summary> */
        public bool Searching => !string.IsNullOrWhiteSpace(Search);

        /** <summary>Clears every filter and the search.</summary> */
        public void Clear()
        {
            Kinds.Clear();
            Repeatable = false;
            ShownEarly = false;
            Problems = false;
            Search = "";
        }

        #endregion
    }
}
