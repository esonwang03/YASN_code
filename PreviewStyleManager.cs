using System.IO;
using YASN.Logging;

namespace YASN
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
                catch (Exception ex)
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
                var files = Directory.GetFiles(AppPaths.StyleRoot, "*.css", SearchOption.AllDirectories)
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to enumerate preview styles: {ex.Message}");
                return new[] { DefaultStyleRelativePath };
            }
        }

        internal static string ResolveStyle(string? configuredStyle)
        {
            EnsureInitialized();
            if (!TryNormalizeStylePath(configuredStyle, out var normalized))
            {
                if (!string.IsNullOrWhiteSpace(configuredStyle))
                {
                    AppLogger.Warn($"Invalid preview style setting '{configuredStyle}', fallback to default.");
                }

                return DefaultStyleRelativePath;
            }

            var absolutePath = ToStyleAbsolutePath(normalized);
            if (File.Exists(absolutePath))
            {
                return normalized;
            }

            AppLogger.Warn($"Preview style file not found: {absolutePath}. Fallback to default style.");
            return DefaultStyleRelativePath;
        }

        internal static string BuildStyleHref(string styleRelativePath, long cacheToken)
        {
            var resolved = ResolveStyle(styleRelativePath);
            var encodedPath = string.Join("/",
                resolved.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            return $"style/{encodedPath}?v={cacheToken}";
        }

        internal static string ToStyleAbsolutePath(string styleRelativePath)
        {
            EnsureInitialized();
            var normalized = TryNormalizePathWithoutFallback(styleRelativePath, out var parsed)
                ? parsed
                : DefaultStyleRelativePath;
            var localRelative = normalized.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(AppPaths.StyleRoot, localRelative);
        }

        private static void EnsureDefaultStyleExists()
        {
            var path = Path.Combine(AppPaths.StyleRoot, DefaultStyleRelativePath);
            if (File.Exists(path))
            {
                return;
            }

            var bundledPath = Path.Combine(BundledStyleRoot, DefaultStyleRelativePath);
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
                if (!_missingBundleDirLogged)
                {
                    AppLogger.Warn($"Bundled style directory not found: {BundledStyleRoot}");
                    _missingBundleDirLogged = true;
                }

                return;
            }

            foreach (var sourcePath in Directory.GetFiles(BundledStyleRoot, "*.css", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(BundledStyleRoot, sourcePath);
                var destination = Path.Combine(AppPaths.StyleRoot, relative);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                var shouldCopy = !File.Exists(destination);
                if (!shouldCopy)
                {
                    var sourceWriteTime = File.GetLastWriteTimeUtc(sourcePath);
                    var destinationWriteTime = File.GetLastWriteTimeUtc(destination);
                    shouldCopy = sourceWriteTime > destinationWriteTime;
                }

                if (!shouldCopy)
                {
                    continue;
                }

                File.Copy(sourcePath, destination, overwrite: true);
                AppLogger.Info($"Synced bundled preview style: {relative}");
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
                var candidate = raw.Trim();
                if (Path.IsPathRooted(candidate))
                {
                    return false;
                }

                var fullStyleRoot = Path.GetFullPath(AppPaths.StyleRoot);
                var fullCandidate = Path.GetFullPath(Path.Combine(AppPaths.StyleRoot, candidate));
                if (!fullCandidate.StartsWith(fullStyleRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!fullCandidate.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var relative = Path.GetRelativePath(fullStyleRoot, fullCandidate)
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
            catch
            {
                return false;
            }
        }

        private static string ToStyleRelativePath(string fullPath)
        {
            try
            {
                var relative = Path.GetRelativePath(AppPaths.StyleRoot, fullPath)
                    .Replace('\\', '/')
                    .Trim();
                return TryNormalizePathWithoutFallback(relative, out var normalized)
                    ? normalized
                    : string.Empty;
            }
            catch
            {
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
