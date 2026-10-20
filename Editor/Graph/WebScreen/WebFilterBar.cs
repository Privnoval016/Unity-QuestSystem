using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>Which form the web takes.</summary> */
    public enum WebView
    {
        /** <summary>Quests wired by the conditions that read other quests.</summary> */
        Dependencies,
        /** <summary>Story flags above the quests, wired to the quests that read them.</summary> */
        StoryFlags,
    }

    /**
     * <summary>The web's toolbar row: the view, a search, a chip per kind and per property, and, while a quest has the
     * focus, whether the focus shows its direct links or its whole chain.</summary>
     */
    public sealed class WebFilterBar : Toolbar
    {
        #region Private Fields

        private readonly ToolbarToggle _dependencies = new ToolbarToggle { text = "Dependencies", tooltip = "Quests wired by the conditions that read other quests" };
        private readonly ToolbarToggle _flags = new ToolbarToggle { text = "Story flags", tooltip = "Story flags wired to the quests that read them" };
        private readonly ToolbarSearchField _search = new ToolbarSearchField { tooltip = "Search quest titles, step text and step ids" };
        private readonly VisualElement _kinds = new VisualElement();
        private readonly ToolbarToggle _repeatable = new ToolbarToggle { text = "Repeatable" };
        private readonly ToolbarToggle _shownEarly = new ToolbarToggle { text = "Shown early", tooltip = "Players see it before it is given" };
        private readonly ToolbarToggle _problems = new ToolbarToggle { text = "Problems", tooltip = "Has errors or warnings" };
        private readonly VisualElement _depth = new VisualElement();
        private readonly ToolbarToggle _direct = new ToolbarToggle { text = "Direct", tooltip = "Show the focused quest's own links" };
        private readonly ToolbarToggle _chain = new ToolbarToggle { text = "Chain", tooltip = "Show everything the focused quest leads to and comes from" };
        private readonly List<ToolbarToggle> _kindChips = new List<ToolbarToggle>();
        private List<QuestKind> _shownKinds = new List<QuestKind>();

        #endregion

        #region Properties

        /** <summary>The filters and search set.</summary> */
        public WebFilters Filters { get; } = new WebFilters();

        /** <summary>The view chosen.</summary> */
        public WebView View { get; private set; } = WebView.Dependencies;

        /** <summary>True when a focus shows the whole chain, false for direct links only.</summary> */
        public bool Chain { get; private set; } = true;

        /** <summary>Raised when the search, a filter or the focus depth changes.</summary> */
        public event Action OnFiltersChanged = delegate { };

        /** <summary>Raised when the view changes.</summary> */
        public event Action OnViewChanged = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes the bar with nothing filtered, on the dependencies view.</summary> */
        public WebFilterBar()
        {
            AddToClassList("quest-graph__web-bar");
            _dependencies.RegisterValueChangedCallback(evt => SetView(WebView.Dependencies, evt.newValue));
            _flags.RegisterValueChangedCallback(evt => SetView(WebView.StoryFlags, evt.newValue));
            Add(_dependencies);
            Add(_flags);
            Add(new ToolbarSpacer());
            _search.AddToClassList("quest-graph__web-search");
            _search.RegisterValueChangedCallback(evt =>
            {
                Filters.Search = evt.newValue ?? "";
                OnFiltersChanged();
            });
            Add(_search);
            _kinds.AddToClassList("quest-graph__chips");
            Add(_kinds);
            Add(new ToolbarSpacer());
            AddChip(_repeatable, on => Filters.Repeatable = on);
            AddChip(_shownEarly, on => Filters.ShownEarly = on);
            AddChip(_problems, on => Filters.Problems = on);
            Add(new ToolbarSpacer { flex = true });
            _depth.AddToClassList("quest-graph__chips");
            _direct.RegisterValueChangedCallback(evt => SetChain(!evt.newValue));
            _chain.RegisterValueChangedCallback(evt => SetChain(evt.newValue));
            _depth.Add(_direct);
            _depth.Add(_chain);
            Add(_depth);
            Refresh();
            ShowFocusDepth(false);
        }

        /** <summary>Shows a chip for each kind given; chips for kinds no longer given are dropped from the filter.</summary> */
        public void SetKinds(IEnumerable<QuestKind> kinds)
        {
            var list = kinds.ToList();
            if (list.SequenceEqual(_shownKinds)) return;
            _shownKinds = list;
            Filters.Kinds.IntersectWith(list);
            _kinds.Clear();
            _kindChips.Clear();
            foreach (var kind in list)
            {
                var chip = new ToolbarToggle { text = QuestStates.NameOf(kind) };
                chip.AddToClassList("quest-graph__kind-chip");
                chip.Insert(0, KindLooks.Swatch(kind));
                chip.SetValueWithoutNotify(Filters.Kinds.Contains(kind));
                chip.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) Filters.Kinds.Add(kind);
                    else Filters.Kinds.Remove(kind);
                    OnFiltersChanged();
                });
                _kinds.Add(chip);
                _kindChips.Add(chip);
            }
        }

        /** <summary>Shows or hides the focus depth, which only means something while a quest has the focus.</summary> */
        public void ShowFocusDepth(bool shown) => _depth.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        /** <summary>Clears the search and every filter.</summary> */
        public void ClearFilters()
        {
            Filters.Clear();
            _search.SetValueWithoutNotify("");
            foreach (var chip in _kindChips.Append(_repeatable).Append(_shownEarly).Append(_problems))
                chip.SetValueWithoutNotify(false);
            OnFiltersChanged();
        }

        #endregion

        #region Helpers

        private void AddChip(ToolbarToggle chip, Action<bool> set)
        {
            chip.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                OnFiltersChanged();
            });
            Add(chip);
        }

        /** <summary>Picks a view; turning the shown view's toggle off leaves it on, since one view always shows.</summary> */
        private void SetView(WebView view, bool on)
        {
            if (!on && View == view)
            {
                Refresh();
                return;
            }
            if (!on || View == view) return;
            View = view;
            Refresh();
            OnViewChanged();
        }

        private void SetChain(bool chain)
        {
            var changed = Chain != chain;
            Chain = chain;
            Refresh();
            if (changed) OnFiltersChanged();
        }

        private void Refresh()
        {
            _dependencies.SetValueWithoutNotify(View == WebView.Dependencies);
            _flags.SetValueWithoutNotify(View == WebView.StoryFlags);
            _direct.SetValueWithoutNotify(!Chain);
            _chain.SetValueWithoutNotify(Chain);
        }

        #endregion
    }
}
