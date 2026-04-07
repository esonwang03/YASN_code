using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using CopyPixelOperation = System.Drawing.CopyPixelOperation;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using ImageControl = System.Windows.Controls.Image;
using YASN.Infrastructure.Logging;
using Color = System.Windows.Media.Color;
using ImageSource = System.Windows.Media.ImageSource;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace YASN.App.WindowLayout
{
    internal sealed class WindowLayoutPickerWindow : Window
    {
        private const double CompactMaxWidth = 1120;
        private const double CompactMaxHeight = 760;
        private const double ResizeOnlyMaxWidth = 1260;
        private const double ResizeOnlyMaxHeight = 900;
        private const double SingleDesktopMaxWidth = 1180;
        private const double SingleDesktopMaxHeight = 720;
        private const double GalleryDesktopMaxWidth = 320;
        private const double GalleryDesktopMaxHeight = 210;
        private const double MinimumSelectionSize = 10;

        private readonly FloatingWindow _target;
        private readonly QuickWindowLayoutMode _mode;
        private readonly IReadOnlyList<VirtualDesktopEntry> _desktops;
        private readonly Rect _desktopBoundsPx;
        private readonly Rect _targetBoundsPx;
        private readonly ImageSource? _currentDesktopScreenshot;
        //private readonly TextBlock _desktopSummaryText = new();

        internal WindowLayoutPickerWindow(FloatingWindow target, QuickWindowLayoutMode mode)
        {
            _target = target;
            _mode = mode;
            _desktopBoundsPx = GetDesktopBoundsPx();
            _targetBoundsPx = GetWindowBoundsPx(target);
            _currentDesktopScreenshot = CaptureCurrentDesktopScreenshot(_desktopBoundsPx);

            IReadOnlyList<VirtualDesktopEntry> desktops = VirtualDesktopCatalog.GetDesktops(target);
            _desktops = mode == QuickWindowLayoutMode.ResizeOnly
                ? [VirtualDesktopCatalog.GetCurrentDesktop(target)]
                : desktops;

            AppLogger.Debug(
                $"Quick layout picker init: mode={mode}, target={DescribeTarget()}, desktopBoundsPx={DescribeRect(_desktopBoundsPx)}, targetBoundsPx={DescribeRect(_targetBoundsPx)}, desktops={string.Join(", ", _desktops.Select(static desktop => $"{desktop.Name}:{desktop.Id}"))}");

            Title = "Quick Layout";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0x88, 0x00, 0x00, 0x00));
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _desktopBoundsPx.Left;
            Top = _desktopBoundsPx.Top;
            Width = Math.Max(1, _desktopBoundsPx.Width);
            Height = Math.Max(1, _desktopBoundsPx.Height);
            PreviewKeyDown += WindowLayoutPickerWindow_PreviewKeyDown;

            Content = BuildContent();
            // UpdateDesktopSummary();
        }

        private UIElement BuildContent()
        {
            Grid root = new()
            {
                Background = System.Windows.Media.Brushes.Transparent
            };
            root.MouseLeftButtonDown += Root_MouseLeftButtonDown;

            Border panel = new()
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x18, 0x1D)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                MaxWidth = _mode == QuickWindowLayoutMode.ResizeOnly ? ResizeOnlyMaxWidth : CompactMaxWidth,
                MaxHeight = _mode == QuickWindowLayoutMode.ResizeOnly ? ResizeOnlyMaxHeight : CompactMaxHeight
            };

            StackPanel layout = new();
            layout.Children.Add(new TextBlock
            {
                Text = GetTitleText(),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            // layout.Children.Add(new TextBlock
            // {
            //     Text = GetSubtitleText(),
            //     Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xDB, 0xE2)),
            //     FontSize = 13,
            //     Margin = new Thickness(0, 0, 0, 10)
            // });

            // _desktopSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xC7, 0xFF));
            // _desktopSummaryText.FontSize = 12;
            // _desktopSummaryText.Margin = new Thickness(0, 0, 0, 10);
            // layout.Children.Add(_desktopSummaryText);

            layout.Children.Add(CreateDesktopGallery());
            panel.Child = layout;
            root.Children.Add(panel);
            return root;
        }

        private UIElement CreateDesktopGallery()
        {
            if (_desktops.Count == 1)
            {
                DesktopMapMetrics metrics = DesktopMapMetrics.Create(_desktopBoundsPx, SingleDesktopMaxWidth, SingleDesktopMaxHeight);
                return CreateDesktopCard(_desktops[0], metrics, false);
            }

            WrapPanel gallery = new()
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            DesktopMapMetrics sharedMetrics = DesktopMapMetrics.Create(_desktopBoundsPx, GalleryDesktopMaxWidth, GalleryDesktopMaxHeight);
            foreach (VirtualDesktopEntry desktop in _desktops)
            {
                gallery.Children.Add(CreateDesktopCard(desktop, sharedMetrics, true));
            }

            AppLogger.Debug($"Quick layout picker gallery created: target={DescribeTarget()}, desktopCount={_desktops.Count}");

            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = CompactMaxHeight - 180,
                Content = gallery
            };
        }

        private UIElement CreateDesktopCard(VirtualDesktopEntry desktop, DesktopMapMetrics metrics, bool compact)
        {
            StackPanel cardContent = new();
            cardContent.Children.Add(new TextBlock
            {
                Text = desktop.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = compact ? 13 : 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            cardContent.Children.Add(new TextBlock
            {
                Text = GetDesktopBadgeText(desktop),
                Foreground = new SolidColorBrush(Color.FromRgb(0xB3, 0xC7, 0xD8)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            });
            cardContent.Children.Add(CreateDesktopSurface(desktop, metrics));

            return new Border
            {
                Width = metrics.Width + 20,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(desktop.IsCurrent ? Color.FromArgb(0x99, 0x4F, 0xC3, 0xF7) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(desktop.IsCurrent ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Child = cardContent
            };
        }

        private UIElement CreateDesktopSurface(VirtualDesktopEntry desktop, DesktopMapMetrics metrics)
        {
            Grid surface = new()
            {
                Width = metrics.Width,
                Height = metrics.Height,
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x16)),
                ClipToBounds = true
            };

            surface.Children.Add(CreateDesktopBackground(desktop, metrics));
            surface.Children.Add(CreateWindowPreviewOverlay(metrics));

            Canvas overlay = new()
            {
                Width = metrics.Width,
                Height = metrics.Height,
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = _mode == QuickWindowLayoutMode.Move ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Cross
            };

            if (_mode == QuickWindowLayoutMode.Move)
            {
                overlay.MouseLeftButtonUp += (_, e) =>
                {
                    Point clickedPoint = e.GetPosition(overlay);
                    Point screenPointPx = metrics.CanvasToScreen(clickedPoint);
                    Rect newBoundsPx = new(screenPointPx.X, screenPointPx.Y, _targetBoundsPx.Width, _targetBoundsPx.Height);
                    AppLogger.Debug(
                        $"Quick layout picker click move: target={DescribeTarget()}, desktop={desktop.Name} ({desktop.Id}), canvasPoint={DescribePoint(clickedPoint)}, screenPointPx={DescribePoint(screenPointPx)}, boundsPx={DescribeRect(newBoundsPx)}");
                    Close();
                    FloatingWindowQuickActions.ApplyWindowBounds(_target, PixelsToDipRect(newBoundsPx), desktop);
                };
            }
            else
            {
                AddSelectionBehavior(overlay, metrics, desktop);
            }

            surface.Children.Add(overlay);

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = surface
            };
        }

        private UIElement CreateDesktopBackground(VirtualDesktopEntry desktop, DesktopMapMetrics metrics)
        {
            if (desktop.IsCurrent && _currentDesktopScreenshot != null)
            {
                AppLogger.Debug($"Quick layout picker background: using current desktop screenshot for {DescribeTarget()} on {desktop.Name} ({desktop.Id})");
                return new ImageControl
                {
                    Source = _currentDesktopScreenshot,
                    Width = metrics.Width,
                    Height = metrics.Height,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false
                };
            }

            string fallbackReason = desktop.IsCurrent ? "screenshot capture failed" : "selected desktop is not current";
            AppLogger.Debug($"Quick layout picker background: using placeholder for {DescribeTarget()} on {desktop.Name} ({desktop.Id}) because {fallbackReason}");
            return CreateDesktopPlaceholder(metrics, desktop);
        }

        private UIElement CreateDesktopPlaceholder(DesktopMapMetrics metrics, VirtualDesktopEntry desktop)
        {
            Grid placeholder = new()
            {
                Width = metrics.Width,
                Height = metrics.Height,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x16, 0x1B, 0x22),
                    Color.FromRgb(0x0F, 0x13, 0x18),
                    90)
            };

            Canvas monitorCanvas = new()
            {
                Width = metrics.Width,
                Height = metrics.Height,
                IsHitTestVisible = false
            };

            int screenIndex = 1;
            foreach (Screen screen in Screen.AllScreens)
            {
                Rect monitorBounds = new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
                Rect scaledMonitor = metrics.ScreenToCanvas(monitorBounds);

                Border monitorRect = new()
                {
                    Width = Math.Max(12, scaledMonitor.Width),
                    Height = Math.Max(12, scaledMonitor.Height),
                    Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4)
                };
                monitorCanvas.Children.Add(monitorRect);
                Canvas.SetLeft(monitorRect, scaledMonitor.Left);
                Canvas.SetTop(monitorRect, scaledMonitor.Top);

                TextBlock monitorLabel = new()
                {
                    Text = $"Display {screenIndex}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEB, 0xF0)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                };
                monitorCanvas.Children.Add(monitorLabel);
                Canvas.SetLeft(monitorLabel, scaledMonitor.Left + 8);
                Canvas.SetTop(monitorLabel, scaledMonitor.Top + 6);

                screenIndex++;
            }

            TextBlock placeholderLabel = new()
            {
                Text = desktop.IsCurrent ? "Current desktop placeholder" : "Virtual desktop placeholder",
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock resolutionLabel = new()
            {
                Text = $"{Math.Round(_desktopBoundsPx.Width)} x {Math.Round(_desktopBoundsPx.Height)}",
                Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
                FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 28, 0, 0)
            };

            placeholder.Children.Add(monitorCanvas);
            placeholder.Children.Add(placeholderLabel);
            placeholder.Children.Add(resolutionLabel);
            return placeholder;
        }

        private UIElement CreateWindowPreviewOverlay(DesktopMapMetrics metrics)
        {
            Canvas canvas = new()
            {
                Width = metrics.Width,
                Height = metrics.Height,
                IsHitTestVisible = false
            };

            Rect targetRect = metrics.ScreenToCanvas(_targetBoundsPx);
            Border preview = new()
            {
                Width = Math.Max(24, targetRect.Width),
                Height = Math.Max(18, targetRect.Height),
                Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xC1, 0x07)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4)
            };

            TextBlock previewLabel = new()
            {
                Text = "Current window",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };

            canvas.Children.Add(preview);
            canvas.Children.Add(previewLabel);
            Canvas.SetLeft(preview, targetRect.Left);
            Canvas.SetTop(preview, targetRect.Top);
            Canvas.SetLeft(previewLabel, targetRect.Left + 6);
            Canvas.SetTop(previewLabel, targetRect.Top + 4);
            return canvas;
        }

        private void AddSelectionBehavior(Canvas overlay, DesktopMapMetrics metrics, VirtualDesktopEntry desktop)
        {
            Point? dragStart = null;
            Rectangle selectionRect = new()
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x4F, 0xC3, 0xF7)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            overlay.Children.Add(selectionRect);

            overlay.MouseLeftButtonDown += (_, e) =>
            {
                dragStart = e.GetPosition(overlay);
                selectionRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(selectionRect, dragStart.Value.X);
                Canvas.SetTop(selectionRect, dragStart.Value.Y);
                selectionRect.Width = 0;
                selectionRect.Height = 0;
                overlay.CaptureMouse();
                AppLogger.Debug($"Quick layout picker drag start: target={DescribeTarget()}, desktop={desktop.Name} ({desktop.Id}), point={DescribePoint(dragStart.Value)}");
            };

            overlay.MouseMove += (_, e) =>
            {
                if (!dragStart.HasValue || !overlay.IsMouseCaptured)
                {
                    return;
                }

                Rect rect = CreateRect(dragStart.Value, e.GetPosition(overlay));
                Canvas.SetLeft(selectionRect, rect.Left);
                Canvas.SetTop(selectionRect, rect.Top);
                selectionRect.Width = rect.Width;
                selectionRect.Height = rect.Height;
            };

            overlay.MouseLeftButtonUp += (_, e) =>
            {
                if (!dragStart.HasValue)
                {
                    return;
                }

                Rect selection = CreateRect(dragStart.Value, e.GetPosition(overlay));
                Point releasePoint = e.GetPosition(overlay);
                Point startPoint = dragStart.Value;
                dragStart = null;
                overlay.ReleaseMouseCapture();
                selectionRect.Visibility = Visibility.Collapsed;

                if (selection.Width < MinimumSelectionSize || selection.Height < MinimumSelectionSize)
                {
                    Point screenPointPx = metrics.CanvasToScreen(releasePoint);
                    Rect newBoundsPx = new(screenPointPx.X, screenPointPx.Y, _targetBoundsPx.Width, _targetBoundsPx.Height);
                    AppLogger.Debug(
                        $"Quick layout picker click-position mode: target={DescribeTarget()}, desktop={desktop.Name} ({desktop.Id}), start={DescribePoint(startPoint)}, release={DescribePoint(releasePoint)}, screenPointPx={DescribePoint(screenPointPx)}, boundsPx={DescribeRect(newBoundsPx)}");
                    Close();
                    FloatingWindowQuickActions.ApplyWindowBounds(_target, PixelsToDipRect(newBoundsPx), desktop);
                    return;
                }

                Rect resizedBoundsPx = metrics.CanvasToScreen(selection);
                AppLogger.Debug(
                    $"Quick layout picker drag apply: target={DescribeTarget()}, desktop={desktop.Name} ({desktop.Id}), selection={DescribeRect(selection)}, boundsPx={DescribeRect(resizedBoundsPx)}");
                Close();
                FloatingWindowQuickActions.ApplyWindowBounds(_target, PixelsToDipRect(resizedBoundsPx), desktop);
            };
        }

        // private void UpdateDesktopSummary()
        // {
        //     int currentCount = _desktops.Count(static desktop => desktop.IsCurrent);
        //     int windowCount = _desktops.Count(static desktop => desktop.IsWindowDesktop);
        //     string previewMode = _currentDesktopScreenshot == null ? "placeholder only" : "current desktop screenshot + placeholder fallback";
        //     _desktopSummaryText.Text = $"Showing {_desktops.Count} virtual desktop thumbnail(s) side by side | current={currentCount} | windowDesktop={windowCount} | {previewMode}";
        // }

        private string GetTitleText()
        {
            return _mode switch
            {
                QuickWindowLayoutMode.Move => "Quick Move",
                QuickWindowLayoutMode.MoveAndResize => "Quick Move + Resize",
                _ => "Quick Resize"
            };
        }

        // private string GetSubtitleText()
        // {
        //     return _mode switch
        //     {
        //         QuickWindowLayoutMode.Move => "Virtual desktops are shown side by side. Click directly on the target thumbnail to move the window there.",
        //         QuickWindowLayoutMode.MoveAndResize => "Virtual desktops are shown side by side. Drag a rectangle on the target thumbnail to set position and size; a short click only changes position.",
        //         _ => "Drag a rectangle to resize and reposition on the current desktop. A short click only changes position."
        //     };
        // }

        private Rect PixelsToDipRect(Rect pixelRect)
        {
            Matrix fromDevice = GetTransformFromDevice();
            Point topLeft = fromDevice.Transform(new Point(pixelRect.Left, pixelRect.Top));
            Point bottomRight = fromDevice.Transform(new Point(pixelRect.Right, pixelRect.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private Matrix GetTransformFromDevice()
        {
            PresentationSource? source = PresentationSource.FromVisual(_target);
            return source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        }

        private static Rect CreateRect(Point start, Point end)
        {
            return new Rect(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y));
        }

        private static Rect GetDesktopBoundsPx()
        {
            Rect[] monitorBounds = Screen.AllScreens
                .Select(screen => new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height))
                .ToArray();

            double left = monitorBounds.Min(static rect => rect.Left);
            double top = monitorBounds.Min(static rect => rect.Top);
            double right = monitorBounds.Max(static rect => rect.Right);
            double bottom = monitorBounds.Max(static rect => rect.Bottom);
            return new Rect(left, top, right - left, bottom - top);
        }

        private static Rect GetWindowBoundsPx(FloatingWindow target)
        {
            PresentationSource? source = PresentationSource.FromVisual(target);
            Matrix toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            Point topLeft = toDevice.Transform(new Point(target.Left, target.Top));
            Point bottomRight = toDevice.Transform(new Point(target.Left + target.Width, target.Top + target.Height));
            return new Rect(topLeft, bottomRight);
        }

        private static ImageSource? CaptureCurrentDesktopScreenshot(Rect desktopBoundsPx)
        {
            int width = Math.Max(1, (int)Math.Round(desktopBoundsPx.Width));
            int height = Math.Max(1, (int)Math.Round(desktopBoundsPx.Height));

            try
            {
                using Bitmap bitmap = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap);
                graphics.CopyFromScreen(
                    new DrawingPoint((int)Math.Round(desktopBoundsPx.Left), (int)Math.Round(desktopBoundsPx.Top)),
                    DrawingPoint.Empty,
                    new DrawingSize(width, height),
                    CopyPixelOperation.SourceCopy);

                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    AppLogger.Debug($"Quick layout picker captured current desktop screenshot: boundsPx={DescribeRect(desktopBoundsPx)}");
                    return source;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Quick layout picker failed to capture current desktop screenshot: {ex.Message}");
                return null;
            }
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource != sender) return;
            AppLogger.Debug($"Quick layout picker dismissed by background click: target={DescribeTarget()}, mode={_mode}");
            Close();
        }

        private void WindowLayoutPickerWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                AppLogger.Debug($"Quick layout picker dismissed by Escape: target={DescribeTarget()}, mode={_mode}");
                Close();
                e.Handled = true;
            }
        }

        private string DescribeTarget()
        {
            return _target.NoteData == null ? $"windowTitle='{_target.Title}'" : $"noteId={_target.NoteData.Id}, title='{_target.NoteData.Title}'";
        }

        private static string DescribeRect(Rect rect)
        {
            return $"[{rect.Left:F1},{rect.Top:F1},{rect.Width:F1},{rect.Height:F1}]";
        }

        private static string DescribePoint(Point point)
        {
            return $"({point.X:F1},{point.Y:F1})";
        }

        private static string GetDesktopBadgeText(VirtualDesktopEntry desktop)
        {
            List<string> badges = [];
            if (desktop.IsCurrent)
            {
                badges.Add("current");
            }

            if (desktop.IsWindowDesktop)
            {
                badges.Add("window");
            }

            if (badges.Count == 0)
            {
                badges.Add("virtual desktop");
            }

            return string.Join(" | ", badges);
        }

        private sealed record DesktopMapMetrics(double Width, double Height, Rect DesktopBoundsPx, double Scale)
        {
            public static DesktopMapMetrics Create(Rect desktopBoundsPx, double maxWidth, double maxHeight)
            {
                double desktopWidth = Math.Max(1, desktopBoundsPx.Width);
                double desktopHeight = Math.Max(1, desktopBoundsPx.Height);
                double scale = Math.Min(maxWidth / desktopWidth, maxHeight / desktopHeight);
                return new DesktopMapMetrics(desktopWidth * scale, desktopHeight * scale, desktopBoundsPx, scale);
            }

            public Rect ScreenToCanvas(Rect screenRect)
            {
                Point topLeft = ScreenToCanvas(new Point(screenRect.Left, screenRect.Top));
                Point bottomRight = ScreenToCanvas(new Point(screenRect.Right, screenRect.Bottom));
                return new Rect(topLeft, bottomRight);
            }

            public Point CanvasToScreen(Point canvasPoint)
            {
                double x = canvasPoint.X / Scale + DesktopBoundsPx.Left;
                double y = canvasPoint.Y / Scale + DesktopBoundsPx.Top;
                return new Point(x, y);
            }

            public Rect CanvasToScreen(Rect canvasRect)
            {
                Point topLeft = CanvasToScreen(new Point(canvasRect.Left, canvasRect.Top));
                Point bottomRight = CanvasToScreen(new Point(canvasRect.Right, canvasRect.Bottom));
                return new Rect(topLeft, bottomRight);
            }

            private Point ScreenToCanvas(Point screenPoint)
            {
                return new Point(
                    (screenPoint.X - DesktopBoundsPx.Left) * Scale,
                    (screenPoint.Y - DesktopBoundsPx.Top) * Scale);
            }
        }

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
