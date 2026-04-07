using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using YASN.Infrastructure.Sync;

namespace YASN.Infrastructure.Logging
{
    /// <summary>
    /// Displays and updates a toast progress bar for a manually triggered sync run.
    /// </summary>
    internal sealed class SyncProgressToast
    {
        private const string ToastTag = "manual-sync";
        private const string ToastGroup = "sync";
        private const string ProgressTitleBinding = "progressTitle";
        private const string ProgressValueBinding = "progressValue";
        private const string ProgressValueStringBinding = "progressValueString";
        private const string ProgressStatusBinding = "progressStatus";
        private const string ProgressTitle = "Sync Progress";

        /// <summary>
        /// Shows the initial progress toast.
        /// </summary>
        internal void Show()
        {
            try
            {
                ToastNotificationManagerCompat.History.Remove(ToastTag, ToastGroup);

                ToastNotification toast = new ToastNotification(CreateToastContent().GetXml())
                {
                    Tag = ToastTag,
                    Group = ToastGroup,
                    ExpirationTime = DateTimeOffset.Now.AddMinutes(10),
                    Data = CreateNotificationData(0d, "0 / 1", "Preparing", sequence: 0)
                };

                ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            }
            catch (COMException ex)
            {
                AppLogger.Debug($"Sync progress toast show failed: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                AppLogger.Debug($"Sync progress toast show failed: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Sync progress toast show failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the progress toast with a new sync snapshot.
        /// </summary>
        /// <param name="progress">The progress snapshot to display.</param>
        internal void Report(SyncProgressInfo progress)
        {
            string valueString = $"{progress.CompletedSteps} / {Math.Max(progress.TotalSteps, 1)}";
            Update(progress.ProgressRatio, valueString, progress.StatusText);
        }

        /// <summary>
        /// Marks the progress toast as completed and displays the final sync summary.
        /// </summary>
        /// <param name="result">The completed sync result.</param>
        internal void Complete(SyncResult result)
        {
            string valueString = $"{result.FilesUploaded} uploaded / {result.FilesDownloaded} downloaded";
            string status = result.Success ? result.Message : $"Failed: {result.Message}";
            Update(1d, valueString, status);
        }

        /// <summary>
        /// Marks the progress toast as failed when an unexpected exception occurs.
        /// </summary>
        /// <param name="message">The failure message to display.</param>
        internal void Fail(string message)
        {
            Update(1d, "failed", message);
        }

        /// <summary>
        /// Updates the existing toast data for the progress bar.
        /// </summary>
        /// <param name="value">The progress ratio to display.</param>
        /// <param name="valueString">The text displayed next to the progress bar.</param>
        /// <param name="status">The progress status line.</param>
        private void Update(double value, string valueString, string status)
        {
            try
            {
                NotificationData data = CreateNotificationData(
                    Math.Clamp(value, 0d, 1d),
                    valueString,
                    status,
                    sequence: 0);

                NotificationUpdateResult updateResult = ToastNotificationManagerCompat
                    .CreateToastNotifier()
                    .Update(data, ToastTag, ToastGroup);

                if (updateResult != NotificationUpdateResult.Succeeded)
                {
                    AppLogger.Debug($"Sync progress toast update result: {updateResult}");
                }
            }
            catch (COMException ex)
            {
                AppLogger.Debug($"Sync progress toast update failed: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                AppLogger.Debug($"Sync progress toast update failed: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Sync progress toast update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates the bindable toast content used for manual sync progress.
        /// </summary>
        /// <returns>A toast content instance with bindable progress fields.</returns>
        private static ToastContent CreateToastContent()
        {
            ToastContentBuilder builder = new ToastContentBuilder()
                .AddText("YASN Manual Sync")
                .AddText("Syncing local data with WebDAV")
                .AddVisualChild(new AdaptiveProgressBar
                {
                    Title = new BindableString(ProgressTitleBinding),
                    Value = new BindableProgressBarValue(ProgressValueBinding),
                    ValueStringOverride = new BindableString(ProgressValueStringBinding),
                    Status = new BindableString(ProgressStatusBinding)
                });

            return builder.Content;
        }

        /// <summary>
        /// Creates notification data values for a progress update.
        /// </summary>
        /// <param name="value">The progress ratio in the inclusive range [0, 1].</param>
        /// <param name="valueString">The value string shown next to the progress bar.</param>
        /// <param name="status">The status text shown under the progress bar.</param>
        /// <param name="sequence">The toast update sequence number.</param>
        /// <returns>The notification data used to show or update the toast.</returns>
        private static NotificationData CreateNotificationData(double value, string valueString, string status, uint sequence)
        {
            NotificationData data = new NotificationData
            {
                SequenceNumber = sequence
            };

            data.Values[ProgressTitleBinding] = ProgressTitle;
            data.Values[ProgressValueBinding] = value.ToString("0.####", CultureInfo.InvariantCulture);
            data.Values[ProgressValueStringBinding] = valueString;
            data.Values[ProgressStatusBinding] = status;

            return data;
        }
    }
}
