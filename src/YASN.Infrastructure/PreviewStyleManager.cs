using System.IO;
using YASN.Infrastructure.Logging;

namespace YASN.Infrastructure
{
    internal static class PreviewStyleManager
    {
        internal const string SettingKey = "note.previewStyle";
        internal const string DefaultStyleRelativePath = "default.css";
        private static readonly string BundledStyleRoot = Path.Combine(AppPaths.BaseDirectory, "style");
        private static readonly object InitLock = new();
        private static bool _initialized;
        private static bool _missingBundleDirLogged;

        internal static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (InitLock)
            {
                if (_initialized)
                {
                    return;
                }

                try
                {
                    Directory.CreateDirectory(AppPaths.StyleRoot);
                    CopyBundledStylesIfMissing();
                    EnsureDefaultStyleExists();
                    _initialized = true;
                }
                catch (IOException ex)
                {
                    AppLogger.Warn($"Failed to initialize style directory: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    AppLogger.Warn($"Failed to initialize style directory: {ex.Message}");
                }
                catch (NotSupportedException ex)
                {
                    AppLogger.Warn($"Failed to initialize style directory: {ex.Message}");
                }
            }
        }

        internal static IReadOnlyList<string> ListStyles()
        {
            EnsureInitialized();
            try
            {
                List<string> files = Directory.GetFiles(AppPaths.StyleRoot, "*.css", SearchOption.AllDirectories)
                    .Select(ToStyleRelativePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (files.Count == 0)
                {
                    files.Add(DefaultStyleRelativePath);
                }

                AppLogger.Debug($"Discovered {files.Count} preview style file(s).");
                return files;
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to enumerate preview styles: {ex.Message}");
                return [DefaultStyleRelativePath];
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to enumerate preview styles: {ex.Message}");
                return [DefaultStyleRelativePath];
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Warn($"Failed to enumerate preview styles: {ex.Message}");
                return [DefaultStyleRelativePath];
            }
        }

        internal static string ResolveStyle(string? configuredStyle)
        {
            EnsureInitialized();
            if (!TryNormalizeStylePath(configuredStyle, out string? normalized))
            {
                if (!string.IsNullOrWhiteSpace(configuredStyle))
                {
                    AppLogger.Warn($"Invalid preview style setting '{configuredStyle}', fallback to default.");
                }

                return DefaultStyleRelativePath;
            }

            string absolutePath = ToStyleAbsolutePath(normalized);
            if (File.Exists(absolutePath))
            {
                return normalized;
            }

            AppLogger.Warn($"Preview style file not found: {absolutePath}. Fallback to default style.");
            return DefaultStyleRelativePath;
        }

        internal static string BuildStyleHref(string styleRelativePath, long cacheToken)
        {
            string resolved = ResolveStyle(styleRelativePath);
            string encodedPath = string.Join("/",
                resolved.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            return $"style/{encodedPath}?v={cacheToken}";
        }

        internal static string ToStyleAbsolutePath(string styleRelativePath)
        {
            EnsureInitialized();
            string normalized = TryNormalizePathWithoutFallback(styleRelativePath, out string? parsed)
                ? parsed
                : DefaultStyleRelativePath;
            string localRelative = normalized.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(AppPaths.StyleRoot, localRelative);
        }

        private static void EnsureDefaultStyleExists()
        {
            string path = Path.Combine(AppPaths.StyleRoot, DefaultStyleRelativePath);
            if (File.Exists(path))
            {
                return;
            }

            string bundledPath = Path.Combine(BundledStyleRoot, DefaultStyleRelativePath);
            if (File.Exists(bundledPath))
            {
                File.Copy(bundledPath, path, overwrite: false);
                AppLogger.Info($"Installed default preview style from bundle: {path}");
                return;
            }

            // Last-resort fallback to keep preview readable if style bundle is missing.
            File.WriteAllText(path, "html,body{margin:0;padding:0;height:100%;}#page{padding:16px 20px;}");
            AppLogger.Warn($"Bundled default style missing. Created fallback style at {path}");
        }

        private static void CopyBundledStylesIfMissing()
        {
            if (!Directory.Exists(BundledStyleRoot))
            {
                if (_missingBundleDirLogged) return;
                AppLogger.Warn($"Bundled style directory not found: {BundledStyleRoot}");
                _missingBundleDirLogged = true;

                return;
            }

            foreach (string sourcePath in Directory.GetFiles(BundledStyleRoot, "*.css", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(BundledStyleRoot, sourcePath);
                string destination = Path.Combine(AppPaths.StyleRoot, relative);
                string? destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                bool shouldCopy = !File.Exists(destination);
                if (!shouldCopy)
                {
                    DateTime sourceWriteTime = File.GetLastWriteTimeUtc(sourcePath);
                    DateTime destinationWriteTime = File.GetLastWriteTimeUtc(destination);
                    shouldCopy = sourceWriteTime > destinationWriteTime;
                }

                if (!shouldCopy)
                {
                    continue;
                }

                File.Copy(sourcePath, destination, overwrite: true);
                AppLogger.Debug($"Synced bundled preview style: {relative}");
            }
        }

        private static bool TryNormalizePathWithoutFallback(string? raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            try
            {
                string candidate = raw.Trim();
                if (Path.IsPathRooted(candidate))
                {
                    return false;
                }

                string fullStyleRoot = Path.GetFullPath(AppPaths.StyleRoot);
                string fullCandidate = Path.GetFullPath(Path.Combine(AppPaths.StyleRoot, candidate));
                if (!fullCandidate.StartsWith(fullStyleRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!fullCandidate.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string relative = Path.GetRelativePath(fullStyleRoot, fullCandidate)
                    .Replace('\\', '/')
                    .Trim();
                if (string.IsNullOrWhiteSpace(relative) ||
                    relative.StartsWith("../", StringComparison.Ordinal) ||
                    string.Equals(relative, "..", StringComparison.Ordinal))
                {
                    return false;
                }

                normalized = relative;
                return true;
            }
            catch (ArgumentException ex)
            {
                AppLogger.Debug($"Failed to normalize preview style path '{raw}': {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to normalize preview style path '{raw}': {ex.Message}");
                return false;
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to normalize preview style path '{raw}': {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to normalize preview style path '{raw}': {ex.Message}");
                return false;
            }
        }

        private static string ToStyleRelativePath(string fullPath)
        {
            try
            {
                string relative = Path.GetRelativePath(AppPaths.StyleRoot, fullPath)
                    .Replace('\\', '/')
                    .Trim();
                return TryNormalizePathWithoutFallback(relative, out string? normalized)
                    ? normalized
                    : string.Empty;
            }
            catch (ArgumentException ex)
            {
                AppLogger.Debug($"Failed to convert style path '{fullPath}' to relative path: {ex.Message}");
                return string.Empty;
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to convert style path '{fullPath}' to relative path: {ex.Message}");
                return string.Empty;
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to convert style path '{fullPath}' to relative path: {ex.Message}");
                return string.Empty;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to convert style path '{fullPath}' to relative path: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool TryNormalizeStylePath(string? raw, out string normalized)
        {
            if (TryNormalizePathWithoutFallback(raw, out normalized))
            {
                return true;
            }

            normalized = string.Empty;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                AppLogger.Warn($"Failed to normalize preview style path '{raw}'.");
            }

            return false;
        }
    }
}
