using Avalonia.Input;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Converts Avalonia <see cref="KeyGesture"/> values into the Win32 modifier flags and virtual
    /// key codes required by <c>RegisterHotKey</c>. The mapping is exhaustive over the key ranges it
    /// claims to support and reports failure for keys it cannot represent, rather than guessing.
    /// </summary>
    public static class Win32KeyGestureConverter
    {
        // Win32 fsModifiers flags (WinUser.h).
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNoRepeat = 0x4000;

        /// <summary>
        /// Attempts to convert a gesture to Win32 hotkey parameters.
        /// </summary>
        /// <param name="gesture">The gesture to convert.</param>
        /// <param name="modifiers">The resulting <c>fsModifiers</c> value (includes MOD_NOREPEAT).</param>
        /// <param name="virtualKey">The resulting virtual key code.</param>
        /// <returns><see langword="true"/> when the gesture maps to a registrable hotkey.</returns>
        public static bool TryConvert(KeyGesture gesture, out uint modifiers, out uint virtualKey)
        {
            modifiers = ModNoRepeat;
            virtualKey = 0;

            // RegisterHotKey requires at least one modifier; a bare key would hijack normal typing.
            if (gesture.KeyModifiers == KeyModifiers.None)
            {
                return false;
            }

            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= ModControl;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= ModAlt;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= ModShift;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= ModWin;

            return TryMapVirtualKey(gesture.Key, out virtualKey);
        }

        private static bool TryMapVirtualKey(Key key, out uint virtualKey)
        {
            // Letters: Avalonia Key.A..Z are contiguous and map directly to VK 0x41..0x5A.
            if (key >= Key.A && key <= Key.Z)
            {
                virtualKey = (uint)('A' + (key - Key.A));
                return true;
            }

            // Top-row digits: Key.D0..D9 map to VK 0x30..0x39.
            if (key >= Key.D0 && key <= Key.D9)
            {
                virtualKey = (uint)('0' + (key - Key.D0));
                return true;
            }

            // Numpad digits: VK_NUMPAD0..9 = 0x60..0x69.
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                virtualKey = (uint)(0x60 + (key - Key.NumPad0));
                return true;
            }

            // Function keys: VK_F1..F24 = 0x70..0x87.
            if (key >= Key.F1 && key <= Key.F24)
            {
                virtualKey = (uint)(0x70 + (key - Key.F1));
                return true;
            }

            virtualKey = key switch
            {
                Key.Space => 0x20,
                Key.Enter => 0x0D,
                Key.Tab => 0x09,
                Key.Escape => 0x1B,
                Key.Back => 0x08,
                Key.Delete => 0x2E,
                Key.Insert => 0x2D,
                Key.Home => 0x24,
                Key.End => 0x23,
                Key.PageUp => 0x21,
                Key.PageDown => 0x22,
                Key.Up => 0x26,
                Key.Down => 0x28,
                Key.Left => 0x25,
                Key.Right => 0x27,
                Key.OemComma => 0xBC,
                Key.OemPeriod => 0xBE,
                Key.OemMinus => 0xBD,
                Key.OemPlus => 0xBB,
                Key.OemQuestion => 0xBF,
                Key.OemSemicolon => 0xBA,
                Key.OemOpenBrackets => 0xDB,
                Key.OemCloseBrackets => 0xDD,
                Key.OemPipe => 0xDC,
                Key.OemQuotes => 0xDE,
                Key.OemTilde => 0xC0,
                _ => 0u
            };

            return virtualKey != 0;
        }
    }
}
