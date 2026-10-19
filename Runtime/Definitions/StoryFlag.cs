using Extensions.Ids;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>A fact about the story that quests and the game set and read: a whole number, 0 when unset.</summary> */
    [CreateAssetMenu(menuName = "Quests/Story Flag", fileName = "StoryFlag")]
    public sealed class StoryFlag : ScriptableObject, IIdentifiedAsset, IConditionSubject
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor.")]
        [SerializeField] private AssetId assetId;

        [Tooltip("The value a new game starts with. 0 means unset; use 1 for a plain on/off flag.")]
        [SerializeField] private int initialValue;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public AssetId AssetId => assetId;

        /** <summary>The value a new game starts with.</summary> */
        public int InitialValue => initialValue;

        #endregion
    }
}
