using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;
using YASN.Infrastructure.Sync;
using YASN.Infrastructure.Sync.WebDav;
using YASN.Localization;
using YASN.Migration;
using YASN.PlatformServices;
using YASN.Theming;

namespace YASN.SettingsUi
{
    /// <summary>
    /// Builds the schema-driven settings model bound by the settings window.
    /// </summary>
    public static class SettingsSchemaBuilder
    {
        /// <summary>
        /// Local-only field key that mirrors the OS auto-start state. The value is not persisted to
        /// the settings store; it is applied directly through <see cref="IAutoStartService"/>.
        /// </summary>
        public const string AutoStartKey = "app.autoStart";

        /// <summary>
        /// Local-only key controlling whether notes left open last session are reopened on startup.
        /// Machine-specific (not synced) since window state is local. Defaults to true.
        /// </summary>
        public const string RestoreOpenNotesKey = "app.restoreOpenNotes";

        /// <summary>
        /// Local-only key for diagnose mode. When enabled the app raises a console streaming the live
        /// log and opens developer tools for note previews. Machine-specific (not synced) and defaults
        /// to false; applied through <see cref="YASN.Diagnostics.DiagnoseMode"/>.
        /// </summary>
        public const string DiagnoseKey = "app.diagnose";

        /// <summary>
        /// Synced key for the master attachment auto-copy toggle.
        /// </summary>
        public const string AttachmentAutoSyncEnabledKey = "attachment.autoSyncEnabled";

        /// <summary>
        /// Synced key for the attachment copy/link size threshold in megabytes.
        /// </summary>
        public const string AttachmentThresholdMbKey = "attachment.autoSyncThresholdMb";

        /// <summary>
        /// Synced key controlling whether a firing reminder activates its note window and scrolls to
        /// the reminder location. The OS toast fires regardless of this setting.
        /// </summary>
        public const string ReminderActivateOnFireKey = "reminder.activateOnFire";

        /// <summary>
        /// Default attachment copy/link threshold in megabytes.
        /// </summary>
        public const int DefaultAttachmentThresholdMb = 5;

        /// <summary>
        /// Synced key for the floating note minimum window width, in device-independent pixels. Governs
        /// how narrow a note can be resized and the lower bound QuickLayout / editor-mode expansion clamp
        /// to.
        /// </summary>
        public const string NoteMinWidthKey = "editor.minWidth";

        /// <summary>
        /// Default floating note minimum width, in device-independent pixels. Matches the historical
        /// hard-coded value so existing notes are unaffected until the user changes it.
        /// </summary>
        public const int DefaultNoteMinWidth = 620;

        /// <summary>
        /// Synced key controlling what double-clicking the preview does. One of
        /// <see cref="PreviewDoubleClickNone"/>, <see cref="PreviewDoubleClickInApp"/>, or
        /// <see cref="PreviewDoubleClickExternal"/>.
        /// </summary>
        public const string PreviewDoubleClickActionKey = "editor.previewDoubleClickAction";

        /// <summary>Double-clicking the preview does nothing.</summary>
        public const string PreviewDoubleClickNone = "none";

        /// <summary>Double-clicking the preview enters the in-app editor at the clicked line (default).</summary>
        public const string PreviewDoubleClickInApp = "inApp";

        /// <summary>Double-clicking the preview opens the note in the external editor.</summary>
        public const string PreviewDoubleClickExternal = "external";

        /// <summary>
        /// Builds the settings view model and applies persisted values.
        /// </summary>
        /// <param name="store">The settings store used to load persisted values.</param>
        /// <param name="autoStart">The auto-start service used to seed the auto-start toggle.</param>
        /// <param name="keybindings">The keybinding registry used to seed the shortcuts module.</param>
        /// <param name="showTutorial">Optional handler backing the "show tutorial note" action; omitted when null.</param>
        /// <param name="deleteAllData">Optional handler backing the "delete all data" action; omitted when null.</param>
        /// <returns>A populated settings view model.</returns>
        public static SettingsViewModel Build(SettingsStore store, IAutoStartService autoStart, KeybindingRegistry keybindings, Func<Task<string>>? showTutorial = null, Func<Task<string>>? deleteAllData = null)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(autoStart);
            ArgumentNullException.ThrowIfNull(keybindings);

            SettingsViewModel viewModel = new SettingsViewModel();
            viewModel.Modules.Add(BuildGeneralModule(autoStart, showTutorial, deleteAllData));
            viewModel.Modules.Add(BuildSyncModule());
            viewModel.Modules.Add(BuildAttachmentsModule());
            viewModel.Modules.Add(BuildEditorModule());
            viewModel.Modules.Add(BuildShortcutsModule(keybindings));

