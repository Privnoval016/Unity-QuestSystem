# Changelog

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
- 44 tests: v1's bugs as regressions, plus a scale test.
