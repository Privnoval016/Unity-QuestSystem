using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestSystem.EditorTools
{
    /** <summary>What deleting steps changed: the steps rewired to keep their order, and the conditions elsewhere that read a deleted step.</summary> */
    public readonly struct DeleteResult
    {
        /** <summary>The steps whose links were rewired.</summary> */
        public IReadOnlyList<QuestStep> Rewired { get; }

        /** <summary>The conditions that read a deleted step, in this quest or others.</summary> */
        public IReadOnlyList<QuestLink> Readers { get; }

        /** <summary>Makes a result.</summary> */
        public DeleteResult(IReadOnlyList<QuestStep> rewired, IReadOnlyList<QuestLink> readers)
        {
            Rewired = rewired;
            Readers = readers;
        }
    }

    /** <summary>What a paste added: the new steps' ids, and how many links to steps that weren't copied were dropped.</summary> */
    public readonly struct PasteResult
    {
        /** <summary>The ids of the pasted steps.</summary> */
        public IReadOnlyList<string> StepIds { get; }

        /** <summary>How many links pointed outside the copied steps and were dropped.</summary> */
        public int DroppedLinks { get; }

        /** <summary>Makes a result.</summary> */
        public PasteResult(IReadOnlyList<string> stepIds, int droppedLinks)
        {
            StepIds = stepIds;
            DroppedLinks = droppedLinks;
        }
    }

    /**
     * <summary>Every change the quest graph makes to quests. Each is one named Undo step over every asset it touches,
     * and nothing here opens a dialog. Positions snap to a 10-unit grid so moves make small, stable diffs.</summary>
     */
    public static class GraphEdits
    {
        #region Private Fields

        /** <summary>Positions snap to multiples of this.</summary> */
        private const float Grid = 10f;

        /** <summary>What the system clipboard holds while steps are copied: a token naming the copy kept here.</summary> */
        private const string ClipboardPrefix = "QuestSteps:";

        /** <summary>The copied steps, deep copies kept in memory so references to unsaved objects survive; lost on a domain reload.</summary> */
        private static StepClipboard _clipboard;

        /** <summary>The token naming the copy in <see cref="_clipboard" />.</summary> */
        private static string _clipboardToken;

        #endregion

        #region Public API: Steps

        /** <summary>Adds a step at a position. Given a step to come after, it is linked after it; otherwise it starts after the step above.</summary> */
        public static QuestStep AddStep(QuestDefinition quest, Vector2 position, string afterStepId = null)
        {
            var step = new QuestStep(QuestDefinition.CreateId(), Array.Empty<QuestObjective>(), new StepSettings
            {
                Start = afterStepId == null ? StepStart.AfterStepAbove : StepStart.AfterLinkedSteps,
                After = afterStepId == null ? null : new[] { afterStepId },
                HiddenUntilReached = true,
            });
            step.SetGraphPosition(Snap(position));
            Change("Add Step", new Object[] { quest }, () => quest.InsertStep(quest.Steps.Count, step));
            return step;
        }

        /**
         * <summary>Deletes steps. Each step that came after one keeps its place by coming after what that step came
         * after, written out as links. Conditions anywhere that read a deleted step are reported, not changed.</summary>
         */
        public static DeleteResult DeleteSteps(QuestDefinition quest, IReadOnlyCollection<string> stepIds, QuestIndex index = null)
        {
            var doomed = new HashSet<string>(stepIds.Where(id => quest.FindStep(id) != null));
            var rewired = new List<QuestStep>();
            var readers = index == null ? new List<QuestLink>() : index.ReadersOf(quest).Where(link => link.ReadStepId != null && doomed.Contains(link.ReadStepId) && !doomed.Contains(link.ReaderStepId ?? "")).ToList();
            if (doomed.Count == 0) return new DeleteResult(rewired, readers);

            var record = new QuestRecord(quest);
            var plan = new List<(QuestStep step, StepStart start, List<string> after)>();
            var remaining = quest.Steps.Where(step => step != null && !doomed.Contains(step.Id)).ToList();
            foreach (var stepRecord in record.Steps)
            {
                var step = stepRecord.Definition;
                if (doomed.Contains(step.Id) || !stepRecord.Follows.Any(f => doomed.Contains(f.Definition.Id))) continue;
                if (step.Start == StepStart.AfterStepAbove && step.UnlockWhen != null) continue;
                var keep = KeptPredecessors(stepRecord, doomed);
                if (step.Start == StepStart.AfterStepAbove)
                {
                    var at = remaining.IndexOf(step);
                    var newAbove = at > 0 ? remaining[at - 1].Id : null;
                    if ((keep.Count == 1 && keep[0] == newAbove) || (keep.Count == 0 && newAbove == null)) continue;
                }
                plan.Add((step, keep.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, keep));
            }

            Change("Delete Steps", new Object[] { quest }, () =>
            {
                foreach (var (step, start, after) in plan)
                {
                    step.SetStart(start, after, step.AfterRule);
                    rewired.Add(step);
                }
                foreach (var id in doomed)
                    quest.RemoveStep(quest.FindStep(id));
            });
            return new DeleteResult(rewired, readers);
        }

        /** <summary>True when linking would make steps wait on each other: <paramref name="toId" /> already leads to <paramref name="fromId" />.</summary> */
        public static bool WouldLoop(QuestDefinition quest, string fromId, string toId)
        {
            if (fromId == toId) return true;
            var record = new QuestRecord(quest);
            var start = record.Find(toId);
            var target = record.Find(fromId);
            if (start == null || target == null) return false;
            var seen = new HashSet<StepRecord>();
            var open = new Stack<StepRecord>();
            open.Push(start);
            while (open.Count > 0)
            {
                var step = open.Pop();
                if (step == target) return true;
                if (!seen.Add(step)) continue;
                foreach (var dependant in step.Dependants)
                    open.Push(dependant);
            }
            return false;
        }

        /**
         * <summary>Makes a step come after another. A step that started after the step above keeps that step as an
         * explicit link, so drawing a wire never silently drops one.</summary>
         * <returns>False when the steps are missing or the link would make them wait on each other.</returns>
         */
        public static bool Link(QuestDefinition quest, string fromId, string toId)
        {
            var to = quest.FindStep(toId);
            if (to == null || quest.FindStep(fromId) == null || WouldLoop(quest, fromId, toId)) return false;
            var after = new List<string>();
            if (to.Start == StepStart.AfterLinkedSteps) after.AddRange(to.After);
            else if (to.Start == StepStart.AfterStepAbove && to.UnlockWhen == null)
            {
                var above = StepAbove(quest, to);
                if (above != null) after.Add(above.Id);
            }
            if (after.Contains(fromId) && to.Start == StepStart.AfterLinkedSteps) return true;
            if (!after.Contains(fromId)) after.Add(fromId);
            Change("Link Steps", new Object[] { quest }, () => to.SetStart(StepStart.AfterLinkedSteps, after, to.AfterRule));
            return true;
        }

        /** <summary>Removes a link, the implicit one to the step above included. A step left with no links starts with the quest.</summary> */
        public static void Unlink(QuestDefinition quest, string fromId, string toId)
        {
            var to = quest.FindStep(toId);
            if (to == null) return;
            if (to.Start == StepStart.AfterStepAbove)
            {
                if (StepAbove(quest, to)?.Id != fromId) return;
                Change("Unlink Steps", new Object[] { quest }, () => to.SetStart(StepStart.WithQuest, null, to.AfterRule));
                return;
            }
            if (to.Start != StepStart.AfterLinkedSteps || !to.After.Contains(fromId)) return;
            var after = to.After.Where(id => id != fromId).ToList();
            Change("Unlink Steps", new Object[] { quest }, () => to.SetStart(after.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, after, to.AfterRule));
        }

        /** <summary>Sets whether all of a step's links, or any one, must be done.</summary> */
        public static void SetJoin(QuestDefinition quest, string stepId, StepJoin rule)
        {
            var step = quest.FindStep(stepId);
            if (step == null || step.AfterRule == rule) return;
            Change("Set Link Rule", new Object[] { quest }, () => step.SetStart(step.Start, step.After, rule));
        }

        /** <summary>Moves steps, as one Undo step.</summary> */
        public static void MoveSteps(QuestDefinition quest, IReadOnlyDictionary<string, Vector2> positions)
        {
            var moves = positions.Select(pair => (step: quest.FindStep(pair.Key), position: Snap(pair.Value))).Where(move => move.step != null && move.step.GraphPosition != move.position).ToList();
            if (moves.Count == 0) return;
            Change(moves.Count == 1 ? "Move Step" : "Move Steps", new Object[] { quest }, () =>
            {
                foreach (var (step, position) in moves)
                    step.SetGraphPosition(position);
            });
        }

        /**
         * <summary>Copies steps, returning the text to put on the system clipboard. Each copy's "after the step above"
         * becomes an explicit link, so a paste anywhere keeps the order it had.</summary>
         */
        public static string Copy(QuestDefinition quest, IReadOnlyCollection<string> stepIds)
        {
            var record = new QuestRecord(quest);
            var holder = ScriptableObject.CreateInstance<StepClipboard>();
            StepClipboard copies;
            try
            {
                holder.Source = quest;
                holder.Steps.AddRange(quest.Steps.Where(step => step != null && stepIds.Contains(step.Id)));
                copies = Object.Instantiate(holder);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
            foreach (var copy in copies.Steps)
            {
                if (copy.Start != StepStart.AfterStepAbove || copy.UnlockWhen != null) continue;
                var follows = record.Find(copy.Id).Follows;
                copy.SetStart(follows.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, follows.Select(f => f.Definition.Id), copy.AfterRule);
            }
            copies.hideFlags = HideFlags.HideAndDontSave;
            if (_clipboard != null) Object.DestroyImmediate(_clipboard);
            _clipboard = copies;
            _clipboardToken = ClipboardPrefix + Guid.NewGuid().ToString("N");
            return _clipboardToken;
        }

        /** <summary>True when the text names the steps copied last.</summary> */
        public static bool CanPaste(string text) => _clipboard != null && text != null && text == _clipboardToken;

        /**
         * <summary>Pastes copied steps into a quest with their top-left at a position. Every step and objective gets a
         * fresh id; links among the pasted steps, and step conditions pointing at them, follow the new ids; links to
         * steps that weren't copied are dropped.</summary>
         */
        public static PasteResult Paste(QuestDefinition quest, string text, Vector2 at)
        {
            if (!CanPaste(text)) return new PasteResult(Array.Empty<string>(), 0);
            var holder = Object.Instantiate(_clipboard);
            try
            {
                var steps = holder.Steps.Where(step => step != null).ToList();
                if (steps.Count == 0) return new PasteResult(Array.Empty<string>(), 0);

                var ids = steps.ToDictionary(step => step.Id, _ => QuestDefinition.CreateId());
                var dropped = 0;
                var origin = new Vector2(steps.Min(step => step.GraphPosition.x), steps.Min(step => step.GraphPosition.y));
                foreach (var step in steps)
                {
                    if (step.Start == StepStart.AfterLinkedSteps)
                    {
                        var kept = step.After.Where(ids.ContainsKey).Select(id => ids[id]).ToList();
                        dropped += step.After.Count - kept.Count;
                        step.SetStart(kept.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, kept, step.AfterRule);
                    }
                    foreach (var condition in ConditionsOf(step))
                        ConditionTree.VisitLeaves(condition, (leaf, _) =>
                        {
                            var stepCondition = QuestReferences.StepConditionOf(leaf);
                            if (stepCondition != null && stepCondition.Quest == holder.Source && stepCondition.StepId != null && ids.TryGetValue(stepCondition.StepId, out var newId))
                                stepCondition.Retarget(quest, newId);
                        });
                    step.AssignId(ids[step.Id]);
                    foreach (var objective in step.Objectives)
                        objective?.AssignId(QuestDefinition.CreateId());
                    step.SetGraphPosition(Snap(at + step.GraphPosition - origin));
                }
                Change(steps.Count == 1 ? "Paste Step" : "Paste Steps", new Object[] { quest }, () =>
                {
                    foreach (var step in steps)
                        quest.InsertStep(quest.Steps.Count, step);
                });
                return new PasteResult(steps.Select(step => step.Id).ToList(), dropped);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        #endregion

        #region Public API: Quests

        /** <summary>Moves quests in the web and pins them there, as one Undo step.</summary> */
        public static void MoveQuests(IReadOnlyDictionary<QuestDefinition, Vector2> positions)
        {
            var moves = positions.Where(pair => pair.Key != null).Select(pair => (quest: pair.Key, position: Snap(pair.Value)))
                .Where(move => !move.quest.WebPinned || move.quest.WebPosition != move.position).ToList();
            if (moves.Count == 0) return;
            Change(moves.Count == 1 ? "Move Quest" : "Move Quests", moves.Select(move => (Object)move.quest).ToArray(), () =>
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
            Change("Unpin Quests", pinned.Cast<Object>().ToArray(), () =>
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
                    if (link.Slot == LinkSlot.Available && link.Use.AsItself && link.Use.Required && link.Read != quest) open.Push(link.Read);
            }
            return false;
        }

        /** <summary>What a quest's "available when" would read after <see cref="AddAvailability" />, without changing it.</summary> */
        public static string PreviewAvailability(QuestDefinition target, QuestDefinition needs) => ConditionTree.Summarize(Merged(target.AvailableWhen, new QuestStateCondition(needs), false));

        /**
         * <summary>Makes a quest available only once another is done: added to an existing And, or joined with the
         * existing condition in a new And. The old condition is never rewritten inside.</summary>
         */
        public static void AddAvailability(QuestDefinition target, QuestDefinition needs)
        {
            if (target == null || needs == null) return;
            Change("Link Quests", new Object[] { target }, () => target.SetAvailableWhen(Merged(target.AvailableWhen, new QuestStateCondition(needs), true)));
        }

        /** <summary>Sets several quests' kind, as one Undo step.</summary> */
        public static void SetKind(IEnumerable<QuestDefinition> quests, QuestKind kind) => ChangeEach("Set Kind", quests, quest => quest.SetKind(kind));

        /** <summary>Sets several quests' giver, as one Undo step.</summary> */
        public static void SetGiver(IEnumerable<QuestDefinition> quests, Object giver) => ChangeEach("Set Giver", quests, quest => quest.SetGiver(giver));

        /** <summary>Sets several quests' place, as one Undo step.</summary> */
        public static void SetPlace(IEnumerable<QuestDefinition> quests, Object place) => ChangeEach("Set Place", quests, quest => quest.SetPlace(place));

        /** <summary>Creates a quest asset in a folder, pinned at a web position. Undo removes it.</summary> */
        public static QuestDefinition CreateQuest(string folder, string title, QuestKind kind, Vector2 position)
        {
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            quest.SetTitle(title);
            quest.SetKind(kind);
            quest.SetWebPlacement(Snap(position), true);
            var name = new string((title ?? "Quest").Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();
            AssetDatabase.CreateAsset(quest, AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/')}/{(name.Length == 0 ? "Quest" : name)}.asset"));
            Undo.RegisterCreatedObjectUndo(quest, "Create Quest");
            return quest;
        }

        #endregion

        #region Helpers

        private static Vector2 Snap(Vector2 position) => new Vector2(Mathf.Round(position.x / Grid) * Grid, Mathf.Round(position.y / Grid) * Grid);

        /** <summary>Runs a change as one named Undo step over the assets it touches, and marks them changed.</summary> */
        private static void Change(string name, Object[] targets, Action change)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            Undo.RecordObjects(targets, name);
            change();
            foreach (var target in targets)
                EditorUtility.SetDirty(target);
            Undo.CollapseUndoOperations(group);
        }

        private static void ChangeEach(string name, IEnumerable<QuestDefinition> quests, Action<QuestDefinition> change)
        {
            var list = quests.Where(quest => quest != null).ToList();
            if (list.Count == 0) return;
            Change(name, list.Cast<Object>().ToArray(), () =>
            {
                foreach (var quest in list)
                    change(quest);
            });
        }

        private static QuestStep StepAbove(QuestDefinition quest, QuestStep step)
        {
            QuestStep above = null;
            foreach (var candidate in quest.Steps)
            {
                if (candidate == step) return above;
                if (candidate != null) above = candidate;
            }
            return null;
        }

        /** <summary>What a step comes after once deleted steps are gone: the deleted steps' own predecessors in their place.</summary> */
        private static List<string> KeptPredecessors(StepRecord step, HashSet<string> doomed)
        {
            var kept = new List<string>();
            var seen = new HashSet<StepRecord>();
            var open = new Stack<StepRecord>(step.Follows);
            while (open.Count > 0)
            {
                var before = open.Pop();
                if (!seen.Add(before)) continue;
                if (!doomed.Contains(before.Definition.Id))
                {
                    if (!kept.Contains(before.Definition.Id)) kept.Add(before.Definition.Id);
                    continue;
                }
                foreach (var further in before.Follows)
                    open.Push(further);
            }
            return kept;
        }

        private static IEnumerable<ICondition<IFacts>> ConditionsOf(QuestStep step)
        {
            if (step.UnlockWhen != null) yield return step.UnlockWhen;
            if (step.FailWhen != null) yield return step.FailWhen;
            var held = new List<ICondition<IFacts>>();
            foreach (var objective in step.Objectives)
                objective?.Tracker?.CollectConditions(held);
            foreach (var condition in held)
                yield return condition;
        }

        /** <summary>An existing condition and a new one, both required: added to an And, or joined in a new one.</summary> */
        private static ICondition<IFacts> Merged(ICondition<IFacts> existing, ICondition<IFacts> added, bool inPlace)
        {
            if (existing == null) return added;
            var and = AndOf(existing);
            if (and != null)
            {
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
            return new AndCondition<IFacts> { children = { existing, added } };
        }

        /** <summary>The condition as an And this tool can add to; null when it isn't one.</summary> */
        private static AndCondition<IFacts> AndOf(ICondition<IFacts> condition) => condition as AndCondition<IFacts>;

        #endregion
    }
}
