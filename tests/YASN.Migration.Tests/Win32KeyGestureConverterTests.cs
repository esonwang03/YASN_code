using Avalonia.Input;
using YASN.PlatformServices;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the Avalonia-to-Win32 hotkey gesture conversion used by global hotkey registration.
    /// </summary>
    public sealed class Win32KeyGestureConverterTests
    {
        /// <summary>
        /// Rejects a gesture without modifiers, since a bare key cannot be a global hotkey.
        /// </summary>
        [Fact]
        public void RejectsGestureWithoutModifiers()
        {
            KeyGesture gesture = new KeyGesture(Key.N);

            Assert.False(Win32KeyGestureConverter.TryConvert(gesture, out _, out _));
        }

        /// <summary>
        /// Maps a letter key with Ctrl+Alt to the expected virtual key and modifier flags.
        /// </summary>
        [Fact]
        public void MapsLetterWithControlAlt()
        {
            KeyGesture gesture = new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Alt);

            bool converted = Win32KeyGestureConverter.TryConvert(gesture, out uint modifiers, out uint virtualKey);

            Assert.True(converted);
            Assert.Equal((uint)'N', virtualKey);
            // MOD_CONTROL (0x2) | MOD_ALT (0x1) | MOD_NOREPEAT (0x4000).
            Assert.Equal(0x4003u, modifiers);
        }

        /// <summary>
        /// Maps function keys into the VK_F1..F24 range.
        /// </summary>
        [Fact]
        public void MapsFunctionKey()
        {
            KeyGesture gesture = new KeyGesture(Key.F5, KeyModifiers.Control);

            bool converted = Win32KeyGestureConverter.TryConvert(gesture, out _, out uint virtualKey);

            Assert.True(converted);
            Assert.Equal(0x74u, virtualKey);
        }

        /// <summary>
        /// Maps the comma OEM key used by the default settings hotkey.
        /// </summary>
        [Fact]
        public void MapsOemComma()
        {
            KeyGesture gesture = new KeyGesture(Key.OemComma, KeyModifiers.Control | KeyModifiers.Alt);

            bool converted = Win32KeyGestureConverter.TryConvert(gesture, out _, out uint virtualKey);

            Assert.True(converted);
            Assert.Equal(0xBCu, virtualKey);
        }
    }
}
