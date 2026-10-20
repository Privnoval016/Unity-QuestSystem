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
| **`QuestDefinition`** (*Create > Quests > Quest*) | A quest: title, summary, kind, order within its kind, giver and place, suggested level, steps, rewards, and when it can be given or fails. Repeatable or not; hidden until given or not |
| **`QuestStep`** (inside the quest) | A stage. It starts after the step above, after its linked steps, or with the quest (below). It completes when all its required objectives are done, or any one. It can fail, which fails the quest. It can end the quest |
| **`QuestObjective`** (inside a step) | One thing a step asks for, counted by a tracker up to a target. Optional (bonus) objectives never hold a step back |
| **`StoryFlag`** (*Create > Quests > Story Flag*) | A whole number about the story, 0 when unset. Set by quests and the game, read by conditions and trackers |
| **`QuestSignal`** (*Create > Quests > Quest Signal*) | Something that happens: a wolf defeated, a place reached. Raised by game code or a `QuestTrigger` |
| **`QuestKind`** (*Create > Quests > Quest Kind*) | Main story, errand, and so on, with an order. A game lists quests by kind |

**Ids.** Steps and objectives get ids the editor assigns, unique in their quest; a duplicated step gets fresh ones.
Quests, kinds and flags get asset ids from Extensions. Saves point at ids, so renaming anything is safe.

**Givers and places** are any assets the game makes that implement `IDisplayNamed` (a character, a town). The
quest only asks for their display name.

**When a step starts** (its start rule):

- **After the step above** (the default): when the step above it is done; the first step starts with the quest. A
  step with an unlock condition starts when that holds instead, for branches.
- **After linked steps:** when all of its linked steps are done, or any one of them. The quest graph draws links as
  wires.
- **With the quest:** as soon as the quest is given, for steps that run side by side.

With linked steps or with the quest, an unlock condition is a further check: both must hold.

**Which step ends the quest:** a step marked "ends quest". With none marked, every step that no other step comes
after ends it. A step after the step above follows it, even with an unlock condition, so a plain list still ends at
its last step. The validator warns when several unmarked steps would end a quest.

**Trackers** (what counts towards an objective):

- **`SignalTracker`:** each time its signal is raised, add the amount. An optional **only when** condition filters
  which raises count, reading the signal's payload first and then the world (below).
- **`FlagTracker`:** the flag's value is the progress, so a counter flag counts and an on/off flag completes it.
- **`ConditionTracker`:** done while a condition holds. This covers another quest or step being done, or anything
  the game can check.
- **A custom objective** is a subclass of `ObjectiveTracker` implementing one or both of:
  - `ISignalCounter` to count signals: `ProgressFrom(signal, amount, context)`, plus `TryCollectSignals` so a signal
    reaches only its counters;
  - `IStateProgress` to follow state: `ProgressNow(facts)`, plus `TryCollectSubjects` to say what it reads.

  A tracker that can't say what it reads or counts still works; it is just offered every change or signal.

## Conditions and facts

Conditions are Extensions' `ICondition<IFacts>`: the same conditions the game uses for its own locks and rewards.

- **The quest service registers `IQuestFacts`** (quest and step states, completions) and **`IFlagFacts`** (flag
  values) in the game's `FactRegistry`. The game registers its own facts there too.
- **Quest conditions:** `FlagCondition` (at least, equal or below a number), `QuestStateCondition`,
  `StepStateCondition` (choose the step from a menu), combined with And / Or / Not. `IQuestFacts` also gives
  objective progress, for conditions a game writes.
- **Tools read quest links from conditions.** `QuestStateCondition` and `StepStateCondition` are `IQuestReference`s
  (the quest, the step, the state). A game's own condition on quests implements it to appear in the quest graph
  and in "read by" lists. `QuestReferences.Collect` finds them anywhere in a condition tree, and counts parts that
  can't say what they read. Conditions and trackers summarize themselves in one line (`ToString`), for the graph's
  nodes.
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

### Signal payloads

A signal can carry what it says about the moment, so one signal serves many objectives: "foe defeated" with the
foe, rather than a signal per kind of foe.

