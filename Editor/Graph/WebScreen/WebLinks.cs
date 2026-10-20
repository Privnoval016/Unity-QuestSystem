using System.Collections.Generic;
using System.Linq;

namespace QuestSystem.EditorTools
{
    /** <summary>One wire in the web: a quest another quest reads, with what the reading asks for.</summary> */
    public sealed class WebLink
    {
        /** <summary>The quest read, where the wire starts.</summary> */
        public QuestDefinition From { get; }

        /** <summary>The quest that reads it, where the wire ends.</summary> */
        public QuestDefinition To { get; }

        /** <summary>What the reading asks for: "needs done", "fails if done", "needs step done".</summary> */
        public string Label { get; }

        /** <summary>True when the reading can fail the quest.</summary> */
        public bool Fails { get; }

        /** <summary>Makes a link.</summary> */
        public WebLink(QuestDefinition from, QuestDefinition to, string label, bool fails)
        {
            From = from;
            To = to;
            Label = label;
            Fails = fails;
        }
    }

    /** <summary>Works out the web's wires from the index: one per pair of quests, self-reads left out.</summary> */
    public static class WebLinks
    {
        #region Public API

        /** <summary>Every quest-to-quest link among the given quests, one per pair, its labels joined.</summary> */
        public static List<WebLink> Of(QuestIndex index, IReadOnlyCollection<QuestDefinition> quests)
        {
            var known = new HashSet<QuestDefinition>(quests);
            var links = new List<WebLink>();
            foreach (var reader in quests)
            {
                var summary = index.SummaryOf(reader);
                if (summary == null) continue;
                foreach (var pair in summary.Reads.Where(link => link.Read != reader && known.Contains(link.Read)).GroupBy(link => link.Read))
                {
                    var labels = pair.Select(link => link.Label).Distinct().ToList();
                    var label = labels.Count <= 2 ? string.Join(" · ", labels) : $"{labels[0]} · +{labels.Count - 1}";
                    links.Add(new WebLink(pair.Key, reader, label, pair.Any(link => LinkSlots.Fails(link.Slot))));
                }
            }
            return links;
        }

        /** <summary>True when a quest reads itself, which the web marks on the quest instead of drawing.</summary> */
        public static bool ReadsItself(QuestIndex index, QuestDefinition quest) =>
            index.SummaryOf(quest)?.Reads.Any(link => link.Read == quest && link.ReadStepId == null) ?? false;

        #endregion
    }
}
