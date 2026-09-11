using System.Windows;
using System.Windows.Input;

namespace OgDub.Views;

public partial class NamePromptWindow : Window
{
    public NamePromptWindow(string current)
    {
        InitializeComponent();
        NameBox.Text = current ?? "";
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string Result => NameBox.Text ?? "";

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NameBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            DialogResult = true;
            Close();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            Close();
        }
    }
}
