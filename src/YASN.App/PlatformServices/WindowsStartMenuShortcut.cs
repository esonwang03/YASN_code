using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Creates and removes the per-user Start Menu shortcut that anchors this unpackaged app's
    /// AppUserModelID (AUMID), and stamps that AUMID onto the shortcut so the toast subsystem
    /// attributes notifications to the same identity.
    /// </summary>
    /// <remarks>
    /// A Start Menu <c>.lnk</c> carrying the <c>System.AppUserModel.ID</c> property is the shell's
    /// canonical AUMID anchor for a desktop app: the OS resolves an incoming toast's AUMID against
    /// the shortcut to decide attribution and Action Center grouping. We pair it with the registry
    /// identity written by <see cref="WindowsToastRegistration"/> so both anchors agree.
    /// <para>
    /// COM is reached through source-generated interop (<see cref="GeneratedComInterfaceAttribute"/>
    /// + <see cref="StrategyBasedComWrappers"/>) rather than classic <c>ComImport</c>, because the
    /// app has an opt-in NativeAOT publish that cannot marshal runtime-generated COM RCWs.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static partial class WindowsStartMenuShortcut
    {
        // ShellLink coclass and the interfaces we drive on it.
        private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
        private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

        // PKEY_AppUserModel_ID — the property that carries the AUMID on the shortcut.
        private static readonly PropertyKey AppUserModelIdKey = new()
        {
            FmtId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            Pid = 5,
        };

        private const uint CLSCTX_INPROC_SERVER = 1;
        private const ushort VT_LPWSTR = 31;
        private const int STGM_READWRITE = 0x00000002;

        private static readonly StrategyBasedComWrappers ComWrappers = new();

        /// <summary>
        /// Writes a Start Menu shortcut named <paramref name="displayName"/> targeting the running
        /// executable and tags it with <paramref name="appUserModelId"/>. Best-effort: a failure is
        /// logged and swallowed so a locked-down profile cannot crash startup — toasts then rely on
        /// the registry identity alone.
        /// </summary>
        /// <param name="appUserModelId">The AUMID stamped onto the shortcut.</param>
        /// <param name="displayName">The shortcut file name (without extension), e.g. the app name.</param>
        public static void Ensure(string appUserModelId, string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                AppLogger.Warn("Cannot create Start Menu shortcut: process path is unknown.");
                return;
            }

            string shortcutPath = ShortcutPath(displayName);
            if (File.Exists(shortcutPath))
            {
                // The shortcut self-heals on each launch; nothing to do once it exists.
                return;
            }

            bool ownsComInit = TryInitializeCom();
            try
            {
                CreateShortcut(shortcutPath, executablePath, appUserModelId);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
            {
                AppLogger.Warn($"Failed to create Start Menu shortcut: {ex.Message}");
            }
            finally
            {
                if (ownsComInit)
                {
                    CoUninitialize();
                }
            }
        }

        /// <summary>
        /// Deletes the Start Menu shortcut written by <see cref="Ensure"/>.
        /// </summary>
        /// <param name="displayName">The shortcut file name (without extension) used at creation.</param>
        /// <returns><c>true</c> when a shortcut existed and was removed; otherwise <c>false</c>.</returns>
        public static bool Remove(string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

            string shortcutPath = ShortcutPath(displayName);
            try
            {
                if (!File.Exists(shortcutPath))
                {
                    return false;
                }

                File.Delete(shortcutPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.Warn($"Failed to remove Start Menu shortcut: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Builds the per-user Start Menu Programs path for the named shortcut.
        /// </summary>
        private static string ShortcutPath(string displayName)
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Path.Combine(programs, displayName + ".lnk");
        }

        /// <summary>
        /// Instantiates a ShellLink, points it at the executable, stamps the AUMID property, and saves
        /// the <c>.lnk</c> to disk.
        /// </summary>
        private static void CreateShortcut(string shortcutPath, string executablePath, string appUserModelId)
        {
            int hr = CoCreateInstance(in CLSID_ShellLink, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IID_IShellLinkW, out IntPtr shellLinkPtr);
            if (hr != 0)
            {
                throw new COMException("CoCreateInstance(ShellLink) failed.", hr);
            }

            object shellLink = ComWrappers.GetOrCreateObjectForComInstance(shellLinkPtr, CreateObjectFlags.UniqueInstance);
            // The wrapper holds its own reference now; release the one CoCreateInstance handed back.
            Marshal.Release(shellLinkPtr);

            var link = (IShellLinkW)shellLink;
            CheckHr(link.SetPath(executablePath), nameof(IShellLinkW.SetPath));

            var store = (IPropertyStore)shellLink;
            IntPtr value = Marshal.StringToCoTaskMemUni(appUserModelId);
            try
            {
                PropVariant variant = new() { Vt = VT_LPWSTR, Pointer = value };
                PropertyKey key = AppUserModelIdKey;
                CheckHr(store.SetValue(in key, in variant), nameof(IPropertyStore.SetValue));
                CheckHr(store.Commit(), nameof(IPropertyStore.Commit));
            }
            finally
            {
                Marshal.FreeCoTaskMem(value);
            }

            var file = (IPersistFile)shellLink;
            CheckHr(file.Save(shortcutPath, fRemember: true), nameof(IPersistFile.Save));
        }

        /// <summary>
        /// Throws a <see cref="COMException"/> for a non-success HRESULT, naming the failed call.
        /// </summary>
        private static void CheckHr(int hr, string operation)
        {
            if (hr != 0)
            {
                throw new COMException($"{operation} failed.", hr);
            }
        }

        /// <summary>
        /// Initializes COM as an STA for this thread when it is not already initialized.
        /// </summary>
        /// <returns><c>true</c> when this call performed the initialization (and must balance it with
        /// <see cref="CoUninitialize"/>); <c>false</c> when COM was already initialized on the thread.</returns>
        private static bool TryInitializeCom()
        {
            const uint COINIT_APARTMENTTHREADED = 0x2;
            int hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            // S_OK (0): we initialized it. S_FALSE (1) / RPC_E_CHANGED_MODE: already up — don't uninit.
            return hr == 0;
        }

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(
            in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [StructLayout(LayoutKind.Sequential)]
        internal struct PropertyKey
        {
            public Guid FmtId;
            public uint Pid;
        }

        // PROPVARIANT, narrowed to the VT_LPWSTR shape we set. On x64 the union sits at offset 8
        // after the 2-byte vt and its 6 bytes of reserved/padding; total size is 16 bytes.
        [StructLayout(LayoutKind.Explicit, Size = 16)]
        internal struct PropVariant
        {
            [FieldOffset(0)] public ushort Vt;
            [FieldOffset(8)] public IntPtr Pointer;
        }

        [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        internal partial interface IShellLinkW
        {
            // Slots 1-17 are unused here; declared (in vtable order) only to place SetPath correctly.
            [PreserveSig] int GetPath();
            [PreserveSig] int GetIDList();
            [PreserveSig] int SetIDList();
            [PreserveSig] int GetDescription();
            [PreserveSig] int SetDescription();
            [PreserveSig] int GetWorkingDirectory();
            [PreserveSig] int SetWorkingDirectory();
            [PreserveSig] int GetArguments();
            [PreserveSig] int SetArguments();
            [PreserveSig] int GetHotkey();
            [PreserveSig] int SetHotkey();
            [PreserveSig] int GetShowCmd();
            [PreserveSig] int SetShowCmd();
            [PreserveSig] int GetIconLocation();
            [PreserveSig] int SetIconLocation();
            [PreserveSig] int SetRelativePath();
            [PreserveSig] int Resolve();
            [PreserveSig] int SetPath(string pszFile);
        }

        [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        internal partial interface IPersistFile
        {
            [PreserveSig] int GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            [PreserveSig] int Load(string pszFileName, int dwMode);
            [PreserveSig] int Save(string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            [PreserveSig] int SaveCompleted(string? pszFileName);
            [PreserveSig] int GetCurFile(out string ppszFileName);
        }

        [GeneratedComInterface]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        internal partial interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint cProps);
            [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
            [PreserveSig] int GetValue(in PropertyKey key, out PropVariant pv);
            [PreserveSig] int SetValue(in PropertyKey key, in PropVariant pv);
            [PreserveSig] int Commit();
        }
    }
}