            foreach (SettingModule module in viewModel.Modules)
            {
                store.ApplyValues(module.Fields);
            }

            return viewModel;
        }

        /// <summary>
        /// Builds the shortcuts module: one hotkey field per keybinding, seeded from the registry's
        /// current gestures. These fields persist through the registry rather than the store.
        /// </summary>
        /// <param name="keybindings">The keybinding registry.</param>
        private static SettingModule BuildShortcutsModule(KeybindingRegistry keybindings)
        {
            SettingModule module = new SettingModule
            {
                Key = "shortcuts",
                Title = LocalizationService.Current["Settings.Shortcuts.Module"]
            };

            foreach (KeybindingDefinition definition in keybindings.Definitions)
            {
                module.Fields.Add(new SettingField
                {
                    Key = definition.SettingKey,
                    Title = LocalizationService.Current[definition.LabelKey],
                    FieldType = SettingFieldType.Hotkey,
                    ShouldSync = false,
                    Value = definition.Gesture?.ToString() ?? string.Empty,
                    DefaultValue = definition.DefaultGesture?.ToString() ?? string.Empty
                });
            }

            return module;
        }

        private static SettingModule BuildGeneralModule(IAutoStartService autoStart, Func<Task<string>>? showTutorial, Func<Task<string>>? deleteAllData)
        {
            SettingModule module = new SettingModule
            {
                Key = "general",
                Title = "General"
            };

            module.Fields.Add(new SettingField
            {
                Key = AppPaths.DataDirectorySettingKey,
                Title = "Data directory",
                Description = LocalizationService.Current["Settings.DataDir.Description"],
                FieldType = SettingFieldType.Text,
                ShouldSync = false,
                EnableFolderBrowse = true,
                Value = AppPaths.DataDirectory
            });

            SettingField taskbar = new SettingField
            {
                Key = TaskbarVisibility.SettingKey,
                Title = LocalizationService.Current["Settings.Taskbar"],
                FieldType = SettingFieldType.Select,
                ShouldSync = true,
                Value = TaskbarVisibility.AlwaysHideValue
            };
            taskbar.Options.Add(new SettingOption { Label = LocalizationService.Current["Taskbar.Mode.AlwaysShow"], Value = TaskbarVisibility.AlwaysShowValue });
            taskbar.Options.Add(new SettingOption { Label = LocalizationService.Current["Taskbar.Mode.AlwaysHide"], Value = TaskbarVisibility.AlwaysHideValue });
            taskbar.Options.Add(new SettingOption { Label = LocalizationService.Current["Taskbar.Mode.HideTopMost"], Value = TaskbarVisibility.HideTopMostOnlyValue });
            module.Fields.Add(taskbar);

            if (autoStart.IsSupported)
            {
                module.Fields.Add(new SettingField
                {
                    Key = AutoStartKey,
                    Title = LocalizationService.Current["Settings.AutoStart"],
                    FieldType = SettingFieldType.Toggle,
                    ShouldSync = false,
                    BoolValue = autoStart.IsEnabled
                });
            }

            module.Fields.Add(new SettingField
            {
                Key = RestoreOpenNotesKey,
                Title = LocalizationService.Current["Settings.RestoreOpenNotes"],
                FieldType = SettingFieldType.Toggle,
                ShouldSync = false,
                BoolValue = true
            });

            module.Fields.Add(new SettingField
            {
                Key = DiagnoseKey,
                Title = LocalizationService.Current["Settings.Diagnose"],
                FieldType = SettingFieldType.Toggle,
                ShouldSync = false,
                BoolValue = false
            });

            SettingField language = new SettingField
            {
                Key = LocalizationSettings.LanguageKey,
                Title = "Language",
                FieldType = SettingFieldType.Select,
                ShouldSync = true
            };
            foreach (string culture in LocalizationService.AvailableCultures)
            {
                language.Options.Add(new SettingOption { Label = LocalizationService.NativeName(culture), Value = culture });
            }
            module.Fields.Add(language);


            SettingField theme = new SettingField
            {
                Key = ThemePreference.SettingKey,
                Title = LocalizationService.Current["Settings.Theme"],
                FieldType = SettingFieldType.Select,
                ShouldSync = true,
                Value = ThemePreference.DefaultValue
            };
            theme.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Theme.System"], Value = ThemePreference.SystemValue });
            theme.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Theme.Light"], Value = ThemePreference.LightValue });
            theme.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Theme.Dark"], Value = ThemePreference.DarkValue });
            module.Fields.Add(theme);

            module.Fields.Add(new SettingField
            {
                Key = "log.maxSizeKb",
                Title = LocalizationService.Current["Settings.Log.MaxSize"],
                FieldType = SettingFieldType.Text,
                ShouldSync = false
            });

            module.Fields.Add(new SettingField
            {
                Key = ReminderActivateOnFireKey,
                Title = LocalizationService.Current["Settings.Reminder.ActivateOnFire"],
                FieldType = SettingFieldType.Toggle,
                ShouldSync = true,
                BoolValue = true
            });

            if (showTutorial is not null)
            {
                module.Actions.Add(new SettingAction
                {
                    Key = "tutorial.show",
                    Label = LocalizationService.Current["Settings.Tutorial.Show"],
                    ExecuteAsync = showTutorial
                });
            }

            module.Actions.Add(new SettingAction
            {
                Key = "migration.ids",
                Label = LocalizationService.Current["Settings.Migration.RunIds"],
                ExecuteAsync = MigrateNoteIdsAsync
            });

            if (deleteAllData is not null)
            {
                module.Actions.Add(new SettingAction
                {
                    Key = "data.deleteAll",
                    Label = LocalizationService.Current["Settings.Data.DeleteAll"],
                    ExecuteAsync = deleteAllData
                });
            }

            if (OperatingSystem.IsWindows())
            {
                module.Actions.Add(new SettingAction
                {
                    Key = "notifications.unregister",
                    Label = LocalizationService.Current["Settings.Notifications.Unregister"],
                    ExecuteAsync = UnregisterToastIdentityAsync
                });
            }

            return module;
        }

