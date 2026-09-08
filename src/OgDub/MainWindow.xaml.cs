using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OgDub;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = AppHost.Deck;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _tick.Stop();
            AppHost.Deck.Shutdown();
        };
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
        }
        catch (Exception ex)
        {
            App.WriteCrash(ex);
            Application.Current.Shutdown();
        }
    }

    private void Chrome_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
