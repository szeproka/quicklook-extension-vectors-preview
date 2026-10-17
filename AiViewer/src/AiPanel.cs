using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;

namespace QuickLook.Plugin.AiViewer
{
    /// <summary>
    /// Shows every artboard (PDF page) of an .ai file, stacked, on a background the user can cycle
    /// with the button in the corner or the B key. The choice is remembered.
    /// </summary>
    public sealed class AiPanel : Grid, IDisposable
    {
        private const string SettingsDomain = "QuickLook.Plugin.AiViewer";
        private const double PageMargin = 24;

        private enum Backdrop { DarkChecker, LightChecker, Black, Gray, White }

        private static readonly string[] BackdropNames =
            { "Dark checker", "Light checker", "Black", "Gray", "White" };

        private readonly string _path;
        private readonly ContextObject _context;
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _pages;
        private readonly TextBlock _buttonText;
        private Backdrop _backdrop;
        private Window _window;
        private volatile bool _disposed;

        public AiPanel(string path, ContextObject context)
        {
            _path = path;
            _context = context;
            _backdrop = LoadBackdrop();

            _pages = new StackPanel { Margin = new Thickness(0, PageMargin / 2, 0, PageMargin / 2) };
            _scroll = new ScrollViewer
            {
                Content = _pages,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false
            };
            _scroll.SizeChanged += (s, e) => FitPagesToViewport();
            _scroll.ScrollChanged += (s, e) =>
            {
                if (e.ViewportHeightChange != 0)
                    FitPagesToViewport();
            };
            Children.Add(_scroll);

            _buttonText = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
            var button = new Border
            {
                Child = _buttonText,
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x20, 0x20, 0x20)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.Hand,
                ToolTip = "Change the background (B)"
            };
            button.MouseLeftButtonUp += (s, e) => CycleBackdrop();
            Children.Add(button);

            Loaded += OnLoaded;
            ApplyBackdrop();
        }

        /// <summary>Renders the artboards one by one off the UI thread, adding each as it is ready.</summary>
        public void StartRendering()
        {
            Task.Run(() =>
            {
                try
                {
                    using (var doc = new AiDocument(_path))
                    {
                        var count = doc.PageCount;
                        var maxSide = count > 4 ? 1600 : 2560;

                        for (var i = 0; i < count && !_disposed; i++)
                        {
                            var bitmap = doc.Render(i, maxSide);
                            var index = i;
                            Dispatcher.BeginInvoke(new Action(() => AddPage(bitmap, index, count)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() => ShowError(ex)));
                }
            });
        }

        private void AddPage(ImageSource bitmap, int index, int count)
        {
            if (_disposed)
                return;

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            // The border hugs the image, so it shows the artboard's edges.
            var frame = new Border
            {
                Child = image,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(PageMargin, PageMargin / 2, PageMargin, 0)
            };

            var item = new StackPanel { Margin = new Thickness(0, 0, 0, PageMargin / 2) };
            item.Children.Add(frame);

            if (count > 1)
                item.Children.Add(new TextBlock
                {
                    Text = $"Artboard {index + 1} of {count}",
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });

            _pages.Children.Add(item);
            ApplyBackdrop();
            FitPagesToViewport();

            if (index == 0)
                _context.IsBusy = false;
        }

        private void ShowError(Exception ex)
        {
            if (_disposed)
                return;

            _pages.Children.Add(new TextBlock
            {
                Text = "Could not render this .ai file.\n\n" + ex.Message,
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(PageMargin)
            });
            _context.IsBusy = false;
        }

        /// <summary>Each artboard fits the window's height; the width is limited by the StackPanel.</summary>
        private void FitPagesToViewport()
        {
            // Before the first layout the viewport height is unknown: leave the pages unconstrained.
            var height = _scroll.ViewportHeight > PageMargin * 4
                ? _scroll.ViewportHeight - PageMargin * 2 - (_pages.Children.Count > 1 ? 24 : 0) // room for the label
                : double.PositiveInfinity;
            foreach (var child in _pages.Children)
                if (child is StackPanel item && item.Children[0] is Border frame && frame.Child is Image image)
                    image.MaxHeight = height;
        }

        private void CycleBackdrop()
        {
            _backdrop = (Backdrop)(((int)_backdrop + 1) % BackdropNames.Length);
            ApplyBackdrop();

            try { SettingHelper.Set("Background", (int)_backdrop, SettingsDomain); }
            catch { /* not being able to remember the choice is fine */ }
        }

        private static Backdrop LoadBackdrop()
        {
            try
            {
                var value = SettingHelper.Get("Background", 0, SettingsDomain);
                return value >= 0 && value < BackdropNames.Length ? (Backdrop)value : Backdrop.DarkChecker;
            }
            catch
            {
                return Backdrop.DarkChecker;
            }
        }

        private void ApplyBackdrop()
        {
            bool dark;
            switch (_backdrop)
            {
                case Backdrop.DarkChecker:
                    Background = Checker(Color.FromRgb(0x2A, 0x2A, 0x2A), Color.FromRgb(0x38, 0x38, 0x38));
                    dark = true;
                    break;
                case Backdrop.LightChecker:
                    Background = Checker(Colors.White, Color.FromRgb(0xCC, 0xCC, 0xCC));
                    dark = false;
                    break;
                case Backdrop.Black:
                    Background = Brushes.Black;
                    dark = true;
                    break;
                case Backdrop.Gray:
                    Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
                    dark = true;
                    break;
                default:
                    Background = Brushes.White;
                    dark = false;
                    break;
            }

            var outline = new SolidColorBrush(dark ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x50, 0, 0, 0));
            var label = new SolidColorBrush(dark ? Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB0, 0, 0, 0));

            foreach (var child in _pages.Children)
                if (child is StackPanel item)
                    foreach (var part in item.Children)
                    {
                        if (part is Border frame) frame.BorderBrush = outline;
                        if (part is TextBlock text) text.Foreground = label;
                    }

            _buttonText.Text = "Background: " + BackdropNames[(int)_backdrop];
        }

        private static Brush Checker(Color a, Color b)
        {
            const double size = 8;
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, size * 2, size * 2))));
            var squares = new GeometryGroup();
            squares.Children.Add(new RectangleGeometry(new Rect(0, 0, size, size)));
            squares.Children.Add(new RectangleGeometry(new Rect(size, size, size, size)));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, squares));

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, size * 2, size * 2),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // The QuickLook window, not this panel, usually has keyboard focus.
            _window = Window.GetWindow(this);
            if (_window != null)
                _window.PreviewKeyDown += OnWindowKeyDown;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.None && IsVisible)
            {
                CycleBackdrop();
                e.Handled = true;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            if (_window != null)
                _window.PreviewKeyDown -= OnWindowKeyDown;
            _window = null;
            _pages.Children.Clear();
        }
    }
}
