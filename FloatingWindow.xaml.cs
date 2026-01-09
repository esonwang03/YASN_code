using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.IO;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfImage = System.Windows.Controls.Image;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfMessageBox = System.Windows.MessageBox;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using System.Linq;

namespace YASN
{
    public enum WindowLevel
    {
        Normal,
        TopMost,
        BottomMost
    }

    public partial class FloatingWindow : Window
    {
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private IntPtr _hwnd;
        private System.Windows.Threading.DispatcherTimer _timer;
        private Storyboard _collapseEditBar;
        private Storyboard _expandEditBar;

        public NoteData NoteData { get; private set; }

        private string _imageDirectory;
        
        // 静态变量跟踪当前的 BottomMost 窗口
        private static FloatingWindow _currentBottomMostWindow = null;
        private static readonly object _bottomMostLock = new object();
        private bool _isFirstBottomMostWindow = false;
        
        private string _backgroundImageDirectory;
        
        // Track current text color
        private WpfColor _currentTextColor = WpfColor.FromRgb(0x2C, 0x3E, 0x50);
        
        // Flag to indicate if user has explicitly set a color
        private bool _hasExplicitColorSet = false;
        
        // Flag to indicate if application is shutting down
        private static bool _isApplicationShuttingDown = false;
        
        public static void SetApplicationShuttingDown()
        {
            _isApplicationShuttingDown = true;
        }
        
        public FloatingWindow(NoteData noteData)
        {
            InitializeComponent();
            
            NoteData = noteData;
            NoteData.Window = this;
            NoteData.IsOpen = true;

            // Create images directory for this note
            _imageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NoteImages", noteData.Id.ToString());
            if (!Directory.Exists(_imageDirectory))
            {
                Directory.CreateDirectory(_imageDirectory);
            }

            // Create background images directory for this note
            _backgroundImageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NoteBackgrounds", noteData.Id.ToString());
            if (!Directory.Exists(_backgroundImageDirectory))
            {
                Directory.CreateDirectory(_backgroundImageDirectory);
            }

            // Apply saved position and size
            if (noteData.Left > 0 && noteData.Top > 0)
            {
                Left = noteData.Left;
                Top = noteData.Top;
            }
            if (noteData.Width > 0 && noteData.Height > 0)
            {
                Width = noteData.Width;
                Height = noteData.Height;
            }

            UpdateStatusText();
            UpdatePinButton();
            
            // Apply theme BEFORE loading content to ensure correct foreground color
            ApplyTheme(noteData.IsDarkMode);
            
            // Load content from RTF or plain text
            LoadContent(noteData.Content);
            
            // Apply title bar color
            ApplyTitleBarColor(noteData.TitleBarColor);
            
            // Apply background image if exists
            ApplyBackgroundImage(noteData.BackgroundImagePath);
            
            // Apply background image opacity
            BackgroundImageBorder.Opacity = noteData.BackgroundImageOpacity;

            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += Timer_Tick;
            
            _collapseEditBar = (Storyboard)FindResource("CollapseEditBar");
            _expandEditBar = (Storyboard)FindResource("ExpandEditBar");

            // Save position when moved or resized
            LocationChanged += (s, e) => SavePosition();
            SizeChanged += (s, e) =>
            {
                SaveSize();
                UpdateImageWidths();
            };
            
            // Enable drag and drop
            ContentRichTextBox.AllowDrop = true;
            ContentRichTextBox.PreviewDragOver += ContentRichTextBox_PreviewDragOver;
            ContentRichTextBox.PreviewDrop += ContentRichTextBox_PreviewDrop;
            
            // Handle selection changes to track current text color
            ContentRichTextBox.SelectionChanged += ContentRichTextBox_SelectionChanged;
            
            // Handle PreviewTextInput to ensure color is applied for all input including IME
            ContentRichTextBox.PreviewTextInput += ContentRichTextBox_PreviewTextInput;
            
            // Handle TextInput event which fires after IME composition
            ContentRichTextBox.TextInput += ContentRichTextBox_TextInput;
        }

        private void LoadContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                ContentRichTextBox.Document = new FlowDocument(new Paragraph(new Run("")));
                return;
            }

