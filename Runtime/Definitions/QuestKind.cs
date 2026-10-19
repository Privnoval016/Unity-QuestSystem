using Extensions.Ids;
using UnityEngine;

namespace QuestSystem
{
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

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public AssetId AssetId => assetId;

        /** <inheritdoc /> */
        public string DisplayName => displayName;

        /** <summary>Where this kind sits among the others: lower comes first.</summary> */
        public int Order => order;

        #endregion
    }
}
