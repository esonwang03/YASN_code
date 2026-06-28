using System.Globalization;
using YASN.Infrastructure.Settings;

namespace YASN.Localization
{
    /// <summary>
    /// Provides runtime-switchable localized strings for Avalonia UI surfaces.
    /// </summary>
    public sealed class LocalizationService
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Catalog =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["App.Title"] = "YASN",
                    ["Language.NativeName"] = "English",
                    ["Menu.NewNote"] = "New note",
                    ["Menu.OpenNote"] = "Open note",
                    ["Menu.ManageNotes"] = "Manage notes",
                    ["Menu.Exit"] = "Exit",
                    ["Window.Close"] = "Close",
                    ["Window.Taskbar"] = "Taskbar",
                    ["Window.SetReminder"] = "Set reminder",
                    ["Window.EditorMode.Hint"] = "Switch editor view",
                    ["Window.EditorMode.PreviewOnly"] = "Preview",
                    ["Window.EditorMode.TextOnly"] = "Edit",
                    ["Window.EditorMode.TextAndPreview"] = "Split",
                    ["Window.QuickLayout"] = "Quick layout",
                    ["Window.QuickLayout.Hint"] = "Click a monitor to move · drag to resize · Esc to cancel",
                    ["Window.QuickLayout.TooSmall"] = "Too small — release to reposition at the current size",
                    ["Window.Resize.Grip"] = "Drag to resize",
                    ["Editor.InsertImage"] = "Insert image",
                    ["Editor.InsertAttachment"] = "Insert attachment",
                    ["Window.Level.Normal"] = "Normal",
                    ["Window.Level.TopMost"] = "Topmost",
                    ["Window.Level.BottomMost"] = "Bottom",
                    ["Window.Menu.Manage"] = "Open Manage Window",
                    ["Reminder.Save"] = "Save",
                    ["Reminder.Clear"] = "Clear",
                    ["Reminder.Manager.Title"] = "Reminders in this note",
                    ["Reminder.Manager.Empty"] = "No reminders in this note.",
                    ["Reminder.Manager.Close"] = "Close",
                    ["Reminder.Manager.Edit"] = "Edit",
                    ["Reminder.Manager.Delete"] = "Delete",
                    ["Reminder.Manager.Enable"] = "Enable",
                    ["Reminder.Manager.Disable"] = "Disable",
                    ["Reminder.Manager.Disabled"] = "Disabled",
                    ["Reminder.Manager.Recurring"] = "Recurring",
                    ["Reminder.Manager.Once"] = "Once",
                    ["Reminder.Manager.TimesLeft"] = "{0} times left",
                    ["Reminder.NextFire"] = "Next: {0}",
                    ["Settings.Title"] = "Settings",
                    ["Menu.Settings"] = "Settings",
                    ["Settings.Save"] = "Save",
                    ["Settings.Language"] = "Language",
                    ["Main.Title"] = "Notes",
                    ["Main.Empty"] = "No notes yet. Create one to get started.",
                    ["Main.Create.Normal"] = "New (normal)",
                    ["Main.Create.TopMost"] = "New (topmost)",
                    ["Main.Create.BottomMost"] = "New (bottom)",
                    ["Main.Refresh"] = "Refresh",
                    ["Main.Settings"] = "Settings",
                    ["Main.OpenDataFolder"] = "Open data folder",
                    ["Main.OpenCacheFolder"] = "Open cache folder",
                    ["Main.HideToTray"] = "Hide to tray",
                    ["Main.Open"] = "Open",
                    ["Main.Close"] = "Close",
                    ["Main.Delete"] = "Delete",
                    ["Main.QuickLayout"] = "Quick layout",
                    ["Main.Status.Open"] = "Open",
                    ["Main.Status.Closed"] = "Closed",
                    ["Main.Delete.Confirm.Title"] = "Delete note",
                    ["Main.Delete.Confirm.Body"] = "Delete this note permanently? This cannot be undone.",
                    ["Rename.MenuItem"] = "Rename",
                    ["Rename.Title"] = "Rename note",
                    ["Rename.Save"] = "Save",
                    ["Rename.Cancel"] = "Cancel",
                    ["Rename.Empty"] = "Title can't be empty.",
                    ["Rename.Duplicate"] = "A note with that title already exists.",
                    ["Settings.Taskbar"] = "Show notes in taskbar",
                    ["Taskbar.Mode.AlwaysShow"] = "Always show",
                    ["Taskbar.Mode.AlwaysHide"] = "Never show",
                    ["Taskbar.Mode.HideTopMost"] = "Hide when topmost",
                    ["Settings.Browse"] = "Browse…",
                    ["Settings.Toggle.On"] = "On",
                    ["Settings.Toggle.Off"] = "Off",
                    ["Settings.Tutorial.Show"] = "Show tutorial note",
                    ["Settings.Tutorial.Added"] = "Tutorial note added.",
                    ["Settings.Migration.RunIds"] = "Migrate note IDs to GUID",
                    ["Settings.Migration.Running"] = "Migrating note IDs…",
                    ["Settings.Migration.Ok"] = "Note IDs migrated.",
                    ["Settings.Migration.NothingToDo"] = "Note IDs are already up to date.",
                    ["Settings.Migration.Failed"] = "Note ID migration failed. See the log for details.",
                    ["Settings.Reminder.ActivateOnFire"] = "Activate note and scroll to reminder when it fires",
                    ["Settings.RestoreOpenNotes"] = "Reopen notes that were open last time",
                    ["Settings.Diagnose"] = "Diagnose mode (show log console and preview developer tools)",
                    ["Settings.Data.DeleteAll"] = "Delete all data and quit",
                    ["Settings.Data.DeleteAll.Confirm.Title"] = "Delete all data",
                    ["Settings.Data.DeleteAll.Confirm.Body"] = "Permanently delete all notes, settings, and cached data on this computer? This cannot be undone.",
                    ["Settings.Data.DeleteAll.Cancelled"] = "Deletion cancelled.",
                    ["Settings.Notifications.Unregister"] = "Unregister notifications",
                    ["Settings.Notifications.Unregister.Ok"] = "Notification registration removed.",
                    ["Settings.Notifications.Unregister.NothingToDo"] = "No notification registration found.",
                    ["Settings.AutoStart"] = "Launch at sign in.",
                    ["Settings.Theme"] = "Theme",
                    ["Settings.Theme.System"] = "Follow system",
                    ["Settings.Theme.Light"] = "Light",
                    ["Settings.Theme.Dark"] = "Dark",
                    ["Settings.PreviewStyle"] = "Preview style",
                    ["Settings.Unrecognized.Title"] = "Some settings were not recognized",
                    ["Settings.Unrecognized.Body"] = "Old configuration from a previous version was found and is being ignored. Review your settings to re-apply them.",
                    ["App.Unhandled.Title"] = "An unexpected error occurred",
                    ["App.Unhandled.Body"] = "An unexpected error occurred and was logged. The app is still running.",
                    ["Settings.DataDir.Restart"] = "The data folder change takes effect after restarting the app.",
                    ["Settings.DataDir.Invalid"] = "That folder path is not valid.",
                    ["Settings.DataDir.Description"] = "Takes effect after restart.",
                    ["Sync.Now"] = "Sync now",
                    ["Sync.Confirm.Title"] = "Confirm sync deletions",
                    ["Sync.Confirm.Body"] = "This sync will delete the following notes. Review before continuing.",
                    ["Sync.Confirm.DeleteLocal"] = "Delete here (removed on another device)",
                    ["Sync.Confirm.DeleteRemote"] = "Delete on server (removed here)",
                    ["Sync.Confirm.Proceed"] = "Apply deletions",
                    ["Sync.Confirm.Cancel"] = "Cancel sync",
                    ["Sync.Conflict.Row"] = "Sync conflict — keep one copy, then mark solved.",
                    ["Sync.Resolve.MenuItem"] = "Mark solved",
                    ["Sync.Resolve.Failed"] = "Could not resolve the conflict.",
                    ["Sync.Resolve.None"] = "No note found for this conflict.",
                    ["Sync.Resolve.Duplicates"] = "Delete the duplicate copies first, leaving one note.",
                    ["Settings.Sync.Module"] = "Sync",
                    ["Settings.Sync.Enabled"] = "Enable sync",
                    ["Settings.Sync.Url"] = "WebDAV server URL",
                    ["Settings.Sync.User"] = "Username",
                    ["Settings.Sync.Password"] = "Password / token",
                    ["Settings.Sync.RemoteDir"] = "Remote directory",
                    ["Settings.Sync.Interval"] = "Sync interval (seconds)",
                    ["Settings.Sync.DeleteGate"] = "Confirm deletions at",
                    ["Settings.Sync.DeleteGate.Description"] = "Ask before a sync deletes this many notes or more. Set to 1 to confirm every deletion.",
                    ["Settings.Sync.Test"] = "Test connection",
                    ["Settings.Sync.Test.Ok"] = "Connection succeeded.",
                    ["Settings.Sync.Test.Fail"] = "Connection failed. Check the URL and credentials.",
                    ["Settings.Sync.Test.BadCredentials"] = "Authentication failed. Check the username and password.",
                    ["Settings.Sync.Test.WebDavDisabled"] = "The server reached, but WebDAV is not enabled for this account.",
                    ["Settings.Sync.Test.EndpointNotFound"] = "No WebDAV endpoint at this URL. Check the address (it may need a path like /webdav).",
                    ["Settings.Sync.Test.DirectoryUnavailable"] = "Connected, but the remote folder could not be created. It (or its parent) may not exist — check that the remote path is correct and that you have permission to create it.",
                    ["Settings.Sync.Test.ReadWriteFailed"] = "Connected, but a test file could not be written and read back. Check write permissions and quota.",
                    ["Settings.Sync.Test.Unreachable"] = "Could not reach the server. Check the URL, network, and certificate.",
                    ["Settings.Sync.Test.NoETag"] = "Connected, but the server does not return ETags. Switch change detection to Last-Modified.",
                    ["Settings.Sync.ChangeDetection"] = "Change detection",
                    ["Settings.Sync.ChangeDetection.Description"] = "How remote edits are detected. Use ETag when supported; switch to Last-Modified for servers that omit ETags.",
                    ["Settings.Sync.ChangeDetection.ETag"] = "ETag (recommended)",
                    ["Settings.Sync.ChangeDetection.LastModified"] = "Last-Modified",
                    ["Sync.ETag.Unsupported.Title"] = "Sync change detection",
                    ["Sync.ETag.Unsupported.Body"] = "The server does not return ETags. Open Settings and switch change detection to Last-Modified.",
                    ["Sync.Notify.Started.Title"] = "Sync started",
                    ["Sync.Notify.Started.Body"] = "YASN is syncing notes.",
                    ["Sync.Notify.Complete.Title"] = "Sync complete",
                    ["Sync.Notify.Complete.Body"] = "{0} uploaded / {1} downloaded / {2} deleted",
                    ["Sync.Notify.Failed.Title"] = "Sync failed",
                    ["Sync.Notify.Disabled.Title"] = "Sync is disabled",
                    ["Sync.Notify.Disabled.Body"] = "Sync is disabled. Enable it in Settings.",
                    ["Settings.Shortcuts.Module"] = "Shortcuts",
                    ["Settings.Shortcuts.Reset"] = "Reset",
                    ["Settings.Shortcuts.Conflict"] = "The shortcut {0} is already used by '{1}'. Change it before saving '{2}'.",
                    ["Settings.Shortcuts.ConflictInline"] = "Conflicts with '{0}'.",
                    ["Hotkey.RaiseMainWindow"] = "Open note manager (global)",
                    ["Hotkey.RaiseSettingsWindow"] = "Open settings (global)",
                    ["Hotkey.CreateNote"] = "New note (global)",
                    ["Hotkey.InsertImage"] = "Insert image",
                    ["Hotkey.InsertAttachment"] = "Insert attachment",
                    ["Hotkey.CycleEditorMode"] = "Switch editor view",
                    ["Hotkey.CycleWindowLevel"] = "Cycle window level",
                    ["Hotkey.QuickLayout"] = "Quick layout",
                    ["Hotkey.ToggleChrome"] = "Show/hide title bar",
                    ["Window.ToggleChrome"] = "Show/hide title bar",
                },
                ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["App.Title"] = "YASN",
                    ["Language.NativeName"] = "中文（简体）",
                    ["Menu.NewNote"] = "创建便签",
                    ["Menu.OpenNote"] = "打开便签",
                    ["Menu.ManageNotes"] = "管理便签",
                    ["Menu.Exit"] = "退出",
                    ["Window.Close"] = "关闭",
                    ["Window.Taskbar"] = "任务栏",
                    ["Window.SetReminder"] = "设置提醒事项",
                    ["Window.EditorMode.Hint"] = "切换编辑视图",
                    ["Window.EditorMode.PreviewOnly"] = "预览",
                    ["Window.EditorMode.TextOnly"] = "编辑",
                    ["Window.EditorMode.TextAndPreview"] = "双栏",
                    ["Window.QuickLayout"] = "快速布局",
                    ["Window.QuickLayout.Hint"] = "点击窗口缩略图以移动 · 拖动以缩放 · 按下 Esc 退出",
                    ["Window.QuickLayout.TooSmall"] = "窗口尺寸过小 — 松开鼠标将只进行移动",
                    ["Window.Resize.Grip"] = "拖动以缩放",
                    ["Editor.InsertImage"] = "插入图片",
                    ["Editor.InsertAttachment"] = "插入文件附件",
                    ["Window.Level.Normal"] = "普通",
                    ["Window.Level.TopMost"] = "置顶",
                    ["Window.Level.BottomMost"] = "置底",
                    ["Window.Menu.Manage"] = "打开管理窗口",
                    ["Reminder.Save"] = "保存",
                    ["Reminder.Clear"] = "清除",
                    ["Reminder.Manager.Title"] = "便签中的提醒事项",
                    ["Reminder.Manager.Empty"] = "便签中无提醒事项",
                    ["Reminder.Manager.Close"] = "关闭",
                    ["Reminder.Manager.Edit"] = "编辑",
                    ["Reminder.Manager.Delete"] = "删除",
                    ["Reminder.Manager.Enable"] = "启用",
                    ["Reminder.Manager.Disable"] = "禁用",
                    ["Reminder.Manager.Disabled"] = "已禁用",
                    ["Reminder.Manager.Recurring"] = "多次",
                    ["Reminder.Manager.Once"] = "单次",
                    ["Reminder.Manager.TimesLeft"] = "剩余 {0} 次",
                    ["Reminder.NextFire"] = "下次触发时间: {0}",
                    ["Settings.Title"] = "设置",
                    ["Menu.Settings"] = "设置",
                    ["Settings.Save"] = "保存",
                    ["Settings.Language"] = "语言",
                    ["Main.Title"] = "便签",
                    ["Main.Empty"] = "没有便签，创建以进行管理",
                    ["Main.Create.Normal"] = "创建 (普通)",
                    ["Main.Create.TopMost"] = "创建 (置顶)",
                    ["Main.Create.BottomMost"] = "创建 (置底)",
                    ["Main.Refresh"] = "刷新",
                    ["Main.Settings"] = "设置",
                    ["Main.OpenDataFolder"] = "打开数据目录",
                    ["Main.OpenCacheFolder"] = "打开缓存目录",
                    ["Main.HideToTray"] = "隐藏到托盘图标",
                    ["Main.Open"] = "打开",
                    ["Main.Close"] = "关闭",
                    ["Main.Delete"] = "删除",
                    ["Main.QuickLayout"] = "快速布局",
                    ["Main.Status.Open"] = "打开",
                    ["Main.Status.Closed"] = "关闭",
                    ["Main.Delete.Confirm.Title"] = "删除便签",
                    ["Main.Delete.Confirm.Body"] = "是否删除便签？这个操作不能被撤销。",
                    ["Rename.MenuItem"] = "重命名",
                    ["Rename.Title"] = "重命名便签",
                    ["Rename.Save"] = "保存",
                    ["Rename.Cancel"] = "取消",
                    ["Rename.Empty"] = "便签标题不能为空",
                    ["Rename.Duplicate"] = "标题与已有笔记重复。",
                    ["Settings.Taskbar"] = "在任务栏展示便签窗口图标",
                    ["Taskbar.Mode.AlwaysShow"] = "始终展示",
                    ["Taskbar.Mode.AlwaysHide"] = "始终不展示",
                    ["Taskbar.Mode.HideTopMost"] = "在置顶模式下不展示",
                    ["Settings.Browse"] = "浏览…",
                    ["Settings.Toggle.On"] = "启用",
                    ["Settings.Toggle.Off"] = "停止",
                    ["Settings.Tutorial.Show"] = "打开教程便签",
                    ["Settings.Tutorial.Added"] = "已添加教程便签。",
                    ["Settings.Migration.RunIds"] = "将便签ID迁移到GUID",
                    ["Settings.Migration.Running"] = "正在迁移便签ID…",
                    ["Settings.Migration.Ok"] = "便签ID已迁移。",
                    ["Settings.Migration.NothingToDo"] = "便签ID已为最新。",
                    ["Settings.Migration.Failed"] = "Note ID migration failed. See the log for details.",
                    ["Settings.Reminder.ActivateOnFire"] = "在提醒事项触发时，打开便签窗口并滚动到提醒事项。",
                    ["Settings.RestoreOpenNotes"] = "重新打开最后打开的笔记",
                    ["Settings.Diagnose"] = "诊断模式 (启用日志终端和Webview开发者工具)",
                    ["Settings.Data.DeleteAll"] = "删除所有数据并退出(为卸载准备)",
                    ["Settings.Data.DeleteAll.Confirm.Title"] = "删除所有数据",
                    ["Settings.Data.DeleteAll.Confirm.Body"] = "是否永久删除所有的便签，配置和缓存数据？这个操作不能撤销。",
                    ["Settings.Data.DeleteAll.Cancelled"] = "取消删除。",
                    ["Settings.Notifications.Unregister"] = "注销系统通知",
                    ["Settings.Notifications.Unregister.Ok"] = "系统通知已注销。",
                    ["Settings.Notifications.Unregister.NothingToDo"] = "未注册系统通知。",
                    ["Settings.AutoStart"] = "登陆时自动启动",
                    ["Settings.Theme"] = "主题",
                    ["Settings.Theme.System"] = "跟随系统",
                    ["Settings.Theme.Light"] = "白天模式",
                    ["Settings.Theme.Dark"] = "黑夜模式",
                    ["Settings.PreviewStyle"] = "便签样式",
                    ["Settings.Unrecognized.Title"] = "某些设置键未被识别。",
                    ["Settings.Unrecognized.Body"] = "旧版本的配置被发现并被忽，删除这些旧的键后这个提示将不再显示。",
                    ["App.Unhandled.Title"] = "发生异常",
                    ["App.Unhandled.Body"] = "发生异常并记录. 软件仍可正常运行.",
                    ["Settings.DataDir.Restart"] = "对数据目录位置的修改将在应用重启后生效。",
                    ["Settings.DataDir.Invalid"] = "非法的文件夹。",
                    ["Settings.DataDir.Description"] = "应用重启后生效。",
                    ["Sync.Now"] = "立即同步",
                    ["Sync.Confirm.Title"] = "确认在同步中将要删除的文件",
                    ["Sync.Confirm.Body"] = "本次同步将会删除以下的文件，请在继续同步前确认",
                    ["Sync.Confirm.DeleteLocal"] = "在此处删除（已在另一台设备上移除）",
                    ["Sync.Confirm.DeleteRemote"] = "从服务器删除（已在此处移除）",
                    ["Sync.Confirm.Proceed"] = "应用删除",
                    ["Sync.Confirm.Cancel"] = "取消同步",
                    ["Sync.Conflict.Row"] = "同步冲突——保留一个副本，然后标记为已解决。",
                    ["Sync.Resolve.MenuItem"] = "标记为已解决",
                    ["Sync.Resolve.Failed"] = "无法解决该冲突。",
                    ["Sync.Resolve.None"] = "未找到与此冲突对应的笔记。",
                    ["Sync.Resolve.Duplicates"] = "请先删除重复副本，只保留一条笔记。",
                    ["Settings.Sync.Module"] = "同步",
                    ["Settings.Sync.Enabled"] = "启用同步",
                    ["Settings.Sync.Url"] = "WebDAV 服务器 URL",
                    ["Settings.Sync.User"] = "用户名",
                    ["Settings.Sync.Password"] = "密码 / 令牌",
                    ["Settings.Sync.RemoteDir"] = "远程目录",
                    ["Settings.Sync.Interval"] = "同步间隔（秒）",
                    ["Settings.Sync.DeleteGate"] = "删除确认阈值",
                    ["Settings.Sync.DeleteGate.Description"] = "当一次同步将删除的笔记数量达到或超过此值时，先询问确认。设为 1 表示每次删除都需要确认。",
                    ["Settings.Sync.Test"] = "测试连接",
                    ["Settings.Sync.Test.Ok"] = "连接成功。",
                    ["Settings.Sync.Test.Fail"] = "连接失败。请检查 URL 和凭据。",
                    ["Settings.Sync.Test.BadCredentials"] = "身份验证失败。请检查用户名和密码。",
                    ["Settings.Sync.Test.WebDavDisabled"] = "已连接到服务器，但此账号未启用 WebDAV。",
                    ["Settings.Sync.Test.EndpointNotFound"] = "此 URL 没有 WebDAV 端点。请检查地址（可能需要包含类似 /webdav 的路径）。",
                    ["Settings.Sync.Test.DirectoryUnavailable"] = "已连接，但无法创建远程文件夹。该文件夹或其父级可能不存在——请检查远程路径是否正确，以及你是否有创建权限。",
                    ["Settings.Sync.Test.ReadWriteFailed"] = "已连接，但无法写入并读回测试文件。请检查写入权限和存储配额。",
                    ["Settings.Sync.Test.Unreachable"] = "无法连接到服务器。请检查 URL、网络和证书。",
                    ["Settings.Sync.Test.NoETag"] = "已连接，但服务器未返回 ETag。请将变更检测方式切换为 Last-Modified。",
                    ["Settings.Sync.ChangeDetection"] = "变更检测",
                    ["Settings.Sync.ChangeDetection.Description"] = "用于检测远程编辑的方式。服务器支持时使用 ETag；对于不返回 ETag 的服务器，请切换为 Last-Modified。",
                    ["Settings.Sync.ChangeDetection.ETag"] = "ETag（推荐）",
                    ["Settings.Sync.ChangeDetection.LastModified"] = "Last-Modified",
                    ["Sync.ETag.Unsupported.Title"] = "同步变更检测",
                    ["Sync.ETag.Unsupported.Body"] = "服务器未返回 ETag。请打开设置，并将变更检测方式切换为 Last-Modified。",
                    ["Sync.Notify.Started.Title"] = "同步已开始",
                    ["Sync.Notify.Started.Body"] = "YASN 正在同步笔记。",
                    ["Sync.Notify.Complete.Title"] = "同步完成",
                    ["Sync.Notify.Complete.Body"] = "{0} 个已上传 / {1} 个已下载 / {2} 个已删除",
                    ["Sync.Notify.Failed.Title"] = "同步失败",
                    ["Sync.Notify.Disabled.Title"] = "同步已禁用",
                    ["Sync.Notify.Disabled.Body"] = "同步已禁用。请在设置中启用。",
                    ["Settings.Shortcuts.Module"] = "快捷键",
                    ["Settings.Shortcuts.Reset"] = "重置",
                    ["Settings.Shortcuts.Conflict"] = "快捷键 {0} 已被“{1}”使用。请先修改它，然后再保存“{2}”。",
                    ["Settings.Shortcuts.ConflictInline"] = "与“{0}”冲突。",
                    ["Hotkey.RaiseMainWindow"] = "打开笔记管理器（全局）",
                    ["Hotkey.RaiseSettingsWindow"] = "打开设置（全局）",
                    ["Hotkey.CreateNote"] = "新建笔记（全局）",
                    ["Hotkey.InsertImage"] = "插入图片",
                    ["Hotkey.InsertAttachment"] = "插入附件",
                    ["Hotkey.CycleEditorMode"] = "切换编辑器视图",
                    ["Hotkey.CycleWindowLevel"] = "循环切换窗口层级",
                    ["Hotkey.QuickLayout"] = "快速布局",
                    ["Hotkey.ToggleChrome"] = "显示/隐藏标题栏",
                    ["Window.ToggleChrome"] = "显示/隐藏标题栏",
                }
            };

        private string currentCulture = "en";
        private readonly SettingsStore? settingsStore;

        /// <summary>
        /// Initializes a localization service without persistence.
        /// </summary>
        public LocalizationService()
        {
            Strings = new LocalizedStrings(this);
        }

        /// <summary>
        /// Initializes a localization service with persisted synced language selection.
        /// </summary>
        /// <param name="settingsStore">The settings store used for language persistence.</param>
        public LocalizationService(SettingsStore settingsStore)
        {
            this.settingsStore = settingsStore;
            currentCulture = settingsStore.GetValue(LocalizationSettings.LanguageKey, shouldSync: true, LocalizationSettings.DefaultLanguage);
            if (!Catalog.ContainsKey(currentCulture))
            {
                currentCulture = LocalizationSettings.DefaultLanguage;
            }

            Strings = new LocalizedStrings(this);
        }

        /// <summary>
        /// Gets or sets the shared service used by the <c>{l:Tr}</c> markup extension.
        /// </summary>
        public static LocalizationService Current { get; set; } = new LocalizationService();

        /// <summary>
        /// Raised when the active culture changes.
        /// </summary>
        public event EventHandler? CultureChanged;

        /// <summary>
        /// Gets the change-notifying string indexer bound by views.
        /// </summary>
        public LocalizedStrings Strings { get; }

        /// <summary>
        /// Gets the active culture name.
        /// </summary>
        public string CurrentCulture => currentCulture;

        /// <summary>
        /// Gets the culture names available in the catalog, in insertion order.
        /// </summary>
        public static IReadOnlyCollection<string> AvailableCultures => (IReadOnlyCollection<string>)Catalog.Keys;

        /// <summary>
        /// Gets the native display name declared by a culture's catalog
        /// (the <c>Language.NativeName</c> key), falling back to the culture code.
        /// </summary>
        /// <param name="culture">The catalog culture code (e.g. <c>en</c>, <c>zh-CN</c>).</param>
        public static string NativeName(string culture) =>
            Catalog.TryGetValue(culture, out IReadOnlyDictionary<string, string>? strings)
            && strings.TryGetValue("Language.NativeName", out string? name)
                ? name
                : culture;

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        /// <param name="key">The localization key.</param>
        public string this[string key] => Catalog[currentCulture].TryGetValue(key, out string? value) ? value : key;

        /// <summary>
        /// Changes the active culture.
        /// </summary>
        /// <param name="cultureName">The culture to activate.</param>
        public void SetCulture(string cultureName)
        {
            string normalized = string.IsNullOrWhiteSpace(cultureName) ? "en" : cultureName.Trim();
            if (!Catalog.ContainsKey(normalized))
            {
                throw new CultureNotFoundException($"Culture '{cultureName}' is not available.");
            }

            currentCulture = normalized;
            settingsStore?.SetValue(LocalizationSettings.LanguageKey, shouldSync: true, normalized);
            CultureChanged?.Invoke(this, EventArgs.Empty);
            Strings.RaiseAllChanged();
        }
    }
}
