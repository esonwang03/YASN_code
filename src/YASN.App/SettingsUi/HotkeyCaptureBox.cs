using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace YASN.SettingsUi
{
    /// <summary>
    /// A focusable control that records a single <see cref="KeyGesture"/> the next time the user
    /// presses a non-modifier key while focused, then exposes it as a serialized string. Modifier-only
    /// presses are ignored so the user can hold Ctrl/Alt/Shift before completing the combination.
    /// </summary>
    public sealed class HotkeyCaptureBox : TextBox
    {
        /// <summary>
        /// Raised when a new gesture is captured, carrying its serialized form.
        /// </summary>
        public event EventHandler<string>? GestureCaptured;

        /// <summary>
        /// Initializes the capture box as a read-only, focusable field.
        /// </summary>
        public HotkeyCaptureBox()
        {
            IsReadOnly = true;
            Focusable = true;
            PlaceholderText = "…";

            // TextBox installs its own bubble-stage KeyDown class handlers that consume text input,
            // navigation, and clipboard gestures before an OnKeyDown override would see them. Handle
            // at the tunnel stage so the gesture is captured first and the base logic never runs.
            AddHandler(KeyDownEvent, HandleKeyDownTunnel, RoutingStrategies.Tunnel);
        }

        // Avalonia resolves control themes by the control's runtime type. Without this override the
        // theme system looks for a "HotkeyCaptureBox" ControlTheme, finds none, applies no template,
        // and the control renders blank. Pointing the style key at TextBox reuses the TextBox theme.
        protected override Type StyleKeyOverride => typeof(TextBox);

        /// <inheritdoc/>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            // Ensure a click gives the box keyboard focus so the next key press is captured.
            base.OnPointerPressed(e);
            Focus();
        }

        private void HandleKeyDownTunnel(object? sender, KeyEventArgs e)
        {
            // Let Tab and Shift+Tab through so keyboard users can move focus out of the box. Capturing
            // them would trap focus here; they are also not useful as application hotkeys.
            if (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift))
            {
                return;
            }

            // Swallow every other key so it never reaches the underlying text editing or shortcuts.
            e.Handled = true;

            if (IsModifierKey(e.Key) || e.Key == Key.Escape)
            {
                return;
            }

            // Backspace/Delete clears the binding.
            if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
            {
                Text = string.Empty;
                GestureCaptured?.Invoke(this, string.Empty);
                return;
            }

            if (e.KeyModifiers == KeyModifiers.None)
            {
                // A bare key cannot be a global hotkey and would shadow typing; require a modifier.
                return;
            }

            KeyGesture gesture = new KeyGesture(e.Key, e.KeyModifiers);
            string serialized = gesture.ToString();
            Text = serialized;
            GestureCaptured?.Invoke(this, serialized);
        }

        private static bool IsModifierKey(Key key) => key is
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin;
    }
}
