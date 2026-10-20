using Extensions.Menus;

namespace QuestSystem
{
    /** <summary>Where the quest system's entries sit: <i>Quest System</i> under the Privnoval heading.</summary> */
    public static class QuestMenus
    {
        #region Public API

        /** <summary>The tool's name in every menu.</summary> */
        public const string Name = "Quest System/";

        /** <summary>Its windows and commands.</summary> */
        public const string Tools = ToolMenus.Tools + Name;

        /** <summary>Its assets.</summary> */
        public const string Create = ToolMenus.Create + Name;

        /** <summary>Its components.</summary> */
        public const string Component = ToolMenus.Component + Name;

        #endregion
    }
}
