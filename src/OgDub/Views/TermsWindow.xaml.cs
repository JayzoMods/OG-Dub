using System.ComponentModel;
using System.Windows;
using OgDub.Services;

namespace OgDub.Views;

public partial class TermsWindow : Window
{
    private readonly bool _requireAccept;

    public TermsWindow(bool requireAccept)
    {
        _requireAccept = requireAccept;
        TermsText = AppHost.Terms.LoadText();
        TermsLoaded = !string.IsNullOrWhiteSpace(TermsText)
            && !TermsText.StartsWith("Terms and conditions could not be loaded", StringComparison.Ordinal);
        DataContext = this;
        InitializeComponent();
    }

    public string TermsText { get; }
    public bool TermsLoaded { get; }
    public bool ShowAccept => _requireAccept && TermsLoaded;
    public bool ShowDecline => _requireAccept;
    public bool ShowClose => !_requireAccept;

    private void Accept_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TermsLoaded)
            return;
        AppHost.Terms.Accept();
        DialogResult = true;
        Close();
    }

    private void Decline_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_requireAccept && DialogResult != true)
            DialogResult = false;
        base.OnClosing(e);
    }
}
