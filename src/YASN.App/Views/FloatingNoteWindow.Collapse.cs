using Avalonia.Controls;
using YASN.Core;

namespace YASN.Views
{
    /// <summary>
    /// Manual show/hide of the note window chrome (the title bar). The user toggles it from the title
    /// bar or preview context menu, or with the configurable shortcut. Hiding the bar keeps the
    /// rendered note unobstructed while reading.
    /// </summary>
    /// <remarks>
    /// The bar occupies the first row of the window's root grid. Toggling hides or shows that row via
    /// layout instead of relying on overlay drawing order, which keeps the native preview WebView below
    /// visible chrome on platforms where native hosts draw above Avalonia visuals.
    /// </remarks>
    public sealed partial class FloatingNoteWindow
    {
        private Border? collapseTitleBar;
        private bool chromeVisible = true;

        /// <summary>
        /// Resolves the chrome controls. Called once from the constructor.
        /// </summary>
        private void InitializeChromeCollapse()
        {
            collapseTitleBar = this.FindControl<Border>("TitleBar")
                ?? throw new InvalidOperationException("TitleBar was not found.");
        }

        /// <summary>
        /// Applies the initial chrome state after the window is loaded.
        /// </summary>
        private void BeginChromeAutoCollapse()
        {
            ApplyChromeState();
        }

        /// <summary>
        /// Toggles the title bar between shown and hidden. Bound to the title bar and preview context
        /// menus and the <see cref="HotkeyAction.ToggleChrome"/> shortcut.
        /// </summary>
        private void ToggleChrome()
        {
            chromeVisible = !chromeVisible;
            ApplyChromeState();
        }

        /// <summary>
        /// Applies the current <see cref="chromeVisible"/> state through layout visibility.
        /// </summary>
        private void ApplyChromeState()
        {
            if (collapseTitleBar is not null)
            {
                collapseTitleBar.IsVisible = chromeVisible;
            }
        }
    }
}