            try
            {
                // Try to load as RTF
                var document = new FlowDocument();
                var textRange = new TextRange(document.ContentStart, document.ContentEnd);
                
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
                {
                    textRange.Load(stream, WpfDataFormats.Rtf);
                }
                
                // 修复加载后的图片尺寸
                FixImageSizes(document);
                
                ContentRichTextBox.Document = document;
            }
            catch
            {
                // Fallback to plain text
                ContentRichTextBox.Document = new FlowDocument(new Paragraph(new Run(content)));
            }
        }
        
        private void FixImageSizes(FlowDocument document)
        {
            var availableWidth = ContentRichTextBox.ActualWidth > 0 
                ? ContentRichTextBox.ActualWidth - ContentRichTextBox.Padding.Left - ContentRichTextBox.Padding.Right - 20
                : 360;
            
            if (document.PageWidth > 0)
            {
                availableWidth = document.PageWidth - document.PagePadding.Left - document.PagePadding.Right;
            }
            
            foreach (var block in document.Blocks)
            {
                if (block is BlockUIContainer container && container.Child is WpfImage image)
                {
                    // reset image width
                    image.Stretch = Stretch.Uniform;
                    image.Width = availableWidth;
                    
                    if (image.Source is BitmapImage bitmapImage && bitmapImage.UriSource != null)
                    {
                        var newBitmap = new BitmapImage();
                        newBitmap.BeginInit();
                        newBitmap.UriSource = bitmapImage.UriSource;
                        newBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        newBitmap.EndInit();
                        image.Source = newBitmap;
                    }

                    container.Margin = new Thickness(0, 5, 0, 5);
                }
            }
        }
        
        private void UpdateImageWidths()
        {
            var availableWidth = ContentRichTextBox.ActualWidth - ContentRichTextBox.Padding.Left - ContentRichTextBox.Padding.Right - 20;
            
            if (availableWidth <= 0)
                return;
            
            foreach (var block in ContentRichTextBox.Document.Blocks)
            {
                if (block is BlockUIContainer container && container.Child is WpfImage image)
                {
                    image.Width = availableWidth;
                }
            }
        }

        private string GetContent()
        {
            try
            {
                var textRange = new TextRange(ContentRichTextBox.Document.ContentStart, ContentRichTextBox.Document.ContentEnd);
                
                using (var stream = new MemoryStream())
                {
                    textRange.Save(stream, WpfDataFormats.Rtf);
                    return System.Text.Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            catch
            {
                return new TextRange(ContentRichTextBox.Document.ContentStart, ContentRichTextBox.Document.ContentEnd).Text;
            }
        }

        private void ContentRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveContent();
        }

        private void ContentRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            // Don't update color indicator if user has explicitly set a color and hasn't typed yet
            if (_hasExplicitColorSet)
            {
                return;
            }
            
            // Update current text color based on selection or caret position
            var selection = ContentRichTextBox.Selection;
            if (!selection.IsEmpty)
            {
                var foreground = selection.GetPropertyValue(TextElement.ForegroundProperty);
                if (foreground is SolidColorBrush brush)
                {
                    _currentTextColor = brush.Color;
                    TextColorIndicator.Fill = brush;
                }
            }
            else
            {
                // Get color at caret position
                var caretPosition = ContentRichTextBox.CaretPosition;
                var foreground = caretPosition.GetAdjacentElement(LogicalDirection.Forward)?.GetValue(TextElement.ForegroundProperty);
                
                if (foreground == null)
                {
                    foreground = caretPosition.GetAdjacentElement(LogicalDirection.Backward)?.GetValue(TextElement.ForegroundProperty);
                }
                
                if (foreground is SolidColorBrush brush)
                {
                    _currentTextColor = brush.Color;
                    TextColorIndicator.Fill = brush;
                }
            }
        }

        private void ContentRichTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // This event fires for all text input including IME (Chinese input)
            // Apply the current text color to ensure new text uses the selected color
            if (_currentTextColor != default(WpfColor))
            {
                var brush = new SolidColorBrush(_currentTextColor);
                ContentRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            }
        }

        private void ContentRichTextBox_TextInput(object sender, TextCompositionEventArgs e)
        {
            // This event fires after text is actually inserted (including after IME composition)
            // Re-apply the color to ensure it sticks even after IME processing
            if (_currentTextColor != default(WpfColor) && !string.IsNullOrEmpty(e.Text))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var caretPosition = ContentRichTextBox.CaretPosition;
                        if (caretPosition != null)
                        {
                            // Get the text that was just inserted
                            var start = caretPosition.GetPositionAtOffset(-e.Text.Length, LogicalDirection.Backward);
                            if (start != null)
                            {
                                var range = new TextRange(start, caretPosition);
                                var brush = new SolidColorBrush(_currentTextColor);
                                range.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                            }
                        }
                        
                        // Clear the explicit color flag after text is actually typed
                        _hasExplicitColorSet = false;
                    }
                    catch
                    {
                        // Ignore any errors
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentRichTextBox.Selection;
            if (!selection.IsEmpty)
            {
                var currentWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
                selection.ApplyPropertyValue(TextElement.FontWeightProperty, 
                    currentWeight.Equals(FontWeights.Bold) ? FontWeights.Normal : FontWeights.Bold);
            }
        }

        private void Italic_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentRichTextBox.Selection;
            if (!selection.IsEmpty)
            {
                var currentStyle = selection.GetPropertyValue(TextElement.FontStyleProperty);
                selection.ApplyPropertyValue(TextElement.FontStyleProperty, 
                    currentStyle.Equals(FontStyles.Italic) ? FontStyles.Normal : FontStyles.Italic);
            }
        }

        private void Underline_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentRichTextBox.Selection;
            if (!selection.IsEmpty)
            {
                var currentDecoration = selection.GetPropertyValue(Inline.TextDecorationsProperty);
                selection.ApplyPropertyValue(Inline.TextDecorationsProperty, 
                    currentDecoration == TextDecorations.Underline ? null : TextDecorations.Underline);
            }
        }

        private void Strikethrough_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentRichTextBox.Selection;
            if (!selection.IsEmpty)
            {
                var currentDecoration = selection.GetPropertyValue(Inline.TextDecorationsProperty);
                selection.ApplyPropertyValue(Inline.TextDecorationsProperty, 
                    currentDecoration == TextDecorations.Strikethrough ? null : TextDecorations.Strikethrough);
            }
        }

        private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ContentRichTextBox == null)
                return;
                
            if (FontSizeCombo?.SelectedItem is WpfComboBoxItem item && item.Tag != null)
            {
                var fontSize = double.Parse(item.Tag.ToString());
                var selection = ContentRichTextBox.Selection;
                if (!selection.IsEmpty)
                {
                    selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSize);
                }
            }
        }

        private void TextColor_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as WpfButton;
            if (button != null)
            {
                var contextMenu = new ContextMenu();
                
                var presetColors = new[]
                {
                    ("#000000", "黑色"),
                    ("#FF0000", "红色"),
                    ("#00FF00", "绿色"),
                    ("#0000FF", "蓝色"),
                    ("#FFFF00", "黄色"),
                    ("#FF00FF", "品红"),
                    ("#00FFFF", "青色"),
                    ("#FFA500", "橙色"),
                    ("#800080", "紫色"),
                    ("#808080", "灰色"),
                    ("#A52A2A", "棕色")
                };
                
                foreach (var (colorHex, colorName) in presetColors)
                {
                    var menuItem = new MenuItem { Header = colorName };
                    
                    // Create a small color preview rectangle
                    var colorRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = 16,
                        Height = 16,
                        Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(colorHex)),
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    
                    var stackPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    stackPanel.Children.Add(colorRect);
                    stackPanel.Children.Add(new TextBlock { Text = colorName, VerticalAlignment = VerticalAlignment.Center });
                    
                    menuItem.Header = stackPanel;
                    menuItem.Tag = colorHex;
                    
                    menuItem.Click += (s, args) =>
                    {
                        var selectedColor = (s as MenuItem)?.Tag as string;
                        if (selectedColor != null)
                        {
                            ApplyTextColor(selectedColor);
                        }
                    };
                    
                    contextMenu.Items.Add(menuItem);
                }
                
                contextMenu.PlacementTarget = button;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                contextMenu.IsOpen = true;
            }
        }
        
        private void ApplyTextColor(string colorHex)
        {
            try
            {
                var color = (WpfColor)WpfColorConverter.ConvertFromString(colorHex);
                var brush = new SolidColorBrush(color);
                
                // Update current text color
                _currentTextColor = color;
                
                // Update the color indicator in the toolbar
                TextColorIndicator.Fill = brush;
                
                var selection = ContentRichTextBox.Selection;
                if (!selection.IsEmpty)
                {
                    // Apply to selected text only
                    selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                    
                    // Don't set the flag when modifying existing text
                    _hasExplicitColorSet = false;
                }
                else
                {
                    // When no text is selected, set the typing attributes directly
                    // Use the BeginChange/EndChange to make this an atomic operation
                    ContentRichTextBox.BeginChange();
                    try
                    {
                        // Apply the color to the current selection (even though it's empty)
                        // This sets the typing format for the next character
                        ContentRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                    }
                    finally
                    {
                        ContentRichTextBox.EndChange();
                    }
                    
                    // Set flag to indicate user has explicitly chosen a color
                    _hasExplicitColorSet = true;
                }
                
                // Focus back to the text box
                ContentRichTextBox.Focus();
            }
            catch
            {
                // If color conversion fails, ignore
            }
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new WpfOpenFileDialog
            {
                Filter = "Image File|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                Title = "Select Image to Insert"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InsertImage(openFileDialog.FileName);
            }
        }

        private void ContentRichTextBox_PreviewDragOver(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
                if (files != null && files.Length > 0 && IsImageFile(files[0]))
                {
                    e.Effects = WpfDragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
        }

        private void ContentRichTextBox_PreviewDrop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
                if (files != null && files.Length > 0 && IsImageFile(files[0]))
                {
                    InsertImage(files[0]);
                    e.Handled = true;
                }
            }
        }

        private bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || 
                   extension == ".gif" || extension == ".bmp" || extension == ".webp";
        }

        private void InsertImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                // Copy image to note's image directory
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                destPath = Path.Combine(_imageDirectory, fileName);
                File.Copy(sourceFilePath, destPath, true);

                // Insert image into RichTextBox
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(destPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                var image = new WpfImage
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform
                };
                
                image.Width = ContentRichTextBox.Document.PageWidth > 0 
                    ? ContentRichTextBox.Document.PageWidth - ContentRichTextBox.Document.PagePadding.Left - ContentRichTextBox.Document.PagePadding.Right
                    : ContentRichTextBox.ActualWidth - ContentRichTextBox.Padding.Left - ContentRichTextBox.Padding.Right - 20;

                var container = new BlockUIContainer(image)
                {
                    Margin = new Thickness(0, 5, 0, 5)
                };
                
                ContentRichTextBox.BeginChange();
                try
                {
                    var insertPosition = ContentRichTextBox.CaretPosition;
                    
                    if (insertPosition != null)
                    {
                        var currentParagraph = insertPosition.Paragraph;
                        if (currentParagraph != null)
                        {
                            ContentRichTextBox.Document.Blocks.InsertAfter(currentParagraph, container);
                            
                            ContentRichTextBox.Document.Blocks.InsertAfter(container, new Paragraph(new Run("")));
                        }
                        else
                        {
                            ContentRichTextBox.Document.Blocks.Add(container);
                            ContentRichTextBox.Document.Blocks.Add(new Paragraph(new Run("")));
                        }
                    }
                    else
                    {
                        ContentRichTextBox.Document.Blocks.Add(container);
                        ContentRichTextBox.Document.Blocks.Add(new Paragraph(new Run("")));
                    }
                }
                finally
                {
                    ContentRichTextBox.EndChange();
                }
                
                SaveContent();
            }
            catch (Exception ex)
            {

                if (destPath != null && File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch
                    {

                    }
                }
                
                WpfMessageBox.Show($"Fail to insert image: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePosition()
        {
            if (NoteData != null && WindowState == WindowState.Normal)
            {
                NoteData.Left = Left;
                NoteData.Top = Top;
                NoteManager.Instance.UpdateNote(NoteData);
            }
        }

        private void SaveSize()
        {
            if (NoteData != null && WindowState == WindowState.Normal)
            {
                NoteData.Width = Width;
                NoteData.Height = Height;
                NoteManager.Instance.UpdateNote(NoteData);
            }
        }

        private void SaveContent()
        {
            if (NoteData != null)
            {
                NoteData.Content = GetContent();
                NoteManager.Instance.UpdateNote(NoteData);
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // 只有当前的 BottomMost 窗口才执行定时器任务
            if (NoteData.Level == WindowLevel.BottomMost && _hwnd != IntPtr.Zero && _currentBottomMostWindow == this)
            {
                SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, 
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyWindowLevel();
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            _expandEditBar?.Begin();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            _collapseEditBar?.Begin();
        }

        private void MainBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _expandEditBar?.Begin();
        }

        private void MainBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!IsActive)
            {
                _collapseEditBar?.Begin();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                try
                {
                    this.DragMove();
                }
                catch { }
                
                if (NoteData.Level == WindowLevel.BottomMost)
                {
                    ApplyWindowLevel();
                }
            }
        }

        private void ShowMainWindow_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as WpfButton;
            if (button != null)
            {
                var contextMenu = new ContextMenu();
                
                var showMainWindowItem = new MenuItem { Header = "显示主窗口" };
                showMainWindowItem.Click += (s, args) =>
                {
                    var app = System.Windows.Application.Current as App;
                    if (app?.MainWindow != null)
                    {
                        app.MainWindow.Show();
                        app.MainWindow.WindowState = WindowState.Normal;
                        app.MainWindow.Activate();
                    }
                };
                
                var createNoteItem = new MenuItem { Header = "新建便签" };
                createNoteItem.Click += (s, args) =>
                {
                    var newNote = NoteManager.Instance.CreateNote();
                    var newWindow = new FloatingWindow(newNote);
                    newWindow.Show();
                };
                
                var createTopMostNoteItem = new MenuItem { Header = "新建置顶便签" };
                createTopMostNoteItem.Click += (s, args) =>
                {
                    var newNote = NoteManager.Instance.CreateNote(WindowLevel.TopMost);
                    var newWindow = new FloatingWindow(newNote);
                    newWindow.Show();
                };
                
                contextMenu.Items.Add(showMainWindowItem);
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(createNoteItem);
                contextMenu.Items.Add(createTopMostNoteItem);
                
                contextMenu.PlacementTarget = button;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                contextMenu.IsOpen = true;
            }
        }

        private void MoreOptions_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as WpfButton;
            if (button != null)
            {
                var contextMenu = new ContextMenu();
                
                var deleteNoteItem = new MenuItem { Header = "Del the Note" };
                deleteNoteItem.Click += (s, args) =>
                {
                    var result = WpfMessageBox.Show(
                        "确定要删除这个便签吗？",
                        "确认删除",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        NoteManager.Instance.DeleteNote(NoteData);
                        this.Close();
                    }
                };
                
                var clearContentItem = new MenuItem { Header = "清空内容" };
                clearContentItem.Click += (s, args) =>
                {
                    var result = WpfMessageBox.Show(
                        "确定要清空便签内容吗？",
                        "确认清空",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        ContentRichTextBox.Document = new FlowDocument(new Paragraph(new Run("")));
                        SaveContent();
                    }
                };
                
                var aboutItem = new MenuItem { Header = "关于" };
                aboutItem.Click += (s, args) =>
                {
                    WpfMessageBox.Show(
                        "YASN - Yet Another Sticky Notes\n版本 1.0\n\n一个简洁的便签应用程序",
                        "关于 YASN",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };
                
                var toggleThemeItem = new MenuItem { Header = NoteData.IsDarkMode ? "切换到白天模式" : "切换到黑夜模式" };
                toggleThemeItem.Click += (s, args) =>
                {
                    ToggleTheme();
                };
                
                var changeTitleBarColorItem = new MenuItem { Header = "更改标题栏颜色" };
                changeTitleBarColorItem.Click += (s, args) =>
                {
                    ShowColorPicker();
                };
                
                contextMenu.Items.Add(deleteNoteItem);
                contextMenu.Items.Add(clearContentItem);
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(toggleThemeItem);
                contextMenu.Items.Add(changeTitleBarColorItem);
                contextMenu.Items.Add(aboutItem);
                
                contextMenu.PlacementTarget = button;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                contextMenu.IsOpen = true;
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (NoteData.Level == WindowLevel.TopMost)
            {
                SetWindowLevel(WindowLevel.Normal);
            }
            else
            {
                SetWindowLevel(WindowLevel.TopMost);
            }
        }

        private void SendToBottom_Click(object sender, RoutedEventArgs e)
        {
            SetWindowLevel(WindowLevel.BottomMost);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            this.Close();
        }

        private void SetWindowLevel(WindowLevel level)
        {
            NoteData.Level = level;
            
            UpdateStatusText();
            UpdatePinButton();
            NoteManager.Instance.UpdateNote(NoteData);
            
            if (_hwnd != IntPtr.Zero)
            {
                ApplyWindowLevel();
            }
        }

        private void UpdatePinButton()
        {
            if (NoteData.Level == WindowLevel.TopMost)
            {
                PinButton.Content = "^";
                PinButton.ToolTip = "Unpin from Top";
            }
            else
            {
                PinButton.Content = "P";
                PinButton.ToolTip = "Pin to Top";
            }
        }

        private void UpdateStatusText()
        {
            string levelPrefix = NoteData.Level switch
            {
                WindowLevel.TopMost => "[T] ",
                WindowLevel.BottomMost => "[B] ",
                _ => ""
            };
            
            StatusText.Text = $"{levelPrefix}{NoteData.Title}";
        }

        private void ApplyWindowLevel()
        {
            if (_hwnd == IntPtr.Zero)
                return;

            // 先停止定时器
            _timer?.Stop();

            switch (NoteData.Level)
            {
                case WindowLevel.TopMost:
                    this.Topmost = true;
                    break;

                case WindowLevel.BottomMost:
                    this.Topmost = false;
                    
                    lock (_bottomMostLock)
                    {
                        // 如果已经有其他窗口是 BottomMost，将其改为 Normal
                        if (_currentBottomMostWindow != null && _currentBottomMostWindow != this)
                        {
                            var previousWindow = _currentBottomMostWindow;
                            _currentBottomMostWindow = null; // 先清空，避免递归
                            
                            // 将之前的 BottomMost 窗口改为 Normal
                            previousWindow.Dispatcher.Invoke(() =>
                            {
                                previousWindow.SetWindowLevel(WindowLevel.Normal);
                            });
                        }
                        
                        // 设置当前窗口为 BottomMost
                        _currentBottomMostWindow = this;
                    }
                    
                    // 设置窗口到底层
                    SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, 
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, 
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    
                    // 启动定时器
                    _timer?.Start();
                    break;

                case WindowLevel.Normal:
                default:
                    this.Topmost = false;
                    
                    // 如果当前窗口是 BottomMost 窈口，清空引用
                    lock (_bottomMostLock)
                    {
                        if (_currentBottomMostWindow == this)
                        {
                            _currentBottomMostWindow = null;
                        }
                    }
                    
                    SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, 
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    break;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            
            // 只有当前的 BottomMost 窗口才重新应用窗口级别
            if (NoteData.Level == WindowLevel.BottomMost && _hwnd != IntPtr.Zero && _currentBottomMostWindow == this)
            {
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, 
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            
            // 如果当前窗口是 BottomMost 窗口，清空引用
            lock (_bottomMostLock)
            {
                if (_currentBottomMostWindow == this)
                {
                    _currentBottomMostWindow = null;
                }
            }
            
            // Only set IsOpen to false if this is a user-initiated close, not application shutdown
            if (!_isApplicationShuttingDown)
            {
                NoteData.IsOpen = false;
            }
            
            NoteData.Window = null;
            NoteManager.Instance.UpdateNote(NoteData);
            base.OnClosed(e);
        }
        
        private void ToggleTheme()
        {
            NoteData.IsDarkMode = !NoteData.IsDarkMode;
            ApplyTheme(NoteData.IsDarkMode);
            
            // Update text colors for black text when switching themes
            UpdateTextColorsForTheme(NoteData.IsDarkMode);
            
            NoteManager.Instance.UpdateNote(NoteData);
        }
        
        private void UpdateTextColorsForTheme(bool isDarkMode)
        {
            // Get the color to replace (black in light mode, white in dark mode)
            var oldColor = isDarkMode ? Colors.Black : Colors.White;
            var newColor = isDarkMode ? Colors.White : Colors.Black;
            
            // Iterate through all blocks in the document
            foreach (var block in ContentRichTextBox.Document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    UpdateInlineColors(paragraph.Inlines, oldColor, newColor);
                }
            }
            
            // Save the updated content
            SaveContent();
        }
        
        private void UpdateInlineColors(InlineCollection inlines, WpfColor oldColor, WpfColor newColor)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    if (run.Foreground is SolidColorBrush brush && brush.Color == oldColor)
                    {
                        run.Foreground = new SolidColorBrush(newColor);
                    }
                }
                else if (inline is Span span)
                {
                    // Recursively update nested inlines
                    UpdateInlineColors(span.Inlines, oldColor, newColor);
                    
                    // Also check the span itself
                    if (span.Foreground is SolidColorBrush brush && brush.Color == oldColor)
                    {
                        span.Foreground = new SolidColorBrush(newColor);
                    }
                }
            }
        }
        
        private void ApplyTheme(bool isDarkMode)
        {
            if (isDarkMode)
            {
                // 黑夜模式：黑底白字
                MainBorder.Background = new SolidColorBrush(WpfColor.FromArgb(0xC8, 0x1E, 0x1E, 0x1E)); // 深灰黑色
                MainBorder.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x60, 0x80, 0x80, 0x80));
                StatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xEC, 0xF0, 0xF1)); // 浅色文字
                FormatToolbar.Background = new SolidColorBrush(WpfColor.FromArgb(0x40, 0x00, 0x00, 0x00));
                ContentRichTextBox.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xEC, 0xF0, 0xF1)); // 白色文字
            }
            else
            {
                // 白天模式：白底黑字
                MainBorder.Background = new SolidColorBrush(WpfColor.FromArgb(0xF0, 0xFF, 0xFF, 0xF0)); // 米白色
                MainBorder.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x60, 0xC0, 0xC0, 0xC0));
                StatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x2C, 0x3E, 0x50)); // 深色文字
                FormatToolbar.Background = new SolidColorBrush(WpfColor.FromArgb(0x30, 0xE0, 0xE0, 0xE0)); // 浅灰色
                ContentRichTextBox.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x2C, 0x3E, 0x50)); // 深色文字
            }
            // 标题栏颜色不在这里设置，由 ApplyTitleBarColor 独立管理
        }
        
        private void ApplyTitleBarColor(string colorHex)
        {
            try
            {
                var color = (WpfColor)WpfColorConverter.ConvertFromString(colorHex);
                TitleBar.Background = new SolidColorBrush(color);
            }
            catch
            {
                // 如果颜色格式错误，使用默认颜色
                TitleBar.Background = new SolidColorBrush(WpfColor.FromArgb(0xE6, 0xD4, 0xC5, 0xE0));
            }
        }
        
        private void ShowColorPicker()
        {
            // 创建颜色选择对话框
            var colorPickerWindow = new Window
            {
                Title = "选择标题栏颜色",
                Width = 400,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };
            
            var stackPanel = new StackPanel { Margin = new Thickness(20) };
            
            // 预设颜色
            var colorsGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 20) };
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            
            var presetColors = new[]
            {
                ("#E6D4C5E0", "淡紫色"),
                ("#E6FFB6C1", "粉红色"),
                ("#E6B0E0E6", "淡蓝色"),
                ("#E6C8E6C9", "淡绿色"),
                ("#E6FFE4B5", "淡黄色"),
                ("#E6F5DEB3", "小麦色"),
                ("#E6E6E6FA", "薰衣草色"),
                ("#E6FFE4E1", "玫瑰白")
            };
            
            int row = 0;
            int col = 0;
            foreach (var (colorHex, colorName) in presetColors)
            {
                var button = new WpfButton
                {
                    Width = 80,
                    Height = 60,
                    Margin = new Thickness(5),
                    Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(colorHex)),
                    Content = colorName,
                    Tag = colorHex
                };
                
                button.Click += (s, e) =>
                {
                    var selectedColor = (s as WpfButton)?.Tag as string;
                    if (selectedColor != null)
                    {
                        NoteData.TitleBarColor = selectedColor;
                        ApplyTitleBarColor(selectedColor);
                        NoteManager.Instance.UpdateNote(NoteData);
                        colorPickerWindow.Close();
                    }
                };
                
                System.Windows.Controls.Grid.SetRow(button, row);
                System.Windows.Controls.Grid.SetColumn(button, col);
                
                if (!colorsGrid.RowDefinitions.Any() || col == 0)
                {
                    colorsGrid.RowDefinitions.Add(new RowDefinition());
                }
                
                colorsGrid.Children.Add(button);
                
                col++;
                if (col >= 4)
                {
                    col = 0;
                    row++;
                }
            }
            
            stackPanel.Children.Add(new TextBlock 
            { 
                Text = "选择预设颜色：", 
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            stackPanel.Children.Add(colorsGrid);
            
            colorPickerWindow.Content = stackPanel;
            colorPickerWindow.ShowDialog();
        }
        
        private void SetBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as WpfButton;
            if (button != null)
            {
                var contextMenu = new ContextMenu();
                
                var selectImageItem = new MenuItem { Header = "选择背景图片" };
                selectImageItem.Click += (s, args) =>
                {
                    var openFileDialog = new WpfOpenFileDialog
                    {
                        Filter = "Image File|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                        Title = "Select Background Image"
                    };

                    if (openFileDialog.ShowDialog() == true)
                    {
                        SetBackgroundImage(openFileDialog.FileName);
                    }
                };
                
                var clearBackgroundItem = new MenuItem { Header = "清除背景图片" };
                clearBackgroundItem.Click += (s, args) =>
                {
                    ClearBackgroundImage();
                };
                
                var adjustOpacityItem = new MenuItem { Header = "调整透明度" };
                adjustOpacityItem.Click += (s, args) =>
                {
                    ShowOpacityAdjuster();
                };
                
                contextMenu.Items.Add(selectImageItem);
                contextMenu.Items.Add(clearBackgroundItem);
                contextMenu.Items.Add(adjustOpacityItem);
                
                contextMenu.PlacementTarget = button;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                contextMenu.IsOpen = true;
            }
        }
        
        private void SetBackgroundImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                // Copy image to note's background image directory
                var fileName = $"background{Path.GetExtension(sourceFilePath)}";
                destPath = Path.Combine(_backgroundImageDirectory, fileName);
                
                // Delete old background image if exists
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
                
                File.Copy(sourceFilePath, destPath, true);
                
                // Update note data and apply background
                NoteData.BackgroundImagePath = destPath;
                ApplyBackgroundImage(destPath);
                NoteManager.Instance.UpdateNote(NoteData);
            }
            catch (Exception ex)
            {
                if (destPath != null && File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch
                    {
                    }
                }
                
                WpfMessageBox.Show($"Fail to set background image: {ex.Message}", "Error", 
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
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                BackgroundImageBrush.ImageSource = bitmap;
            }
            catch
            {
                BackgroundImageBrush.ImageSource = null;
            }
        }
        
        private void ClearBackgroundImage()
        {
            NoteData.BackgroundImagePath = null;
            BackgroundImageBrush.ImageSource = null;
            NoteManager.Instance.UpdateNote(NoteData);
            
            // Optionally delete the background image file
            if (Directory.Exists(_backgroundImageDirectory))
            {
                try
                {
                    var files = Directory.GetFiles(_backgroundImageDirectory);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }
        
        private void ShowOpacityAdjuster()
        {
            var opacityWindow = new Window
            {
                Title = "调整背景图片透明度",
                Width = 300,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };
            
            var stackPanel = new StackPanel { Margin = new Thickness(20) };
            
            stackPanel.Children.Add(new TextBlock 
            { 
                Text = "背景图片透明度：", 
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            
            var slider = new Slider
            {
                Minimum = 0.05,
                Maximum = 1.0,
                Value = BackgroundImageBorder.Opacity,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            var valueText = new TextBlock
            {
                Text = $"当前值: {BackgroundImageBorder.Opacity:F2}",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            slider.ValueChanged += (s, e) =>
            {
                BackgroundImageBorder.Opacity = slider.Value;
                valueText.Text = $"当前值: {slider.Value:F2}";
                
                // Save the opacity value to NoteData immediately
                NoteData.BackgroundImageOpacity = slider.Value;
                NoteManager.Instance.UpdateNote(NoteData);
            };
            
            stackPanel.Children.Add(slider);
            stackPanel.Children.Add(valueText);
            
            opacityWindow.Content = stackPanel;
            opacityWindow.ShowDialog();
        }
    }
}
