using Avalonia.Input;
using YASN.Core;
using YASN.Hotkeys;
using YASN.Infrastructure.Settings;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies keybinding lookup, conflict detection, and gesture parsing. These operations are
    /// read-only and do not persist, so they leave the settings store untouched.
    /// </summary>
    public sealed class KeybindingRegistryTests
    {
        /// <summary>
        /// Seeds the catalog with the expected global and editor actions and default gestures.
        /// </summary>
        [Fact]
        public void DefaultsCoverEveryAction()
        {
            KeybindingRegistry registry = new KeybindingRegistry(new SettingsStore());

            foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
            {
                Assert.Contains(registry.Definitions, d => d.Action == action);
            }
        }

        /// <summary>
        /// Matches a gesture to its action only within the requested scope.
        /// </summary>
        [Fact]
        public void MatchRespectsScope()
        {
            KeybindingRegistry registry = new KeybindingRegistry(new SettingsStore());
            KeybindingDefinition insertImage = registry.Definitions.Single(d => d.Action == HotkeyAction.InsertImage);

            Assert.Equal(HotkeyAction.InsertImage, registry.Match(HotkeyScope.Editor, insertImage.Gesture!));
            Assert.Null(registry.Match(HotkeyScope.Global, insertImage.Gesture!));
        }

        /// <summary>
        /// Flags a same-scope duplicate gesture and ignores the action being edited.
        /// </summary>
        [Fact]
        public void FindConflictDetectsSameScopeDuplicate()
        {
            KeybindingRegistry registry = new KeybindingRegistry(new SettingsStore());
            KeybindingDefinition insertImage = registry.Definitions.Single(d => d.Action == HotkeyAction.InsertImage);
            KeyGesture gesture = insertImage.Gesture!;

            Assert.Equal(HotkeyAction.InsertImage, registry.FindConflict(HotkeyScope.Editor, gesture, HotkeyAction.CycleEditorMode));
            Assert.Null(registry.FindConflict(HotkeyScope.Editor, gesture, HotkeyAction.InsertImage));
        }

        /// <summary>
        /// Parses a serialized gesture and treats blank input as unbound.
        /// </summary>
        [Fact]
        public void ParseHandlesValidAndBlank()
        {
            KeyGesture? parsed = KeybindingRegistry.Parse("Ctrl+Shift+N");

            Assert.NotNull(parsed);
            Assert.Equal(Key.N, parsed!.Key);
            Assert.True(parsed.KeyModifiers.HasFlag(KeyModifiers.Control));
            Assert.True(parsed.KeyModifiers.HasFlag(KeyModifiers.Shift));
            Assert.Null(KeybindingRegistry.Parse("   "));
            Assert.Null(KeybindingRegistry.Parse("not a gesture!!"));
        }
    }
}
