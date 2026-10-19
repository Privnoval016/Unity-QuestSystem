# Unity-QuestSystem

Quests for Unity games, authored as assets. A quest has steps, steps have objectives, and progress comes from
gameplay signals and from story state, with no per-frame polling. Quests and the game share one condition system.
Version 2; version 1 is kept on the `v1` branch and the `v1.0.0` tag.

**Requires:**

- Unity 6;
- [Unity-Extensions](https://github.com/Privnoval016/Unity-Extensions): conditions and facts, the event bus, asset ids;
- [Unity-SaveSystem](https://github.com/Privnoval016/Unity-SaveSystem), only for `Saving/`. Delete that folder if a
  project doesn't save quests.

## Assemblies

| Assembly | Folder | Holds |
|---|---|---|
| `QuestSystem` | `Runtime/` | Definitions, the quest service, trackers, conditions, events, scene triggers and reactions |
| `QuestSystem.Saving` | `Saving/` | `QuestSaveSection`, for Unity-SaveSystem |
| `QuestSystem.Editor` | `Editor/` | Validation and its build check, the step picker |
| `QuestSystem.Tests` | `Tests/` | EditMode tests |

None is auto-referenced.

## The pieces

| Asset or type | What it is |
|---|---|
| **`QuestDefinition`** (*Create > Quests > Quest*) | A quest: title, summary, kind, giver and place, suggested level, steps, rewards, and when it can be given or fails. Repeatable or not; hidden until given or not |
| **`QuestStep`** (inside the quest) | A stage. It starts when the step before it is done, or when its own condition holds (for branches). It completes when all its required objectives are done, or any one. It can fail, which fails the quest. It can end the quest |
| **`QuestObjective`** (inside a step) | One thing a step asks for, counted by a tracker up to a target. Optional (bonus) objectives never hold a step back |
| **`StoryFlag`** (*Create > Quests > Story Flag*) | A whole number about the story, 0 when unset. Set by quests and the game, read by conditions and trackers |
| **`QuestSignal`** (*Create > Quests > Quest Signal*) | Something that happens: a wolf defeated, a place reached. Raised by game code or a `QuestTrigger` |
| **`QuestKind`** (*Create > Quests > Quest Kind*) | Main story, errand, and so on, with an order. A game lists quests by kind |

**Ids.** Steps and objectives get ids the editor assigns, unique in their quest; a duplicated step gets fresh ones.
Quests, kinds and flags get asset ids from Extensions. Saves point at ids, so renaming anything is safe.

**Trackers** (what counts towards an objective):

- **`SignalTracker`:** each time its signal is raised, add the amount.
- **`FlagTracker`:** the flag's value is the progress, so a counter flag counts and an on/off flag completes it.
- **`ConditionTracker`:** done while a condition holds. This covers another quest or step being done, or anything
  the game can check.
- **A custom objective** is a subclass of `ObjectiveTracker`. Override `ProgressFrom(signal, amount)` to count
  signals, `ProgressNow(facts)` to read state, and `TryCollectSubjects` to say what it reads.

## Conditions and facts

Conditions are Extensions' `ICondition<IFacts>`: the same conditions the game uses for its own locks and rewards.

- **The quest service registers `IQuestFacts`** (quest and step states, completions) and **`IFlagFacts`** (flag
  values) in the game's `FactRegistry`. The game registers its own facts there too.
- **Quest conditions:** `FlagCondition`, `QuestStateCondition`, `StepStateCondition` (choose the step from a
  menu), combined with And / Or / Not.
- **Checks are change-driven.** Every condition says what it reads, so a change checks only the quests that read
  it. A custom condition that doesn't say is checked after every change, so it still works.

## Using it in a game

```csharp
var facts = new FactRegistry();                                // shared with the game's own conditions
var quests = new QuestService(facts, allQuests, assetCatalog); // allQuests: every QuestDefinition
questRuntime.Bind(quests);                                     // the QuestRuntime asset scene objects use
saveService.Register(new QuestSaveSection(quests));            // or quests.NewGame() without saving

quests.Give(smithQuest);              // when the smith offers it (or mark the quest "given automatically")
quests.Raise(wolfDefeated);           // when the game's own event happens
quests.SetFlag(metTheSmith, 1);
quests.SetActive(smithQuest);         // the quest the player follows
```

- **Events** go out on Extensions' event bus for the HUD and menus:
  - `QuestGivenEvent`;
  - `StepChangedEvent` (started, done, failed);
  - `ObjectiveProgressedEvent`;
  - `QuestEndedEvent` (done or failed);
  - `FlagChangedEvent`;
  - `ActiveQuestChangedEvent`.
- **Rewards** implement `IQuestReward` in the game (items, money, a bond rank) and are granted once per completion,
  never again on a load.
- **The service owns no statics.** Create it when a game starts and `Dispose` it when the game ends.

## In scenes

- **`QuestRuntime`** (*Create > Quests > Quest Runtime*) is the asset scene objects reach the running service
  through. The game binds its service to it.
- **`QuestTrigger`** raises a signal when something enters or leaves its collider. It filters by layer, and by an
  `IQuestTriggerFilter` the game writes (the player only, say).
- **`QuestReaction`** keeps scene objects in step with quests: while its condition holds, its effects are on
  (`SetActiveEffect`, `EnableBehaviourEffect`, or the game's own `IQuestReactionEffect`). It re-applies itself when
  its scene loads, when a save loads, and when what it reads changes. So a gate a quest opened is open again after
  loading.

## Saving

`QuestSaveSection` saves every quest's state, steps and objective progress, every set flag, and the active quest,
all by id.

- **Quests and flags missing from the game** (removed content) are kept and saved again.
- **A step added after a save** starts by its rule when the save loads.
- **A new game** restores an empty section, which clears everything.

## Validation

*Tools > Quests > Validate Quests* logs problems. Errors also fail a build:

- an objective with no tracker, or a tracker with nothing set;
- a step that waits on itself;
- a quest with no steps.

Warnings: a missing kind or title, a step with no objectives.

## Upgrading from v1

v2 is a rewrite. v1's objectives, outcomes, broadcasters and receivers are replaced as follows:

| v1 | v2 |
|---|---|
| Objectives and outcomes | Quests made of steps |
| Broadcasters | Signals and triggers |
| Receivers | Reactions |
| `ICondition<QuestOutcome>` | `ICondition<IFacts>` |

Projects on v1 can stay on the `v1` branch or the `v1.0.0` tag. v2 fixes v1's known bugs, and each has a test:

- outcomes depending on outcomes never updating;
- a toggled-off outcome triggering receivers;
- outcomes only listening while loaded;
- receivers using up completions;
- a failed check wiping progress.

## Scale

Measured with 500 active quests of 5 steps each and 2,000 flags (`QuestScaleTests`):

| Operation | Time |
|---|---|
| Raising a signal | about 0.7 ms |
| Changing a flag | about 0.01 ms |
| Capture | about 2 ms |
| Restore | about 12 ms |
