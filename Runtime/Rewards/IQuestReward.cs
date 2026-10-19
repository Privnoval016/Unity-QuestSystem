using Extensions.Logic;

namespace QuestSystem
{
    /** <summary>Something completing a quest gives. A game implements its own (items, money, a bond rank).</summary> */
    public interface IQuestReward
    {
        /** <summary>The reward as players see it on the quest page.</summary> */
        string Describe();

        /** <summary>Gives the reward, reading whatever it needs from the facts the game registered.</summary> */
        void Grant(IFacts facts);
    }
}