        /// <summary>
        /// Removes the per-user Windows AppUserModelID registration and Start Menu shortcut that let
        /// toast notifications display, reversing what <see cref="WindowsToastRegistration.Ensure"/>
        /// writes at startup. Both are re-created on the next launch; this is for cleanup on uninstall.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static Task<string> UnregisterToastIdentityAsync()
        {
            bool removed = WindowsToastRegistration.Unregister(
                YASN.Notifications.RustNotificationSender.AppId,
                YASN.Notifications.RustNotificationSender.AppDisplayName);
            string key = removed
                ? "Settings.Notifications.Unregister.Ok"
                : "Settings.Notifications.Unregister.NothingToDo";
            return Task.FromResult(LocalizationService.Current[key]);
        }

        /// <summary>
        /// Runs the note-store schema migration (which collapses legacy integer ids into GUIDs) over
        /// the current data directory and reports the outcome. Idempotent: re-running on an
        /// already-migrated store is a no-op.
        /// </summary>
        private static Task<string> MigrateNoteIdsAsync()
        {
            MigrationReport report = WpfNoteStorageMigrator.Migrate(AppPaths.DataDirectory);
            string key = report.Status switch
            {
                MigrationStatus.Migrated => "Settings.Migration.Ok",
                MigrationStatus.Failed => "Settings.Migration.Failed",
                _ => "Settings.Migration.NothingToDo"
            };
            return Task.FromResult(LocalizationService.Current[key]);
        }

