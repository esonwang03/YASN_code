using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Markdig;
using Microsoft.Web.WebView2.Core;
using YASN.Infrastructure.Logging;
using YASN.Infrastructure.Markdown;
using YASN.App.Settings;
using YASN.App.WindowLayout;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataFormats = System.Windows.DataFormats;
using DragDeltaEventArgs = System.Windows.Controls.Primitives.DragDeltaEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using DrawingColor = System.Drawing.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Drawing.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = ModernWpf.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using WinFormsClipboard = System.Windows.Forms.Clipboard;


namespace YASN
{
    /// <summary>
    /// Contains theme, title bar, and background image appearance behavior for the floating window.
    /// </summary>
    public partial class FloatingWindow
    {
        private void ToggleTheme()
        {
            NoteData.IsDarkMode = !NoteData.IsDarkMode;
            ApplyTheme(NoteData.IsDarkMode);
            UpdateThemeToggleButton();
            NoteManager.Instance.UpdateNote(NoteData);
            _ = ApplyPreviewThemeClassAsync(NoteData.IsDarkMode);
            _previewDebounceTimer.Stop();
            _ = RenderPreviewAsync();
        }

        private async Task ApplyPreviewThemeClassAsync(bool isDarkMode)
        {
            if (!_previewReady || PreviewWebView.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                string themeClass = isDarkMode ? "theme-dark" : "theme-light";
                string script = $"(() => {{ if (document.body) document.body.className = '{themeClass}'; }})();";
                await PreviewWebView.ExecuteScriptAsync(script);
            }
            catch (COMException ex)
            {
                AppLogger.Debug($"Failed to apply preview theme class: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to apply preview theme class: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"Failed to apply preview theme class: {ex.Message}");
            }
        }

        private void UpdateThemeToggleButton()
        {
            if (ThemeToggleButton == null)
            {
                return;
            }

            ThemeToggleButton.Content = NoteData.IsDarkMode ? IconSun : IconMoon;
            ThemeToggleButton.ToolTip = NoteData.IsDarkMode
                ? "Switch to Day mode"
                : "Switch to Night mode";
        }

        private void ApplyTheme(bool isDarkMode)
        {
            if (isDarkMode)
            {
                MainBorder.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1E, 0x1E, 0x1E));
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80));
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xF0, 0xF1));
                MarkdownToolbar.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
                ContentTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xF0, 0xF1));
                ContentTextBox.Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
            }
            else
            {
                MainBorder.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xF0));
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xC0, 0xC0, 0xC0));
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
                MarkdownToolbar.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0xE0, 0xE0));
                ContentTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
                ContentTextBox.Background = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            }
        }

        internal void ReapplyWindowLevelAfterQuickLayout()
        {
            switch (NoteData.Level)
            {
                case WindowLevel.BottomMost:
                    ApplyWindowLevel();
                    break;
                case WindowLevel.TopMost:
                    Topmost = true;
                    break;
            }
        }

        private void ApplyTitleBarColor(string colorHex)
        {
            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(colorHex);
                TitleBar.Background = new SolidColorBrush(color);
            }
            catch (FormatException ex)
            {
                AppLogger.Warn($"Failed to apply title bar color '{colorHex}': {ex.Message}");
                TitleBar.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xD4, 0xC5, 0xE0));
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Warn($"Failed to apply title bar color '{colorHex}': {ex.Message}");
                TitleBar.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xD4, 0xC5, 0xE0));
            }
        }
        private void ShowColorPicker()
        {
            Window colorPickerWindow = new Window
            {
                Title = "Title Bar Color",
                Width = 390,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            StackPanel stackPanel = new StackPanel { Margin = new Thickness(18) };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Choose a preset color",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            Grid colorsGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());

            (string, string)[] presetColors =
            [
                ("#E6D4C5E0", "Lavender"),
                ("#E6FFB6C1", "Rose"),
                ("#E6B0E0E6", "Sky"),
                ("#E6C8E6C9", "Mint"),
                ("#E6FFE4B5", "Peach"),
                ("#E6F5DEB3", "Sand"),
                ("#E6E6E6FA", "Soft Indigo"),
                ("#E6FFE4E1", "Mist")
            ];

            int row = 0;
            int col = 0;
            foreach (var (colorHex, colorName) in presetColors)
            {
                if (col == 0)
                {
                    colorsGrid.RowDefinitions.Add(new RowDefinition());
                }

                Button button = new Button
                {
                    Width = 108,
                    Height = 56,
                    Margin = new Thickness(4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                    Content = colorName,
                    Tag = colorHex
                };

                button.Click += (s, _) =>
                {
                    string? selectedColor = (s as Button)?.Tag as string;
                    if (!string.IsNullOrEmpty(selectedColor))
                    {
                        NoteData.TitleBarColor = selectedColor;
                        ApplyTitleBarColor(selectedColor);
                        NoteManager.Instance.UpdateNote(NoteData);
                        colorPickerWindow.Close();
                    }
                };

                Grid.SetRow(button, row);
                Grid.SetColumn(button, col);
                colorsGrid.Children.Add(button);

                col++;
                if (col >= 3)
                {
                    col = 0;
                    row++;
                }
            }

            stackPanel.Children.Add(colorsGrid);
            colorPickerWindow.Content = stackPanel;
            colorPickerWindow.ShowDialog();
        }

        private void ShowBackgroundImageMenu(FrameworkElement anchorElement)
        {
            ContextMenu contextMenu = new ContextMenu();

            MenuItem selectImageItem = new MenuItem { Header = "Select Background Image" };
            selectImageItem.Click += (_, _) =>
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Image File|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                    Title = "Select Background Image"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    SetBackgroundImage(openFileDialog.FileName);
                }
            };

            MenuItem clearBackgroundItem = new MenuItem { Header = "Clear Background Image" };
            clearBackgroundItem.Click += (_, _) => ClearBackgroundImage();

            MenuItem adjustOpacityItem = new MenuItem { Header = "Adjust Background Opacity" };
            adjustOpacityItem.Click += (_, _) => ShowOpacityAdjuster();

            contextMenu.Items.Add(selectImageItem);
            contextMenu.Items.Add(clearBackgroundItem);
            contextMenu.Items.Add(adjustOpacityItem);

            contextMenu.PlacementTarget = anchorElement;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            contextMenu.IsOpen = true;
        }

        private void SetBackgroundImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                string fileName = $"background{Path.GetExtension(sourceFilePath)}";
                destPath = Path.Combine(_backgroundImageDirectory, fileName);

                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                File.Copy(sourceFilePath, destPath, true);

                NoteData.BackgroundImagePath = destPath;
                ApplyBackgroundImage(destPath);
                NoteManager.Instance.UpdateNote(NoteData);
            }
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "setting background image");
                AppLogger.Warn($"Failed to set background image from '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to set background image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "setting background image");
                AppLogger.Warn($"Failed to set background image from '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to set background image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "setting background image");
                AppLogger.Warn($"Failed to set background image from '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to set background image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyBackgroundImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                BackgroundImageBrush.ImageSource = null;
                return;
            }

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                BackgroundImageBrush.ImageSource = bitmap;
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to load background image '{imagePath}': {ex.Message}");
                BackgroundImageBrush.ImageSource = null;
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to load background image '{imagePath}': {ex.Message}");
                BackgroundImageBrush.ImageSource = null;
            }
            catch (UriFormatException ex)
            {
                AppLogger.Debug($"Failed to load background image '{imagePath}': {ex.Message}");
                BackgroundImageBrush.ImageSource = null;
            }
        }

        private void ClearBackgroundImage()
        {
            NoteData.BackgroundImagePath = null;
            BackgroundImageBrush.ImageSource = null;
            NoteManager.Instance.UpdateNote(NoteData);

            if (!Directory.Exists(_backgroundImageDirectory))
            {
                return;
            }

            try
            {
                foreach (string file in Directory.GetFiles(_backgroundImageDirectory))
                {
                    File.Delete(file);
                }
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to clear background image files in '{_backgroundImageDirectory}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to clear background image files in '{_backgroundImageDirectory}': {ex.Message}");
            }
        }

        private void ShowOpacityAdjuster()
        {
            Window opacityWindow = new Window
            {
                Title = "Background Opacity",
                Width = 300,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            StackPanel stackPanel = new StackPanel { Margin = new Thickness(20) };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Background opacity",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            Slider slider = new Slider
            {
                Minimum = 0.05,
                Maximum = 1.0,
                Value = BackgroundImageBorder.Opacity,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 10)
            };

            TextBlock valueText = new TextBlock
            {
                Text = $"Current: {BackgroundImageBorder.Opacity:F2}",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            slider.ValueChanged += (_, _) =>
            {
                BackgroundImageBorder.Opacity = slider.Value;
                valueText.Text = $"Current: {slider.Value:F2}";

                NoteData.BackgroundImageOpacity = slider.Value;
                NoteManager.Instance.UpdateNote(NoteData);
            };

            stackPanel.Children.Add(slider);
            stackPanel.Children.Add(valueText);

            opacityWindow.Content = stackPanel;
            opacityWindow.ShowDialog();
        }

        private async void RefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            await RenderPreviewAsync();
        }

        // For Editor mode switching in title bar
        EditorDisplayMode GetNextEditorDisplayMode(EditorDisplayMode previousMode)
        {
            return previousMode switch
            {
                EditorDisplayMode.TextOnly => EditorDisplayMode.TextAndPreview,
                EditorDisplayMode.TextAndPreview => EditorDisplayMode.PreviewOnly,
                _ => EditorDisplayMode.TextOnly
            };
        }
        private void EditorModeButton_Click(object sender, RoutedEventArgs e)
        {
            EditorDisplayMode nextMode = GetNextEditorDisplayMode(_editorDisplayMode);
            SetDisplayMode(nextMode, focusEditor: nextMode != EditorDisplayMode.PreviewOnly);
        }

        private static string GetEditorModeLabel(EditorDisplayMode mode)
        {
            return mode switch
            {
                EditorDisplayMode.TextOnly => "Text only",
                EditorDisplayMode.TextAndPreview => "Text + Preview",
                _ => "Preview only"
            };
        }

        private void UpdateEditorModeButton()
        {
            // for collapse
            if (EditorModeButton == null)
            {
                return;
            }

            EditorDisplayMode nextMode = GetNextEditorDisplayMode(_editorDisplayMode);
            switch (_editorDisplayMode)
            {
                case EditorDisplayMode.TextOnly:
                    EditorModeButton.Content = IconModeTextOnly;
                    EditorModeButton.ToolTip = $"Mode: Text only (Next: {GetEditorModeLabel(nextMode)})";
                    break;
                case EditorDisplayMode.TextAndPreview:
                    EditorModeButton.Content = IconModeTextAndPreview;
                    EditorModeButton.ToolTip = $"Mode: Text + Preview (Next: {GetEditorModeLabel(nextMode)})";
                    break;
                case EditorDisplayMode.PreviewOnly:
                default:
                    EditorModeButton.Content = IconModePreviewOnly;
                    EditorModeButton.ToolTip = $"Mode: Preview only (Next: {GetEditorModeLabel(nextMode)})";
                    break;
            }
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            SetDisplayMode(EditorDisplayMode.PreviewOnly);
        }
    }
}