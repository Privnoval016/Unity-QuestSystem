using Extensions.Ids;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>A colour editor tools give a kind. The values are saved in assets: never reorder or renumber them.</summary> */
    public enum KindColour
    {
        /** <summary>Indigo.</summary> */
        Indigo = 0,
        /** <summary>Green.</summary> */
        Green = 1,
        /** <summary>Purple.</summary> */
        Purple = 2,
        /** <summary>Orange.</summary> */
        Orange = 3,
        /** <summary>Olive.</summary> */
        Olive = 4,
        /** <summary>Grey.</summary> */
        Grey = 5,
    }

    /** <summary>A kind of quest, such as main story or errand. A game lists quests by kind, in this order.</summary> */
    [CreateAssetMenu(menuName = "Quests/Quest Kind", fileName = "QuestKind")]
    public sealed class QuestKind : ScriptableObject, IIdentifiedAsset, IDisplayNamed
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor.")]
        [SerializeField] private AssetId assetId;

        [Tooltip("The kind's name as players see it.")]
        [SerializeField] private string displayName;

        [Tooltip("Where this kind sits among the others: lower comes first.")]
        [SerializeField] private int order;

        [Tooltip("The kind's colour in the quest graph. The game never reads it.")]
        [SerializeField] private KindColour graphColour;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public AssetId AssetId => assetId;

        /** <inheritdoc /> */
        public string DisplayName => displayName;

        /** <summary>Where this kind sits among the others: lower comes first.</summary> */
        public int Order => order;

        /** <summary>The kind's colour in the quest graph.</summary> */
        public KindColour GraphColour => graphColour;

        #endregion
    }
}
