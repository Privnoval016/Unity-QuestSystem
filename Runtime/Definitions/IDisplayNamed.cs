using UnityEngine;

namespace QuestSystem
{
    /** <summary>An asset with a name to show, such as the person who gives a quest or the place it is given.</summary> */
    public interface IDisplayNamed
    {
        /** <summary>The name to show.</summary> */
        string DisplayName { get; }
    }

    /** <summary>The one place that asks whether an asset has a display name.</summary> */
    public static class DisplayNames
    {
        /** <summary>The asset as something with a display name; null when it isn't one.</summary> */
        public static IDisplayNamed As(Object asset) => asset != null && asset is IDisplayNamed named ? named : null;

        /** <summary>True for an asset that can be shown by name, or no asset.</summary> */
        public static bool IsNamedOrEmpty(Object asset) => asset == null || asset is IDisplayNamed;
    }
}
