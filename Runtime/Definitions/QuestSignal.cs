using UnityEngine;

namespace QuestSystem
{
    /**
     * <summary>Something that happens and can count towards objectives, such as a foe of one kind defeated or a place
     * reached. Game code or a scene trigger raises it.</summary>
     */
    [CreateAssetMenu(menuName = "Quests/Quest Signal", fileName = "QuestSignal")]
    public sealed class QuestSignal : ScriptableObject { }
}
