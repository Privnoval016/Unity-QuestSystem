using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>Something completing a quest gives. A game implements its own (items, money, a bond rank).</summary> */
    public interface IQuestReward
    {
        /** <summary>The reward as the quest page shows it.</summary> */
        RewardDescription Describe();

        /** <summary>Gives the reward, reading what it needs from the facts the game registered.</summary> */
        void Grant(IFacts facts);
    }

    /** <summary>How a reward shows on the quest page: its text, and optionally an icon and an amount.</summary> */
    public readonly struct RewardDescription
    {
        #region Properties

        /** <summary>What it is, as players see it.</summary> */
        public string Text { get; }

        /** <summary>Its icon; null when it has none.</summary> */
        public Sprite Icon { get; }

        /** <summary>How many; 0 when a count doesn't apply.</summary> */
        public int Amount { get; }

        #endregion

        #region Public API

        /** <summary>Makes a description.</summary> */
        public RewardDescription(string text, Sprite icon = null, int amount = 0)
        {
            Text = text;
            Icon = icon;
            Amount = amount;
        }

        #endregion
    }
}
