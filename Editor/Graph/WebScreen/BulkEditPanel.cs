using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Edits several selected quests at once: kind, giver and place, each one Undo step. A field shows the shared
     * value, or a mixed value when the quests differ.</summary>
     */
    public sealed class BulkEditPanel : VisualElement
    {
        #region Private Fields

        /** <summary>The kind picker's entry for quests that differ, or have none; picking it changes nothing.</summary> */
        private const string Mixed = "—";

        private List<QuestDefinition> _quests = new List<QuestDefinition>();

        #endregion

        #region Properties

        /** <summary>Raised after an edit, with the quests changed.</summary> */
        public event Action<IReadOnlyList<QuestDefinition>> OnEdited = delegate { };

        /** <summary>Raised when a picked asset has no display name to show, so nothing changed.</summary> */
        public event Action OnRefused = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes a hidden panel.</summary> */
        public BulkEditPanel()
        {
            AddToClassList("quest-graph__bulk");
            style.display = DisplayStyle.None;
        }

        /** <summary>Shows the panel for two or more quests, choosing among the kinds given; hides it otherwise.</summary> */
        public void Show(IReadOnlyCollection<QuestDefinition> quests, IReadOnlyList<QuestKind> kinds)
        {
            Clear();
            _quests = quests.Where(quest => quest != null).ToList();
            style.display = _quests.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_quests.Count < 2) return;
            Add(new Label($"{_quests.Count} quests"));
            Add(KindPicker(kinds));
            Add(ObjectPicker("Giver", quest => quest.GiverAsset, QuestEdits.SetGiver));
            Add(ObjectPicker("Place", quest => quest.PlaceAsset, QuestEdits.SetPlace));
        }

        #endregion

        #region Helpers

        /** <summary>A popup of every kind by position, so kinds sharing a name stay apart.</summary> */
        private PopupField<int> KindPicker(IReadOnlyList<QuestKind> kinds)
        {
            var choices = Enumerable.Range(-1, kinds.Count + 1).ToList();
            var shared = _quests.Select(quest => quest.Kind).Distinct().ToList();
            var current = shared.Count == 1 && shared[0] != null ? IndexOf(kinds, shared[0]) : -1;
            var field = new PopupField<int>("Kind", choices, current, index => index < 0 ? Mixed : QuestStates.NameOf(kinds[index]), index => index < 0 ? Mixed : QuestStates.NameOf(kinds[index]));
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 0) return;
                QuestEdits.SetKind(_quests, kinds[evt.newValue]);
                OnEdited(_quests);
            });
            return field;
        }

        private ObjectField ObjectPicker(string label, Func<QuestDefinition, Object> read, Func<IEnumerable<QuestDefinition>, Object, bool> write)
        {
            var values = _quests.Select(read).Distinct().ToList();
            var field = new ObjectField(label) { objectType = typeof(Object), allowSceneObjects = false, value = values.Count == 1 ? values[0] : null, showMixedValue = values.Count > 1 };
            field.RegisterValueChangedCallback(evt =>
            {
                if (!write(_quests, evt.newValue))
                {
                    field.SetValueWithoutNotify(evt.previousValue);
                    OnRefused();
                    return;
                }
                field.showMixedValue = false;
                OnEdited(_quests);
            });
            return field;
        }

        private static int IndexOf(IReadOnlyList<QuestKind> kinds, QuestKind kind)
        {
            for (var i = 0; i < kinds.Count; i++)
                if (kinds[i] == kind) return i;
            return -1;
        }

        #endregion
    }
}