```csharp
// In the game: what the signal says, and a condition that reads it.
public interface IDefeatedFoe { FoeDefinition Foe { get; } int Level { get; } }

[Serializable]
public sealed class FoeIs : ICondition<IFacts>
{
    [SerializeField] private FoeDefinition foe;
    public bool Evaluate(IFacts facts) => facts.TryGet<IDefeatedFoe>(out var defeated) && defeated.Foe == foe;
}

// Where the foe falls:
quests.Raise(foeDefeated, 1, Facts.Of<IDefeatedFoe>(defeat));
```

A `SignalTracker` whose **only when** is `FoeIs (wolf)` then counts wolves only. The payload is read before the
world's facts, so **only when** can also mix in a flag or the time of day with And / Or. A raise with no payload
fails any condition that needs one. The payload lives only for that raise.

- **Events** go out on Extensions' event bus for the HUD and menus. Each carries the service that raised it
  (`Source`), so a tool running a second service can tell them apart:
  - `QuestGivenEvent`;
  - `StepChangedEvent` (started, done, failed);
  - `ObjectiveProgressedEvent`;
  - `QuestEndedEvent` (done or failed);
  - `FlagChangedEvent`;
  - `ActiveQuestChangedEvent`.
- **Rewards** implement `IQuestReward` in the game (items, money, a bond rank) and are granted once per completion,
  never again on a load. `Describe()` returns a `RewardDescription` (text, an optional icon and amount) for menus.
  A reward that throws is logged and the rest are still granted.
- **Order.** `GivenOrderOf` and `EndedOrderOf` tell when a quest was last given or ended, as a count of quest
  events this game, so a journal can list recent quests first. They are saved.
- **The service owns no statics.** Create it when a game starts and `Dispose` it when the game ends; a disposed
  service ignores further changes.
- **It stays up when content is wrong.** A condition that throws is logged and that quest skipped; the others run.
  Conditions that keep changing each other are stopped after 10,000 changes, with an error.
- **One signal, one moment.** A signal counts towards the steps active when it is raised. A step that the same
  signal completes or unlocks doesn't count it again, and a quest it ends doesn't end twice.

## In scenes

- **`QuestRuntime`** (*Create > Quests > Quest Runtime*) is the asset scene objects reach the running service
  through. The game binds its service to it.
- **`QuestTrigger`** raises a signal when something enters or leaves its collider. It filters by layer, and by an
  `IQuestTriggerFilter` the game writes (the player only, say). It counts bodies (a rigidbody, or the collider's
  object without one), so a character with several colliders enters and leaves once.
- **`QuestReaction`** keeps scene objects in step with quests: while its condition holds, its effects are on
  (`SetActiveEffect`, `EnableBehaviourEffect`, or the game's own `IQuestReactionEffect`). It re-applies itself when
  its scene loads, when a save loads, and when what it reads changes. So a gate a quest opened is open again after
  loading.
  - It listens from `Awake` to `OnDestroy`, so its effects may turn off its own object and later turn it back on.
  - Unity never wakes an object that starts inactive, so put the reaction on an object that starts active and point
    its effects at what should change.

## Saving

`QuestSaveSection` saves every quest's state, completions and order, its steps' states, its objectives' progress,
every set flag, and the active quest, all by id.

- **Quests and flags missing from the game** (removed content) are kept and saved again.
- **A step added after a save** starts by its rule when the save loads.
- **A quest whose ending step was already done** completes when the save loads.
- **Damaged values** are kept in range: an unknown state reads as the start, progress stays within its target.
- **Restoring replaces everything,** even on a service already in use.
- **A new game** restores an empty section, which clears everything.

## Validation

*Tools > Quests > Validate Quests* logs problems. Errors also fail a build:

- a quest with no asset id (it couldn't be saved);
- an empty step or objective slot;
- an objective with no tracker, or a tracker with nothing set;
- a step that waits on itself, or steps whose links wait on each other;
- a link to a step that doesn't exist, to itself, or twice; linked steps with no links;
- a quest with no steps.

Warnings: a missing kind or title, a step with no objectives, a step that can never start, "any" of a single link,
several unmarked steps that would each end the quest.

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
| Raising a signal | about 0.06 ms |
| Changing a flag | about 0.01 ms |
| Capture | about 3 ms |
| Restore | about 10 ms |

A signal reaches only the objectives that count it, and a change only the quests that read it.
