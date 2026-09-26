using UnityEngine;

namespace TheDeep.UI.Terminal
{
    /// <summary>
    /// Base class for a program on the sub's terminal. Add app components next to the TerminalOS
    /// and they show up as desktop icons automatically.
    /// </summary>
    public abstract class TerminalApp : MonoBehaviour
    {
        public abstract string Title { get; }
        /// <summary>Short text drawn inside the desktop icon.</summary>
        public abstract string IconGlyph { get; }
        public virtual Color IconColor => new(0.25f, 0.55f, 0.6f);
        public virtual Vector2 WindowSize => new(560, 400);

        /// <summary>Called once, the first time the window opens. Build the app's UI inside <paramref name="content"/>.</summary>
        public abstract void BuildContent(RectTransform content);

        public virtual void OnOpened() { }
        public virtual void OnClosed() { }
    }
}
