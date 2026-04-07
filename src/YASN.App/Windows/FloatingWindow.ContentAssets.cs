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
    /// Contains editor content updates, clipboard handling, drag and drop, and note asset insertion logic.
    /// </summary>
    public partial class FloatingWindow
    {
        private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveContent();
            SchedulePreviewRender();
        }

        private void ContentTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool isPasteShortcut = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V;
            if (!isPasteShortcut)
            {
                return;
            }

            if (!TryPasteClipboardAssets())
            {
                return;
            }

            e.Handled = true;
        }

        private bool TryPasteClipboardAssets()
        {
            try
            {
                if (WinFormsClipboard.ContainsImage())
                {
                    using Image? clipboardImage = WinFormsClipboard.GetImage();
                    if (clipboardImage != null)
                    {
                        InsertClipboardImage(clipboardImage);
                        return true;
                    }
                }

                if (System.Windows.Clipboard.ContainsImage())
                {
                    var clipboardImage = System.Windows.Clipboard.GetImage();
                    if (clipboardImage != null)
                    {
                        InsertClipboardImage(clipboardImage);
                        return true;
                    }
                }

                if (!System.Windows.Clipboard.ContainsFileDropList())
                {
                    return false;
                }

                StringCollection files = System.Windows.Clipboard.GetFileDropList();
                if (files.Count == 0)
                {
                    return false;
                }

                bool insertedAny = false;
                foreach (string path in files.Cast<string>())
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    if (IsImageFile(path))
                    {
                        InsertImage(path);
                    }
                    else
                    {
                        InsertAttachment(path);
                    }

                    insertedAny = true;
                }

                return insertedAny;
            }
            catch (ExternalException ex)
            {
                AppLogger.Warn($"Failed to paste clipboard content: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"Failed to paste clipboard content: {ex.Message}");
                return false;
            }
        }

        private void InsertClipboardImage(System.Drawing.Image image)
        {
            string? destPath = null;
            try
            {
                string fileName = $"{Guid.NewGuid()}.png";
                destPath = Path.GetFullPath(Path.Combine(_imageDirectory, fileName));

                string? targetDirectory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                using Bitmap bitmap = new System.Drawing.Bitmap(image);
                using (var stream = File.Create(destPath))
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                }

                string markdown = $"![clipboard-image](note-assets/{NoteData.Id}/{fileName}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (ExternalException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (NotSupportedException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertClipboardImage(BitmapSource imageSource)
        {
            string? destPath = null;
            try
            {
                string fileName = $"{Guid.NewGuid()}.png";
                destPath = Path.GetFullPath(Path.Combine(_imageDirectory, fileName));

                string? targetDirectory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(imageSource));
                using (var stream = File.Create(destPath))
                {
                    encoder.Save(stream);
                }

                string markdown = $"![clipboard-image](note-assets/{NoteData.Id}/{fileName}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (NotSupportedException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (InvalidOperationException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image File|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                Title = "Select Image to Insert"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InsertImage(openFileDialog.FileName);
            }
        }

        private void InsertAttachment_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "All Files|*.*",
                Title = "Select Attachment to Insert"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InsertAttachment(openFileDialog.FileName);
            }
        }

        private void ContentTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void ContentTextBox_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && File.Exists(files[0]))
            {
                if (IsImageFile(files[0]))
                {
                    InsertImage(files[0]);
                }
                else
                {
                    InsertAttachment(files[0]);
                }

                e.Handled = true;
            }
        }

        private static bool IsImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteGeneratedFile(string? path, string reason)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to clean up generated file '{path}' after {reason}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to clean up generated file '{path}' after {reason}: {ex.Message}");
            }
        }

        private void InsertImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                string fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                destPath = Path.Combine(_imageDirectory, fileName);
                File.Copy(sourceFilePath, destPath, true);

                string relativePath = $"note-assets/{NoteData.Id}/{fileName}";
                string altText = Path.GetFileNameWithoutExtension(sourceFilePath);
                string markdown = $"![{altText}]({relativePath}){Environment.NewLine}";

                InsertTextAtCaret(markdown);
            }
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (NotSupportedException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertAttachment(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                MessageBox.Show("Attachment file does not exist.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(sourceFilePath);
                string displayName = Path.GetFileName(sourceFilePath);
                string linkTarget;
                SettingsStore settingsStore = new SettingsStore();
                bool autoSyncEnabled = AttachmentSyncSettings.GetAutoSyncEnabled(settingsStore);
                long autoSyncMaxBytes = AttachmentSyncSettings.GetAutoSyncThresholdBytes(settingsStore);

                if (autoSyncEnabled && fileInfo.Length <= autoSyncMaxBytes)
                {
                    string fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                    string destPath = Path.Combine(_attachmentDirectory, fileName);
                    File.Copy(sourceFilePath, destPath, true);
                    linkTarget = $"note-assets/attachments/{NoteData.Id}/{fileName}";
                }
                else
                {
                    linkTarget = new Uri(sourceFilePath, UriKind.Absolute).AbsoluteUri;
                }

                string markdown = $"[{displayName}]({linkTarget}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UriFormatException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertTextAtCaret(string text)
        {
            int index = ContentTextBox.CaretIndex;
            string current = ContentTextBox.Text ?? string.Empty;
            ContentTextBox.Text = current.Insert(index, text);
            ContentTextBox.CaretIndex = index + text.Length;
            ContentTextBox.Focus();
        }
    }
}