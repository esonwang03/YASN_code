using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using YASN.Core;

namespace YASN.Views
{
    /// <summary>
    /// Manual show/hide of the note window chrome (the title bar). The user toggles it from the title
    /// bar or preview context menu, or with the configurable shortcut. Hiding the bar keeps the
    /// rendered note unobstructed while reading.
    /// </summary>
    /// <remarks>
    /// The bar is an overlay over a full-size body, animated by an opacity fade and a
    /// <see cref="TranslateTransform"/> slide (declared in XAML). Toggling never resizes the body, so
    /// the native preview WebView is untouched — an earlier auto-collapse that resized the docked bar's
    /// row made Chromium emit synthetic pointer events that fought the toggle, so the resize approach
    /// was abandoned for a render-transform overlay.
    /// </remarks>
    public sealed partial class FloatingNoteWindow
    {
        private Border? collapseTitleBar;
        private TranslateTransform? collapseSlide;
        private Grid? collapseBody;
        private double expandedChromeHeight = double.NaN;
        private bool chromeVisible = true;

        /// <summary>
        /// Resolves the chrome controls. Called once from the constructor.
        /// </summary>
        private void InitializeChromeCollapse()
        {
            collapseTitleBar = this.FindControl<Border>("TitleBar")
                ?? throw new InvalidOperationException("TitleBar was not found.");
            collapseSlide = collapseTitleBar.RenderTransform as TranslateTransform
                ?? throw new InvalidOperationException("TitleBar slide transform was not found.");
            collapseBody = this.FindControl<Grid>("BodyContent")
                ?? throw new InvalidOperationException("BodyContent was not found.");
        }

        /// <summary>
        /// Pins the bar's expanded height from the first layout pass so the slide transition animates
        /// between concrete values. Called from the loaded handler once the title bar is measured.
        /// </summary>
        private void BeginChromeAutoCollapse()
        {
            if (collapseTitleBar is not null && double.IsNaN(expandedChromeHeight))
            {
                double measured = collapseTitleBar.Bounds.Height;
                if (measured > 0)
                {
                    expandedChromeHeight = measured;
                }
            }

            UpdateBodyInset();
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
        /// Insets the body below the floating bar while the bar is shown so it does not cover the editor
        /// toolbar or the top of the preview. When hidden, the body fills the window. The inset only
        /// changes on an explicit toggle, never during the slide animation, so the preview WebView is
        /// not resized mid-animation.
        /// </summary>
        private void UpdateBodyInset()
        {
            if (collapseBody is null || double.IsNaN(expandedChromeHeight))
            {
                return;
            }

            double top = chromeVisible ? expandedChromeHeight : 0;
            collapseBody.Margin = new Thickness(0, top, 0, 0);
        }

        /// <summary>
        /// Applies the current <see cref="chromeVisible"/> state: slides and fades the bar in or out and
        /// re-insets the body. Animated by the XAML transitions on the translate transform and opacity.
        /// </summary>
        private void ApplyChromeState()
        {
            UpdateBodyInset();

            if (chromeVisible)
            {
                if (collapseSlide is not null)
                {
                    collapseSlide.Y = 0;
                }

                if (collapseTitleBar is not null)
                {
                    collapseTitleBar.Opacity = 1;
                    collapseTitleBar.IsHitTestVisible = true;
                }

                return;
            }

            if (double.IsNaN(expandedChromeHeight))
            {
                return;
            }

            if (collapseSlide is not null)
            {
                collapseSlide.Y = -expandedChromeHeight;
            }

            if (collapseTitleBar is not null)
            {
                collapseTitleBar.Opacity = 0;
                collapseTitleBar.IsHitTestVisible = false;
            }
        }
    }
}
