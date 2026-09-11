using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OgDub.ViewModels;

namespace OgDub;

public partial class MainWindow : Window
{
    private const double ResizeEdge = 10;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Rect _restoreBounds;
    private bool _filledToWorkArea;
    private GlobalHotkeys? _hotkeys;
    private int _howToIndex = -1;
    private Point _dragStart;
    private CassetteItem? _dragItem;
    private ResizeDir _resizeDir;
    private bool _resizing;
    private Point _resizeOrigin;
    private Rect _resizeStart;

    [Flags]
    private enum ResizeDir
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = AppHost.Deck;
        Loaded += OnLoaded;
        Closed += OnClosed;
        SizeChanged += (_, _) =>
        {
            if (_howToIndex >= 0)
                ShowHowToStep();
        };
        AppHost.Deck.PropertyChanged += DeckOnPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!AppHost.Terms.EnsureAccepted(this))
            {
                Application.Current.Shutdown();
                return;
            }

            _tick.Tick += (_, _) => AppHost.Deck.Tick();
            _tick.Start();
            ApplyHotkeys(AppHost.Deck.GlobalHotkeys);
            if (string.Equals(AppHost.Settings.Current.LastTab, "edit", StringComparison.OrdinalIgnoreCase))
                AppHost.Deck.ShowEditTab = true;
        }
        catch (Exception ex)
        {
            App.WriteCrash(ex);
            Application.Current.Shutdown();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AppHost.Deck.PropertyChanged -= DeckOnPropertyChanged;
        _tick.Stop();
        _hotkeys?.Dispose();
        AppHost.Deck.Shutdown();
    }

    private void DeckOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeckViewModel.GlobalHotkeys))
            ApplyHotkeys(AppHost.Deck.GlobalHotkeys);
    }

    private void ApplyHotkeys(bool enable)
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
        if (!enable)
            return;

        _hotkeys = new GlobalHotkeys(
            this,
            () => Dispatcher.Invoke(() =>
            {
                if (AppHost.Deck.RecCommand.CanExecute(null))
                    AppHost.Deck.RecCommand.Execute(null);
            }),
            () => Dispatcher.Invoke(() =>
            {
                if (AppHost.Deck.StopCommand.CanExecute(null))
                    AppHost.Deck.StopCommand.Execute(null);
            }));

        if (!_hotkeys.TryRegister())
        {
            AppHost.Deck.Status = "Ctrl+Alt+R / Ctrl+Alt+S are in use. Untick Hotkeys or free those shortcuts.";
            _hotkeys.Dispose();
            _hotkeys = null;
        }
    }

    private void Chrome_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (_filledToWorkArea || _resizing)
            return;
        if (e.Handled)
            return;
        if (HowToOverlay.Visibility == Visibility.Visible)
            return;
        if (HitResizeDir(e.GetPosition(this)) != ResizeDir.None)
            return;
        DragMove();
    }

    private void Window_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_filledToWorkArea)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (_resizing)
        {
            ApplyResize(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        Cursor = CursorForDir(HitResizeDir(e.GetPosition(this)));
    }

    private void Window_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_filledToWorkArea || e.ChangedButton != MouseButton.Left)
            return;

        var dir = HitResizeDir(e.GetPosition(this));
        if (dir == ResizeDir.None)
            return;

        _resizeDir = dir;
        _resizing = true;
        _resizeOrigin = PointToScreen(e.GetPosition(this));
        _resizeStart = new Rect(Left, Top, Width, Height);
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing)
            return;

        EndResize();
        e.Handled = true;
    }

    private void Window_OnLostMouseCapture(object sender, MouseEventArgs e) => EndResize();

    private void EndResize()
    {
        if (!_resizing)
            return;

        _resizing = false;
        _resizeDir = ResizeDir.None;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private ResizeDir HitResizeDir(Point pos)
    {
        var dir = ResizeDir.None;
        if (pos.X <= ResizeEdge)
            dir |= ResizeDir.Left;
        else if (pos.X >= ActualWidth - ResizeEdge)
            dir |= ResizeDir.Right;

        if (pos.Y <= ResizeEdge)
            dir |= ResizeDir.Top;
        else if (pos.Y >= ActualHeight - ResizeEdge)
            dir |= ResizeDir.Bottom;

        return dir;
    }

    private static Cursor CursorForDir(ResizeDir dir) => dir switch
    {
        ResizeDir.Left or ResizeDir.Right => Cursors.SizeWE,
        ResizeDir.Top or ResizeDir.Bottom => Cursors.SizeNS,
        ResizeDir.Left | ResizeDir.Top or ResizeDir.Right | ResizeDir.Bottom => Cursors.SizeNWSE,
        ResizeDir.Right | ResizeDir.Top or ResizeDir.Left | ResizeDir.Bottom => Cursors.SizeNESW,
        _ => Cursors.Arrow
    };

    private void ApplyResize(Point local)
    {
        var screen = PointToScreen(local);
        var dx = screen.X - _resizeOrigin.X;
        var dy = screen.Y - _resizeOrigin.Y;
        var left = _resizeStart.Left;
        var top = _resizeStart.Top;
        var width = _resizeStart.Width;
        var height = _resizeStart.Height;

        if (_resizeDir.HasFlag(ResizeDir.Left))
        {
            var next = _resizeStart.Width - dx;
            if (next >= MinWidth)
            {
                left = _resizeStart.Left + dx;
                width = next;
            }
        }
        else if (_resizeDir.HasFlag(ResizeDir.Right))
        {
            width = Math.Max(MinWidth, _resizeStart.Width + dx);
        }

        if (_resizeDir.HasFlag(ResizeDir.Top))
        {
            var next = _resizeStart.Height - dy;
            if (next >= MinHeight)
            {
                top = _resizeStart.Top + dy;
                height = next;
            }
        }
        else if (_resizeDir.HasFlag(ResizeDir.Bottom))
        {
            height = Math.Max(MinHeight, _resizeStart.Height + dy);
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    private void CrateLabel_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            e.Handled = true;
    }

    private void Maximize_OnClick(object sender, RoutedEventArgs e)
    {
        if (_filledToWorkArea)
        {
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _filledToWorkArea = false;
            ChromeBorder.CornerRadius = new CornerRadius(22);
            MaximizeButton.Content = "□";
            MaximizeButton.ToolTip = "Maximise";
            return;
        }

        _restoreBounds = new Rect(Left, Top, Width, Height);
        var work = SystemParameters.WorkArea;
        Left = work.Left;
        Top = work.Top;
        Width = work.Width;
        Height = work.Height;
        _filledToWorkArea = true;
        ChromeBorder.CornerRadius = new CornerRadius(0);
        MaximizeButton.Content = "❐";
        MaximizeButton.ToolTip = "Restore";
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CrateLabel_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenFolder(AppHost.Crate.LibraryRoot);
    }

    private void FavLabel_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenFolder(AppHost.Crate.FavoritesRoot);
    }

    private void FolderLabel_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose crate folder",
            InitialDirectory = AppHost.Crate.LibraryRoot
        };
        if (dlg.ShowDialog(this) != true)
            return;
        if (!AppHost.TryChangeLibrary(dlg.FolderName, out var error))
            AppHost.Deck.Status = string.IsNullOrWhiteSpace(error) ? "That folder was refused." : error;
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = path,
            UseShellExecute = true
        });
    }

    private void Terms_OnClick(object sender, RoutedEventArgs e)
    {
        var resume = HowToOverlay.Visibility == Visibility.Visible;
        HowToOverlay.Visibility = Visibility.Collapsed;
        AppHost.Terms.Show(this);
        if (resume)
        {
            HowToOverlay.Visibility = Visibility.Visible;
            ShowHowToStep();
        }
    }

    private void HowTo_OnClick(object sender, RoutedEventArgs e)
    {
        _howToIndex = 0;
        HowToOverlay.Visibility = Visibility.Visible;
        ShowHowToStep();
    }

    private void HowToBack_OnClick(object sender, RoutedEventArgs e)
    {
        if (_howToIndex > 0)
            _howToIndex--;
        ShowHowToStep();
    }

    private void HowToNext_OnClick(object sender, RoutedEventArgs e)
    {
        if (_howToIndex < HowToTour.Steps.Count - 1)
        {
            _howToIndex++;
            ShowHowToStep();
            return;
        }

        CloseHowTo();
    }

    private void HowToDone_OnClick(object sender, RoutedEventArgs e) => CloseHowTo();

    private void CloseHowTo()
    {
        _howToIndex = -1;
        HowToOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || HowToOverlay.Visibility != Visibility.Visible)
            return;
        CloseHowTo();
        e.Handled = true;
    }

    private void ShowHowToStep()
    {
        if (_howToIndex < 0 || _howToIndex >= HowToTour.Steps.Count)
            return;

        var step = HowToTour.Steps[_howToIndex];
        if (step.TargetName is "EditTab" or "EditPanelHost")
            AppHost.Deck.ShowEditTab = true;
        else
            AppHost.Deck.ShowEditTab = false;

        HowToTitle.Text = step.Title;
        HowToBody.Text = step.Body;
        HowToIndex.Text = (_howToIndex + 1) + " / " + HowToTour.Steps.Count;
        HowToOverlay.UpdateLayout();
        ChromeGrid.UpdateLayout();

        var target = FindName(step.TargetName) as FrameworkElement;
        if (target is null)
            return;

        target.UpdateLayout();
        var origin = target.TransformToAncestor(ChromeGrid).Transform(new Point(0, 0));
        var width = Math.Max(8, target.ActualWidth);
        var height = Math.Max(8, target.ActualHeight);
        Canvas.SetLeft(HowToSpot, origin.X - 6);
        Canvas.SetTop(HowToSpot, origin.Y - 6);
        HowToSpot.Width = width + 12;
        HowToSpot.Height = height + 12;

        var overlay = new Rect(0, 0, Math.Max(1, ChromeGrid.ActualWidth), Math.Max(1, ChromeGrid.ActualHeight));
        var hole = new Rect(origin.X - 6, origin.Y - 6, width + 12, height + 12);
        HowToDimmer.Data = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(overlay),
            new RectangleGeometry(hole, 8, 8));
    }

    private void Stations_OnDropDownOpened(object sender, EventArgs e) => AppHost.Deck.StationsMenuOpen = true;

    private void Stations_OnDropDownClosed(object sender, EventArgs e) => AppHost.Deck.StationsMenuOpen = false;

    private void Mics_OnDropDownOpened(object sender, EventArgs e) => AppHost.Deck.RefreshMicDevices();

    private void CrateList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (AppHost.Deck.PlayCommand.CanExecute(null))
            AppHost.Deck.PlayCommand.Execute(null);
    }

    private void Cassette_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            _dragItem = null;
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragItem = (sender as FrameworkElement)?.DataContext as CassetteItem;
    }

    private void Cassette_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (!File.Exists(_dragItem.WavPath))
            return;

        var data = new DataObject(DataFormats.FileDrop, new[] { _dragItem.WavPath });
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        _dragItem = null;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T hit)
                return hit;
            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
