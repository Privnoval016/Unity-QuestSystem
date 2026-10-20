using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestSystem.EditorTools
{
    /** <summary>Every change the quest web makes to quests. Each is one named Undo step, and nothing here opens a dialog.</summary> */
    public static class QuestEdits
    {
        #region Public API

        /** <summary>Moves quests in the web and pins them there, as one Undo step.</summary> */
        public static void MoveQuests(IReadOnlyDictionary<QuestDefinition, Vector2> positions)
        {
            var moves = positions.Where(pair => pair.Key != null).Select(pair => (quest: pair.Key, position: GraphMetrics.Snap(pair.Value)))
                .Where(move => !move.quest.WebPinned || move.quest.WebPosition != move.position).ToList();
            if (moves.Count == 0) return;
            EditScope.Run(moves.Count == 1 ? "Move Quest" : "Move Quests", moves.Select(move => (Object)move.quest).ToArray(), () =>
            {
                foreach (var (quest, position) in moves)
                    quest.SetWebPlacement(position, true);
            });
        }

        /** <summary>Lets the web lay quests out again.</summary> */
        public static void Unpin(IEnumerable<QuestDefinition> quests)
        {
            var pinned = quests.Where(quest => quest != null && quest.WebPinned).ToList();
            if (pinned.Count == 0) return;
            EditScope.Run("Unpin Quests", pinned.Cast<Object>().ToArray(), () =>
            {
                foreach (var quest in pinned)
                    quest.SetWebPlacement(Vector2.zero, false);
            });
        }

        /** <summary>True when making <paramref name="target" /> need <paramref name="needs" /> would make quests need each other.</summary> */
        public static bool WouldLoop(QuestIndex index, QuestDefinition target, QuestDefinition needs)
        {
            if (target == needs) return true;
            var seen = new HashSet<QuestDefinition>();
            var open = new Stack<QuestDefinition>();
            open.Push(needs);
            while (open.Count > 0)
            {
                var quest = open.Pop();
                if (quest == target) return true;
                if (!seen.Add(quest)) continue;
                var summary = index.SummaryOf(quest);
                if (summary == null) continue;
                foreach (var link in summary.Reads)
                    if (LinkSlots.GatesAvailability(link.Slot) && link.Use.AsItself && link.Use.Required && link.Read != quest) open.Push(link.Read);
            }
            return false;
        }

        /** <summary>True when a quest already can't be given until another is done.</summary> */
        public static bool AlreadyNeeds(QuestDefinition target, QuestDefinition needs)
        {
            var found = new List<FoundReference>();
            QuestReferences.Collect(target.AvailableWhen, found);
            var done = QuestStates.NameOf(QuestState.Done);
            return found.Any(reference => reference.Reference.Quest == needs && reference.Reference.StepId == null
                && reference.Use.AsItself && reference.Use.Required && reference.Reference.StateName == done);
        }

        /** <summary>What a quest's "available when" would read after <see cref="AddAvailability" />, without changing it.</summary> */
        public static string PreviewAvailability(QuestDefinition target, QuestDefinition needs) =>
            ConditionTree.Summarize(AlreadyNeeds(target, needs) ? target.AvailableWhen : Merged(target.AvailableWhen, new QuestStateCondition(needs), false));

        /**
         * <summary>Makes a quest available only once another is done: added to an existing And, or joined with the
         * existing condition in a new And. The old condition is never rewritten inside.</summary>
         * <returns>False when it already needed it, or either quest is missing.</returns>
         */
        public static bool AddAvailability(QuestDefinition target, QuestDefinition needs)
        {
            if (target == null || needs == null || AlreadyNeeds(target, needs)) return false;
            EditScope.Run("Link Quests", new Object[] { target }, () => target.SetAvailableWhen(Merged(target.AvailableWhen, new QuestStateCondition(needs), true)));
            return true;
        }

        /** <summary>Sets several quests' kind, as one Undo step.</summary> */
        public static void SetKind(IEnumerable<QuestDefinition> quests, QuestKind kind) => ChangeEach("Set Kind", quests, quest => quest.SetKind(kind));

        /** <summary>Sets several quests' giver, as one Undo step; an asset without a display name is refused.</summary> */
        public static bool SetGiver(IEnumerable<QuestDefinition> quests, Object giver) =>
            DisplayNames.IsNamedOrEmpty(giver) && ChangeEach("Set Giver", quests, quest => quest.SetGiver(giver));

        /** <summary>Sets several quests' place, as one Undo step; an asset without a display name is refused.</summary> */
        public static bool SetPlace(IEnumerable<QuestDefinition> quests, Object place) =>
            DisplayNames.IsNamedOrEmpty(place) && ChangeEach("Set Place", quests, quest => quest.SetPlace(place));

        /** <summary>Creates a quest asset in a folder, pinned at a web position. Undo removes it.</summary> */
        public static QuestDefinition CreateQuest(string folder, string title, QuestKind kind, Vector2 position)
        {
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            quest.SetTitle(title);
            quest.SetKind(kind);
            quest.SetWebPlacement(GraphMetrics.Snap(position), true);
            var name = new string((title ?? "Quest").Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();
            AssetDatabase.CreateAsset(quest, AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/')}/{(name.Length == 0 ? "Quest" : name)}.asset"));
            Undo.RegisterCreatedObjectUndo(quest, "Create Quest");
            return quest;
        }

        #endregion

        #region Helpers

        private static bool ChangeEach(string name, IEnumerable<QuestDefinition> quests, Action<QuestDefinition> change)
        {
            var list = quests.Where(quest => quest != null).ToList();
            if (list.Count == 0) return false;
            EditScope.Run(name, list.Cast<Object>().ToArray(), () =>
            {
                foreach (var quest in list)
                    change(quest);
            });
            return true;
        }

        /** <summary>An existing condition and a new one, both required: added to an And, or joined in a new one.</summary> */
        private static ICondition<IFacts> Merged(ICondition<IFacts> existing, ICondition<IFacts> added, bool inPlace)
        {
            if (existing == null) return added;
            var and = AndOf(existing);
            if (and == null) return new AndCondition<IFacts> { children = { existing, added } };
            if (inPlace)
            {
                and.children.Add(added);
                return and;
            }
            var preview = new AndCondition<IFacts>();
            preview.children.AddRange(and.children);
            preview.children.Add(added);
            return preview;
        }

        /** <summary>The condition as an And this tool can add to; null when it isn't one.</summary> */
        private static AndCondition<IFacts> AndOf(ICondition<IFacts> condition) => condition as AndCondition<IFacts>;

        #endregion
    }
}
