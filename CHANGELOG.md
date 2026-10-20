# Changelog

## Unreleased

- Step links: a step starts after the step above (as before), after its linked steps (all or any), or with the
  quest. An unlock condition is a further check on links. Saves are unchanged.
- With no step marked to end the quest, every step nothing follows ends it; a plain list still ends at its last step.
- Validation of links: missing, self and repeated links, steps waiting on each other, steps that can never start.
- Steps and quests keep where the quest graph draws them (editor data the game never reads).
- `IQuestReference` and `QuestReferences`: tools find which quests and steps a condition reads. Trackers hand over
  the conditions they hold. Conditions and trackers summarize themselves in one line.
- The validator finds loops through step conditions as well as links; an Or offering another way isn't a loop.
- Editor groundwork for the quest graph: a quest index kept current one asset at a time, and every graph edit
  (add, link, unlink, delete with rewiring, copy and paste between quests, web links, bulk edits) as one Undo step.

## 2.0.0 (2026-10-19)

A rewrite. Version 1 is kept on the `v1` branch and the `v1.0.0` tag.

- Quests made of steps and objectives, with plain states (nothing is consumed), failure, repeats, hidden quests
  and steps, optional objectives, branching steps.
- Progress from signals (`QuestSignal`, raised by code or `QuestTrigger`) and from state (`FlagTracker`,
  `ConditionTracker`). Checks are change-driven, with no polling.
- Story flags. Conditions on Unity-Extensions' shared facts, the same ones the game uses.
- `QuestService`: no statics; events on the event bus; rewards through `IQuestReward`.
- Saving through `QuestSystem.Saving` (Unity-SaveSystem).
- `QuestReaction` re-applies scene state on scene and save loads.
- Validation, with a build check; a step picker.
- Signal payloads: `Raise(signal, amount, payload)`, filtered by a signal tracker's **only when** condition.
- Trackers declare what they can do (`ISignalCounter`, `IStateProgress`); a signal reaches only its counters.
- Quest order (given and ended, saved), an order within a kind, `IDisplayNamed` givers and places,
  `RewardDescription`, objective progress in `IQuestFacts`, events that name their service.
- Hardened: one signal never completes a quest twice or flips an ended one; a throwing condition or runaway
  conditions can't stop the rest; restores replace everything, keep damaged values in range and complete a quest
  whose ending step is done; failed repeatable quests can be given again; a disposed service ignores changes.
- Triggers count bodies, not colliders; reactions survive turning off their own object.
- 63 tests: v1's bugs as regressions, edge cases, plus a scale test.
