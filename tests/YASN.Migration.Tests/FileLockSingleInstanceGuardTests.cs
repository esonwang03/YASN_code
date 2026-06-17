using YASN.PlatformServices;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the file-lock single-instance guard enforces exclusivity.
    /// </summary>
    public sealed class FileLockSingleInstanceGuardTests
    {
        /// <summary>
        /// Grants the primary slot to the first holder and denies a contender until release.
        /// </summary>
        [Fact]
        public void SecondGuardIsDeniedUntilFirstReleases()
        {
            string lockPath = Path.Combine(Path.GetTempPath(), $"yasn-lock-{Guid.NewGuid():N}", "yasn.lock");

            try
            {
                FileLockSingleInstanceGuard first = new FileLockSingleInstanceGuard(lockPath);
                Assert.True(first.HasPrimaryInstance);

                using (FileLockSingleInstanceGuard second = new FileLockSingleInstanceGuard(lockPath))
                {
                    Assert.False(second.HasPrimaryInstance);
                }

                first.Dispose();

                using FileLockSingleInstanceGuard third = new FileLockSingleInstanceGuard(lockPath);
                Assert.True(third.HasPrimaryInstance);
            }
            finally
            {
                string? directory = Path.GetDirectoryName(lockPath);
                if (directory is not null && Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }
}
