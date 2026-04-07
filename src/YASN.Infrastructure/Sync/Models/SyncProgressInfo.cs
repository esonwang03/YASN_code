namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Represents one progress snapshot for a running sync operation.
    /// </summary>
    public sealed class SyncProgressInfo
    {
        /// <summary>
        /// Gets the number of completed sync steps.
        /// </summary>
        public int CompletedSteps { get; init; }

        /// <summary>
        /// Gets the total number of sync steps.
        /// </summary>
        public int TotalSteps { get; init; }

        /// <summary>
        /// Gets the current status text for the running sync.
        /// </summary>
        public string StatusText { get; init; } = string.Empty;

        /// <summary>
        /// Gets the normalized progress ratio in the inclusive range [0, 1].
        /// </summary>
        public double ProgressRatio
        {
            get
            {
                if (TotalSteps <= 0)
                {
                    return 0d;
                }

                return Math.Clamp((double)CompletedSteps / TotalSteps, 0d, 1d);
            }
        }
    }
}
