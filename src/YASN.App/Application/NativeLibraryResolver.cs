using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace YASN.Application
{
    /// <summary>
    /// Resolves the app's bundled native libraries from Contents/Frameworks on macOS.
    /// </summary>
    /// <remarks>
    /// The macOS .app keeps native dylibs in Contents/Frameworks to satisfy Apple's
    /// code-signing layout (codesign --deep --strict rejects dotted-name dylibs loose in
    /// Contents/MacOS). A single-file self-contained publish has no deps.json and probes only
    /// the app base directory (Contents/MacOS) for native P/Invoke libraries, so this hook
    /// redirects failed unmanaged-load attempts to the sibling Frameworks directory.
    /// </remarks>
    internal static class NativeLibraryResolver
    {
        private static int _registered;

        /// <summary>
        /// Subscribes the resolver to the default load context. No-op off macOS; idempotent.
        /// </summary>
        internal static void Register()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            if (Interlocked.Exchange(ref _registered, 1) == 1)
            {
                return;
            }

            AssemblyLoadContext.Default.ResolvingUnmanagedDll += Resolve;
        }

        /// <summary>
        /// Attempts to load <paramref name="libraryName"/> from the bundle's Frameworks directory.
        /// </summary>
        /// <param name="requestingAssembly">The assembly requesting the native library (unused).</param>
        /// <param name="libraryName">The native library name passed to the failed P/Invoke.</param>
        /// <returns>The loaded module handle, or <see cref="IntPtr.Zero"/> if not resolved here.</returns>
        private static IntPtr Resolve(Assembly requestingAssembly, string libraryName)
        {
            try
            {
                string frameworks = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, "..", "Frameworks"));
                if (!Directory.Exists(frameworks))
                {
                    return IntPtr.Zero;
                }

                foreach (string candidate in CandidateFileNames(libraryName))
                {
                    string full = Path.Combine(frameworks, candidate);
                    if (!File.Exists(full))
                    {
                        continue;
                    }

                    try
                    {
                        return NativeLibrary.Load(full);
                    }
                    catch (DllNotFoundException ex)
                    {
                        // The file is present but unloadable: a real problem worth surfacing,
                        // unlike a plain miss (which is expected for libraries that aren't ours).
                        AppLogger.Warn($"NativeLibraryResolver: failed to load '{full}': {ex.Message}");
                        return IntPtr.Zero;
                    }
                    catch (BadImageFormatException ex)
                    {
                        AppLogger.Warn($"NativeLibraryResolver: bad image '{full}': {ex.Message}");
                        return IntPtr.Zero;
                    }
                }

                return IntPtr.Zero;
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"NativeLibraryResolver: I/O error resolving '{libraryName}': {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Yields the macOS filename spellings to probe for a requested native library name.
        /// </summary>
        /// <param name="libraryName">The name passed to the failed P/Invoke.</param>
        /// <returns>Candidate filenames, most specific first.</returns>
        private static IEnumerable<string> CandidateFileNames(string libraryName)
        {
            yield return libraryName;                // exact, e.g. "libSkiaSharp.dylib"
            yield return libraryName + ".dylib";     // "{name}.dylib", e.g. "libAvaloniaNative" -> ".dylib"
            if (!libraryName.StartsWith("lib", StringComparison.Ordinal))
            {
                yield return "lib" + libraryName + ".dylib";  // "e_sqlite3" -> "libe_sqlite3.dylib"
            }
        }
    }
}
