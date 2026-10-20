using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Every change the quest screen makes to a quest's steps. Each is one named Undo step, and nothing here opens
     * a dialog. The first change that places a step in a quest never arranged by hand also saves where every other step
     * was drawn, so nothing jumps.</summary>
     */
    public static class StepEdits
    {
        #region Public API

        /**
         * <summary>Adds a step at a position. Given a step to come after, it is linked after it; otherwise it starts after
         * the step above.</summary>
         * <param name="arrangement">Where every step is drawn now, saved too when the quest was never arranged.</param>
         */
        public static QuestStep AddStep(QuestDefinition quest, Vector2 position, string afterStepId = null, IReadOnlyDictionary<string, Vector2> arrangement = null)
        {
            var step = new QuestStep(QuestDefinition.CreateId(), Array.Empty<QuestObjective>(), new StepSettings
            {
                Start = afterStepId == null ? StepStart.AfterStepAbove : StepStart.AfterLinkedSteps,
                After = afterStepId == null ? null : new[] { afterStepId },
                HiddenUntilReached = true,
            });
            step.SetGraphPosition(GraphMetrics.Snap(position));
            EditScope.Run("Add Step", new Object[] { quest }, () =>
            {
                Arrange(quest, arrangement);
                quest.InsertStep(quest.Steps.Count, step);
            });
            return step;
        }

        /**
         * <summary>Deletes steps. Each step that came after one keeps its place by coming after what that step came
         * after, written out as links in their original order. Conditions anywhere that read a deleted step are
         * reported, not changed.</summary>
         */
        public static DeleteResult DeleteSteps(QuestDefinition quest, IReadOnlyCollection<string> stepIds, QuestIndex index = null)
        {
            var doomed = new HashSet<string>(stepIds.Where(id => quest.FindStep(id) != null));
            var rewired = new List<QuestStep>();
            var readers = index == null
                ? new List<QuestLink>()
                : index.ReadersOf(quest).Where(link => link.ReadStepId != null && doomed.Contains(link.ReadStepId) && !doomed.Contains(link.ReaderStepId ?? "")).ToList();
            if (doomed.Count == 0) return new DeleteResult(rewired, readers);

            var record = new QuestRecord(quest);
            var remaining = quest.Steps.Where(step => step != null && !doomed.Contains(step.Id)).ToList();
            var plan = new List<(QuestStep step, StepStart start, List<string> after)>();
            foreach (var stepRecord in record.Steps)
            {
                var step = stepRecord.Definition;
                if (doomed.Contains(step.Id) || WaitsWithoutFollowing(step) || !stepRecord.Follows.Any(before => doomed.Contains(before.Definition.Id))) continue;
                var keep = KeptPredecessors(stepRecord, doomed);
                if (step.Start == StepStart.AfterStepAbove)
                {
                    var at = remaining.IndexOf(step);
                    var newAbove = at > 0 ? remaining[at - 1].Id : null;
                    if ((keep.Count == 1 && keep[0] == newAbove) || (keep.Count == 0 && newAbove == null)) continue;
                }
                plan.Add((step, keep.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, keep));
            }

            EditScope.Run("Delete Steps", new Object[] { quest }, () =>
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
                    if (!WaitsWithoutFollowing(dependant.Definition)) open.Push(dependant);
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
            if (to.Start == StepStart.AfterLinkedSteps && to.After.Contains(fromId)) return true;
            var after = new List<string>();
            if (to.Start == StepStart.AfterLinkedSteps) after.AddRange(to.After);
            else if (to.Start == StepStart.AfterStepAbove && to.UnlockWhen == null)
            {
                var above = StepAbove(quest, to);
                if (above != null) after.Add(above.Id);
            }
            if (!after.Contains(fromId)) after.Add(fromId);
            EditScope.Run("Link Steps", new Object[] { quest }, () => to.SetStart(StepStart.AfterLinkedSteps, after, to.AfterRule));
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
                EditScope.Run("Unlink Steps", new Object[] { quest }, () => to.SetStart(StepStart.WithQuest, null, to.AfterRule));
                return;
            }
            if (to.Start != StepStart.AfterLinkedSteps || !to.After.Contains(fromId)) return;
            var after = to.After.Where(id => id != fromId).ToList();
            EditScope.Run("Unlink Steps", new Object[] { quest }, () => to.SetStart(after.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, after, to.AfterRule));
        }

        /** <summary>Sets whether all of a step's links, or any one, must be done.</summary> */
        public static void SetJoin(QuestDefinition quest, string stepId, StepJoin rule)
        {
            var step = quest.FindStep(stepId);
            if (step == null || step.AfterRule == rule) return;
            EditScope.Run("Set Link Rule", new Object[] { quest }, () => step.SetStart(step.Start, step.After, rule));
        }

        /** <summary>Moves steps, as one Undo step. In a quest never arranged, give every step's position so all are saved.</summary> */
        public static void MoveSteps(QuestDefinition quest, IReadOnlyDictionary<string, Vector2> positions)
        {
            var moves = positions.Select(pair => (step: quest.FindStep(pair.Key), position: GraphMetrics.Snap(pair.Value)))
                .Where(move => move.step != null && (!quest.GraphArranged || move.step.GraphPosition != move.position)).ToList();
            if (moves.Count == 0) return;
            EditScope.Run(moves.Count == 1 ? "Move Step" : "Move Steps", new Object[] { quest }, () =>
            {
                foreach (var (step, position) in moves)
                    step.SetGraphPosition(position);
                quest.SetGraphArranged(true);
            });
        }

        /**
         * <summary>Copies steps and returns the text to put on the system clipboard. Each copy's "after the step above"
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
                var follows = record.Find(copy.Id)?.Follows ?? Array.Empty<StepRecord>();
                copy.SetStart(follows.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, follows.Select(before => before.Definition.Id), copy.AfterRule);
            }
            return StepClipboard.Hold(copies);
        }

        /** <summary>True when the text names the steps copied last.</summary> */
        public static bool CanPaste(string text) => StepClipboard.Holds(text);

        /**
         * <summary>Pastes copied steps into a quest with their top-left at a position. Every step and objective gets a
         * fresh id; links among the pasted steps, and step conditions pointing at them, follow the new ids; links to
         * steps that weren't copied are dropped.</summary>
         * <param name="arrangement">Where every step is drawn now, saved too when the quest was never arranged.</param>
         */
        public static PasteResult Paste(QuestDefinition quest, string text, Vector2 at, IReadOnlyDictionary<string, Vector2> arrangement = null)
        {
            var holder = StepClipboard.Take(text);
            if (holder == null) return new PasteResult(Array.Empty<string>(), 0);
            try
            {
                var steps = holder.Steps.Where(step => step != null).ToList();
                if (steps.Count == 0) return new PasteResult(Array.Empty<string>(), 0);

                var ids = new Dictionary<string, string>();
                foreach (var step in steps)
                    if (step.Id != null && !ids.ContainsKey(step.Id)) ids[step.Id] = QuestDefinition.CreateId();
                var dropped = 0;
                var origin = new Vector2(steps.Min(step => step.GraphPosition.x), steps.Min(step => step.GraphPosition.y));
                foreach (var step in steps)
                {
                    if (step.Start == StepStart.AfterLinkedSteps)
                    {
                        var kept = step.After.Where(ids.ContainsKey).Select(id => ids[id]).Distinct().ToList();
                        dropped += step.After.Count - kept.Count;
                        step.SetStart(kept.Count == 0 ? StepStart.WithQuest : StepStart.AfterLinkedSteps, kept, step.AfterRule);
                    }
                    foreach (var condition in ConditionsOf(step))
                        ConditionTree.VisitLeaves(condition, (leaf, _) => Retarget(leaf, holder.Source, quest, ids));
                    step.AssignId(step.Id != null && ids.TryGetValue(step.Id, out var newId) ? newId : QuestDefinition.CreateId());
                    foreach (var objective in step.Objectives)
                        objective?.AssignId(QuestDefinition.CreateId());
                    step.SetGraphPosition(GraphMetrics.Snap(at + step.GraphPosition - origin));
                }
                EditScope.Run(steps.Count == 1 ? "Paste Step" : "Paste Steps", new Object[] { quest }, () =>
                {
                    Arrange(quest, arrangement);
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

        #region Helpers

        /** <summary>True when a step's unlock condition replaces waiting for the step above, so it follows that step without waiting.</summary> */
        private static bool WaitsWithoutFollowing(QuestStep step) => step.Start == StepStart.AfterStepAbove && step.UnlockWhen != null;

        /** <summary>Saves where every step is drawn, once, for a quest never arranged by hand.</summary> */
        private static void Arrange(QuestDefinition quest, IReadOnlyDictionary<string, Vector2> arrangement)
        {
            if (quest.GraphArranged || arrangement == null) return;
            foreach (var step in quest.Steps)
                if (step != null && arrangement.TryGetValue(step.Id, out var position)) step.SetGraphPosition(GraphMetrics.Snap(position));
            quest.SetGraphArranged(true);
        }

        /** <summary>Points a condition that read one of the copied steps at its copy.</summary> */
        private static void Retarget(ICondition<IFacts> leaf, QuestDefinition source, QuestDefinition target, Dictionary<string, string> ids)
        {
            var reference = QuestReferences.Of(leaf);
            var retargetable = QuestReferences.RetargetableOf(leaf);
            if (reference == null || retargetable == null || reference.Quest != source || reference.StepId == null) return;
            if (ids.TryGetValue(reference.StepId, out var newId)) retargetable.Retarget(target, newId);
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

        /** <summary>What a step comes after once deleted steps are gone: each deleted step's own predecessors in its place, in order.</summary> */
        private static List<string> KeptPredecessors(StepRecord step, HashSet<string> doomed)
        {
            var kept = new List<string>();
            var seen = new HashSet<StepRecord>();
            void Walk(StepRecord current)
            {
                foreach (var before in current.Follows)
                {
                    if (!seen.Add(before)) continue;
                    if (doomed.Contains(before.Definition.Id)) Walk(before);
                    else kept.Add(before.Definition.Id);
                }
            }
            Walk(step);
            return kept;
        }

        private static IEnumerable<ICondition<IFacts>> ConditionsOf(QuestStep step)
        {
            if (step.UnlockWhen != null) yield return step.UnlockWhen;
            if (step.FailWhen != null) yield return step.FailWhen;
            var held = new List<ICondition<IFacts>>();
            foreach (var objective in step.Objectives)
                if (objective?.Tracker != null) Trackers.ConditionHolderOf(objective.Tracker)?.CollectConditions(held);
            foreach (var condition in held)
                yield return condition;
        }

        #endregion
    }
}
