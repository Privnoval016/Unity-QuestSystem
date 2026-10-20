using System;
using System.Collections.Generic;
using Extensions.Ids;
using Extensions.Inspector;
using Extensions.Logic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestSystem
{
    /** <summary>A quest: who gives it and where, its steps, what it rewards, and when it can be given or fails.</summary> */
    [CreateAssetMenu(menuName = QuestMenus.Create + "Quest", fileName = "Quest")]
    public sealed class QuestDefinition : ScriptableObject, IIdentifiedAsset, IConditionSubject
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor.")]
        [SerializeField] private AssetId assetId;

        [Tooltip("The quest's title, as players see it.")]
        [SerializeField] private string title;

        [Tooltip("What the quest is about, as players see it.")]
        [TextArea(2, 6)]
        [SerializeField] private string summary;

        [Tooltip("The kind of quest, which decides where it is listed.")]
        [SerializeField] private QuestKind kind;

        [Tooltip("Who gives the quest: any asset with a display name. Optional.")]
        [SerializeField] private Object giver;

        [Tooltip("Where the quest is given: any asset with a display name. Optional.")]
        [SerializeField] private Object place;

        [Tooltip("Where the quest sits among others of its kind: lower comes first.")]
        [SerializeField] private int order;

        [Tooltip("The level the quest is meant for, shown to players. 0 shows none.")]
        [Min(0)]
        [SerializeField] private int suggestedLevel;

        [Tooltip("The quest can be given again after it is done.")]
        [SerializeField] private bool repeatable;

        [Tooltip("Players don't see the quest until it is given.")]
        [SerializeField] private bool hiddenUntilGiven = true;

        [Tooltip("When the quest can be given. Empty: always.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> availableWhen;

        [Tooltip("Give the quest as soon as it is available, instead of waiting for the game to give it.")]
        [SerializeField] private bool givenAutomatically;

        [Tooltip("When the quest fails. Empty: it can't fail, except through a step.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> failWhen;

        [Tooltip("The quest's steps. Order matters for steps that start after the step above.")]
        [SerializeField] private List<QuestStep> steps = new List<QuestStep>();

        [Tooltip("What completing the quest gives.")]
        [SerializeReference, SubclassPicker] private List<IQuestReward> rewards = new List<IQuestReward>();

        [Tooltip("Where the quest web draws the quest when pinned. The game never reads it.")]
        [HideInInspector]
        [SerializeField] private Vector2 webPosition;

        [Tooltip("True when the quest web keeps the quest where it was dragged. The game never reads it.")]
        [HideInInspector]
        [SerializeField] private bool webPinned;

        [Tooltip("True once the quest graph has saved where every step goes. The game never reads it.")]
        [HideInInspector]
        [SerializeField] private bool graphArranged;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public AssetId AssetId => assetId;

        /** <summary>The title.</summary> */
        public string Title => title;

        /** <summary>What the quest is about.</summary> */
        public string Summary => summary;

        /** <summary>The kind; null when unset.</summary> */
        public QuestKind Kind => kind;

        /** <summary>Who gives it; null when unset. A game's own asset type, so a portrait or a map pin can come from it.</summary> */
        public IDisplayNamed Giver => DisplayNames.As(giver);

        /** <summary>Where it is given; null when unset.</summary> */
        public IDisplayNamed Place => DisplayNames.As(place);

        /** <summary>Where it sits among others of its kind: lower comes first.</summary> */
        public int Order => order;

        /** <summary>The level it is meant for; 0 when none.</summary> */
        public int SuggestedLevel => suggestedLevel;

        /** <summary>True when it can be given again after it is done.</summary> */
        public bool Repeatable => repeatable;

        /** <summary>True when players don't see it until it is given.</summary> */
        public bool HiddenUntilGiven => hiddenUntilGiven;

        /** <summary>When it can be given; null means always.</summary> */
        public ICondition<IFacts> AvailableWhen => availableWhen;

        /** <summary>True when it is given as soon as it is available.</summary> */
        public bool GivenAutomatically => givenAutomatically;

        /** <summary>When it fails; null means never, except through a step.</summary> */
        public ICondition<IFacts> FailWhen => failWhen;

        /** <summary>The steps, in order.</summary> */
        public IReadOnlyList<QuestStep> Steps => steps;

        /** <summary>What completing it gives.</summary> */
        public IReadOnlyList<IQuestReward> Rewards => rewards;

        /** <summary>The asset that gives the quest, for editor tools; null when none.</summary> */
        internal Object GiverAsset => giver;

        /** <summary>The asset where the quest is given, for editor tools; null when none.</summary> */
        internal Object PlaceAsset => place;

        /** <summary>Where the quest web draws it, when pinned; never read by the game.</summary> */
        public Vector2 WebPosition => webPosition;

        /** <summary>True when the quest web keeps it where it was dragged; never read by the game.</summary> */
        public bool WebPinned => webPinned;

        /** <summary>True once the quest graph has saved where every step goes; never read by the game.</summary> */
        public bool GraphArranged => graphArranged;

        #endregion

        #region MonoBehaviour Callbacks

        private void OnValidate()
        {
            AssignMissingIds();
            if (!DisplayNames.IsNamedOrEmpty(giver)) giver = null;
            if (!DisplayNames.IsNamedOrEmpty(place)) place = null;
        }

        #endregion

        #region Public API

        /** <summary>Sets the quest up in code, as tests and tools do.</summary> */
        internal void Configure(IEnumerable<QuestStep> questSteps, QuestSettings settings = default)
        {
            steps = new List<QuestStep>(questSteps ?? Array.Empty<QuestStep>());
            rewards = new List<IQuestReward>(settings.Rewards ?? Array.Empty<IQuestReward>());
            availableWhen = settings.AvailableWhen;
            failWhen = settings.FailWhen;
            givenAutomatically = settings.GivenAutomatically;
            repeatable = settings.Repeatable;
            hiddenUntilGiven = settings.HiddenUntilGiven;
            title = settings.Title;
            kind = settings.Kind;
        }

        /** <summary>Puts a step at a place in the list, for editor tools.</summary> */
        internal void InsertStep(int index, QuestStep step) => steps.Insert(Mathf.Clamp(index, 0, steps.Count), step);

        /** <summary>Takes a step out, for editor tools.</summary> <returns>True when it was there.</returns> */
        internal bool RemoveStep(QuestStep step) => steps.Remove(step);

        /** <summary>Sets where the quest web draws the quest, for editor tools.</summary> */
        internal void SetWebPlacement(Vector2 position, bool pinned)
        {
            webPosition = position;
            webPinned = pinned;
        }

        /** <summary>Marks every step as placed by the quest graph, for editor tools.</summary> */
        internal void SetGraphArranged(bool arranged) => graphArranged = arranged;

        /** <summary>Sets when the quest can be given, for editor tools.</summary> */
        internal void SetAvailableWhen(ICondition<IFacts> condition) => availableWhen = condition;

        /** <summary>Sets the quest's kind, for editor tools.</summary> */
        internal void SetKind(QuestKind questKind) => kind = questKind;

        /** <summary>Sets who gives the quest, for editor tools; an asset that isn't display-named clears it.</summary> */
        internal void SetGiver(Object asset) => giver = DisplayNames.IsNamedOrEmpty(asset) ? asset : null;

        /** <summary>Sets where the quest is given, for editor tools; an asset that isn't display-named clears it.</summary> */
        internal void SetPlace(Object asset) => place = DisplayNames.IsNamedOrEmpty(asset) ? asset : null;

        /** <summary>Sets the quest's title, for editor tools.</summary> */
        internal void SetTitle(string questTitle) => title = questTitle;

        /** <summary>Finds a step by id; null when there is none.</summary> */
        public QuestStep FindStep(string stepId)
        {
            foreach (var step in steps)
                if (step != null && string.Equals(step.Id, stepId, StringComparison.Ordinal)) return step;
            return null;
        }

        /**
         * <summary>Gives every step and objective without an id, or with one used earlier in this quest, a new id.</summary>
         * <returns>True when any id changed.</returns>
         */
        public bool AssignMissingIds()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var changed = false;
            foreach (var step in steps)
            {
                if (step == null) continue;
                if (string.IsNullOrEmpty(step.Id) || !seen.Add(step.Id))
                {
                    step.AssignId(NewId(seen));
                    changed = true;
                }
                foreach (var objective in step.Objectives)
                {
                    if (objective == null) continue;
                    if (string.IsNullOrEmpty(objective.Id) || !seen.Add(objective.Id))
                    {
                        objective.AssignId(NewId(seen));
                        changed = true;
                    }
                }
            }
            return changed;
        }

        #endregion

        #region Helpers

        private static string NewId(HashSet<string> seen)
        {
            var id = CreateId();
            seen.Add(id);
            return id;
        }

        /** <summary>A fresh step or objective id.</summary> */
        internal static string CreateId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion
    }

    /** <summary>A quest's settings, for setting one up in code.</summary> */
    public struct QuestSettings
    {
        /** <summary>The title.</summary> */
        public string Title;

        /** <summary>The kind.</summary> */
        public QuestKind Kind;

        /** <summary>When it can be given; null means always.</summary> */
        public ICondition<IFacts> AvailableWhen;

        /** <summary>True when it is given as soon as it is available.</summary> */
        public bool GivenAutomatically;

        /** <summary>When it fails.</summary> */
        public ICondition<IFacts> FailWhen;

        /** <summary>True when it can be given again.</summary> */
        public bool Repeatable;

        /** <summary>True when players don't see it until it is given.</summary> */
        public bool HiddenUntilGiven;

        /** <summary>What completing it gives.</summary> */
        public IEnumerable<IQuestReward> Rewards;
    }
}
