using System.Runtime.CompilerServices;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Redirects the app's entire storage root to a throwaway per-run temp directory before any test
    /// code runs. <see cref="YASN.Infrastructure.AppPaths"/> resolves its persistent, cache, and data
    /// directories once at static initialization; without this redirect every test that constructs a
    /// <c>SettingsStore</c> (or otherwise touches <c>AppPaths</c>) would read and write the real
    /// per-user configuration under <c>%AppData%/yasn</c>, leaving orphan keys behind. The
    /// <c>[ModuleInitializer]</c> runs when the test assembly loads — guaranteed before the first test
    /// touches <c>AppPaths</c>, so the static initializer observes the override and never sees the live
    /// user paths.
    /// </summary>
    internal static class TestDataRootInitializer
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            // A unique directory per run keeps concurrent or repeated test runs from sharing state and
            // avoids ever clobbering the developer's real configuration.
            string root = Path.Combine(Path.GetTempPath(), "yasn-tests", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("YASN_DATA_ROOT", root);
        }
    }
}
