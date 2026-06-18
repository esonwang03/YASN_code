using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using YASN.Infrastructure.Sync;
using YASN.Localization;

namespace YASN.Views
{
    /// <summary>
    /// Modal dialog shown before a sync pass applies two or more deletions. Lists each note that would
    /// be removed and which side loses it, returning whether the user approved the deletions.
    /// </summary>
    public sealed partial class BulkChangeDialog : Window
    {
        /// <summary>
        /// Initializes the dialog over a change plan, rendering one row per planned deletion.
        /// </summary>
        /// <param name="plan">The planned deletions to display.</param>
        /// <param name="localization">The localization service for side labels.</param>
        public BulkChangeDialog(SyncChangePlan plan, LocalizationService localization)
        {
            InitializeComponent();

            ItemsControl list = this.FindControl<ItemsControl>("ItemsList")
                ?? throw new InvalidOperationException("ItemsList was not found.");

            list.ItemsSource = plan.Deletions
                .Select(d => new Row(
                    d.Title,
                    localization[d.Side == SyncDeleteSide.Local
                        ? "Sync.Confirm.DeleteLocal"
                        : "Sync.Confirm.DeleteRemote"]))
                .ToList();
        }

        /// <summary>
        /// Initializes an empty dialog for the XAML designer.
        /// </summary>
        public BulkChangeDialog()
            : this(new SyncChangePlan(), LocalizationService.Current)
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Gets whether the user approved the deletions. Meaningful after the dialog closes; used by
        /// the ownerless <see cref="Window.Show()"/> path where <see cref="Window.ShowDialog{TResult}"/>
        /// cannot return a value.
        /// </summary>
        public bool GetResult() => approved;

        private bool approved;

        private void HandleProceedClick(object? sender, RoutedEventArgs e)
        {
            approved = true;
            Close(true);
        }

        private void HandleCancelClick(object? sender, RoutedEventArgs e)
        {
            approved = false;
            Close(false);
        }

        /// <summary>One displayed deletion row.</summary>
        private sealed record Row(string Title, string SideText);
    }
}