        private static SettingModule BuildSyncModule()
        {
            SettingModule module = new SettingModule
            {
                Key = "sync",
                Title = LocalizationService.Current["Settings.Sync.Module"]
            };

            SettingField enabled = new SettingField
            {
                Key = SyncSettings.EnabledKey,
                Title = LocalizationService.Current["Settings.Sync.Enabled"],
                FieldType = SettingFieldType.Toggle,
                ShouldSync = false,
                BoolValue = false
            };
            module.Fields.Add(enabled);

            module.Fields.Add(new SettingField
            {
                Key = SyncSettings.UrlKey,
                Title = LocalizationService.Current["Settings.Sync.Url"],
                FieldType = SettingFieldType.Text,
                ShouldSync = false
            });

            module.Fields.Add(new SettingField
            {
                Key = SyncSettings.UserKey,
                Title = LocalizationService.Current["Settings.Sync.User"],
                FieldType = SettingFieldType.Text,
                ShouldSync = false
            });

            module.Fields.Add(new SettingField
            {
                Key = SyncSettings.PasswordKey,
                Title = LocalizationService.Current["Settings.Sync.Password"],
                FieldType = SettingFieldType.Password,
                ShouldSync = false
            });

            module.Fields.Add(new SettingField
            {
                Key = SyncSettings.RemoteDirKey,
                Title = LocalizationService.Current["Settings.Sync.RemoteDir"],
                FieldType = SettingFieldType.Text,
                ShouldSync = false,
                Value = SyncSettings.DefaultRemoteDir
            });

            module.Fields.Add(new SettingField
            {
                Key = SyncSettings.IntervalSecondsKey,
                Title = LocalizationService.Current["Settings.Sync.Interval"],
                FieldType = SettingFieldType.Number,
                ShouldSync = false,
                Minimum = SyncSettings.MinIntervalSeconds,
                Maximum = 86400,
                Value = SyncSettings.DefaultIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            module.Fields.Add(new SettingField
            {
                Key = SyncSettings.DeleteGateThresholdKey,
                Title = LocalizationService.Current["Settings.Sync.DeleteGate"],
                Description = LocalizationService.Current["Settings.Sync.DeleteGate.Description"],
                FieldType = SettingFieldType.Number,
                ShouldSync = false,
                Minimum = SyncSettings.MinDeleteGateThreshold,
                Maximum = 1000,
                Value = SyncSettings.DefaultDeleteGateThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            SettingField changeDetection = new SettingField
            {
                Key = SyncSettings.ChangeDetectionKey,
                Title = LocalizationService.Current["Settings.Sync.ChangeDetection"],
                Description = LocalizationService.Current["Settings.Sync.ChangeDetection.Description"],
                FieldType = SettingFieldType.Select,
                ShouldSync = false,
                Value = ChangeDetection.ETagValue
            };
            changeDetection.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Sync.ChangeDetection.ETag"], Value = ChangeDetection.ETagValue });
            changeDetection.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Sync.ChangeDetection.LastModified"], Value = ChangeDetection.LastModifiedValue });
            module.Fields.Add(changeDetection);

            module.Actions.Add(new SettingAction
            {
                Key = "sync.test",
                Label = LocalizationService.Current["Settings.Sync.Test"],
                ExecuteAsync = () => TestConnectionAsync(module)
            });

            // The sync-detail fields (everything except the Enabled toggle) and the test-connection
            // action only apply when sync is on. Hide them while sync is off, but keep each field's
            // in-memory Value/BoolValue so entered content is preserved and still persists on save.
            // Driven live off the Enabled toggle's change hook, with the initial state seeded here
            // (ApplyValues re-fires this via BoolValue when a value is loaded).
            void SyncDetailVisibility(SettingField toggle)
            {
                foreach (SettingField field in module.Fields)
                {
                    if (!ReferenceEquals(field, toggle))
                    {
                        field.IsFieldVisible = toggle.BoolValue;
                    }
                }

                foreach (SettingAction action in module.Actions)
                {
                    action.IsActionVisible = toggle.BoolValue;
                }
            }

            enabled.OnChanged = SyncDetailVisibility;
            SyncDetailVisibility(enabled);

            return module;
        }

        private static async Task<string> TestConnectionAsync(SettingModule module)
        {
            string Url() => module.Fields.First(f => f.Key == SyncSettings.UrlKey).Value;
            string User() => module.Fields.First(f => f.Key == SyncSettings.UserKey).Value;
            string Password() => module.Fields.First(f => f.Key == SyncSettings.PasswordKey).Value;
            string Dir() => module.Fields.First(f => f.Key == SyncSettings.RemoteDirKey).Value.Trim().Trim('/');
            string Mode() => module.Fields.FirstOrDefault(f => f.Key == SyncSettings.ChangeDetectionKey)?.Value ?? ChangeDetection.ETagValue;

            if (string.IsNullOrWhiteSpace(Url()))
            {
                return LocalizationService.Current["Settings.Sync.Test.Fail"];
            }

            try
            {
                using WebDavSyncClient client = new WebDavSyncClient(new WebDavOptions
                {
                    ServerUrl = Url(),
                    Username = User(),
                    Password = Password()
                });

                string remote = Dir().Length == 0 ? SyncSettings.DefaultRemoteDir : Dir();
                SyncProbeResult probe = await client.ProbeConnectionAsync(remote).ConfigureAwait(false);

                if (!probe.IsUsable)
                {
                    string message = LocalizationService.Current[ProbeFailureKey(probe.Status)];

                    // Surface the backend's specific reason (e.g. "Parent directory is missing.") when it
                    // carried one, so a missing remote folder is distinguishable from a permissions fault.
                    return string.IsNullOrWhiteSpace(probe.Detail)
                        ? message
                        : $"{message} ({probe.Detail})";
                }

                // Connected and read/write verified. If ETag detection is selected but the server omits
                // ETags, warn — that mode would silently mask remote edits.
                if (ChangeDetection.Parse(Mode()) == ChangeDetectionMode.ETag && !probe.ServerReturnsETags)
                {
                    return LocalizationService.Current["Settings.Sync.Test.NoETag"];
                }

                return LocalizationService.Current["Settings.Sync.Test.Ok"];
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                return LocalizationService.Current["Settings.Sync.Test.Fail"];
            }
        }

        /// <summary>Maps a non-Ok probe status to its localized, actionable message key.</summary>
        private static string ProbeFailureKey(SyncProbeStatus status) => status switch
        {
            SyncProbeStatus.BadCredentials => "Settings.Sync.Test.BadCredentials",
            SyncProbeStatus.WebDavDisabled => "Settings.Sync.Test.WebDavDisabled",
            SyncProbeStatus.EndpointNotFound => "Settings.Sync.Test.EndpointNotFound",
            SyncProbeStatus.DirectoryUnavailable => "Settings.Sync.Test.DirectoryUnavailable",
            SyncProbeStatus.ReadWriteFailed => "Settings.Sync.Test.ReadWriteFailed",
            SyncProbeStatus.Unreachable => "Settings.Sync.Test.Unreachable",
            _ => "Settings.Sync.Test.Fail"
        };

        private static SettingModule BuildAttachmentsModule()
        {
            SettingModule module = new SettingModule
            {
                Key = "attachments",
                Title = LocalizationService.Current["Settings.Attachments"]
            };

            module.Fields.Add(new SettingField
            {
                Key = AttachmentAutoSyncEnabledKey,
                Title = LocalizationService.Current["Settings.Attachments.Toggle"],
                Description = LocalizationService.Current["Settings.Attachments.Toggle.Description"],
                FieldType = SettingFieldType.Toggle,
                ShouldSync = true,
                BoolValue = true
            });

            module.Fields.Add(new SettingField
            {
                Key = AttachmentThresholdMbKey,
                Title = LocalizationService.Current["Settings.Attachments.Threshold"],
                FieldType = SettingFieldType.Number,
                ShouldSync = true,
                Minimum = 1,
                Maximum = 1024,
                Value = DefaultAttachmentThresholdMb.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            return module;
        }

        private static SettingModule BuildEditorModule()
        {
            SettingModule module = new SettingModule
            {
                Key = "editor",
                Title = LocalizationService.Current["Settings.Editor"]
            };

            module.Fields.Add(new SettingField
            {
                Key = NoteMinWidthKey,
                Title = LocalizationService.Current["Settings.Editor.MinWidth"],
                Description = LocalizationService.Current["Settings.Editor.MinWidth.Description"],
                FieldType = SettingFieldType.Number,
                ShouldSync = true,
                Minimum = 360,
                Maximum = 1200,
                Value = DefaultNoteMinWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            SettingField previewStyle = new SettingField
            {
                Key = PreviewStyleManager.SettingKey,
                Title = LocalizationService.Current["Settings.PreviewStyle"],
                FieldType = SettingFieldType.Select,
                ShouldSync = true,
                Value = PreviewStyleManager.DefaultStyleRelativePath
            };
            foreach (string style in PreviewStyleManager.ListStyles())
            {
                previewStyle.Options.Add(new SettingOption { Label = StyleDisplayLabel(style), Value = style });
            }
            module.Fields.Add(previewStyle);

            SettingField doubleClick = new SettingField
            {
                Key = PreviewDoubleClickActionKey,
                Title = LocalizationService.Current["Settings.Editor.PreviewDoubleClick"],
                Description = LocalizationService.Current["Settings.Editor.PreviewDoubleClick.Description"],
                FieldType = SettingFieldType.Select,
                ShouldSync = true,
                Value = PreviewDoubleClickInApp
            };
            doubleClick.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Editor.PreviewDoubleClick.None"], Value = PreviewDoubleClickNone });
            doubleClick.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Editor.PreviewDoubleClick.InApp"], Value = PreviewDoubleClickInApp });
            doubleClick.Options.Add(new SettingOption { Label = LocalizationService.Current["Settings.Editor.PreviewDoubleClick.External"], Value = PreviewDoubleClickExternal });
            module.Fields.Add(doubleClick);

            return module;
        }

        /// <summary>
        /// Derives the dropdown label for a preview style: the relative path with its trailing
        /// ".css" extension stripped (e.g. "default.css" → "default", "themes/mono.css" →
        /// "themes/mono"). Any subfolder structure is preserved.
        /// </summary>
        /// <param name="styleRelativePath">The style file's data-dir-relative path.</param>
        private static string StyleDisplayLabel(string styleRelativePath)
        {
            return styleRelativePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                ? styleRelativePath[..^4]
                : styleRelativePath;
        }
    }
}
