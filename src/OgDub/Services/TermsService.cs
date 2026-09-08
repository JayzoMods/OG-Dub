using System.IO;
using System.Reflection;
using System.Windows;
using OgDub.Core;
using OgDub.Views;

namespace OgDub.Services;

public sealed class TermsService
{
    public const string CurrentVersion = ProductInfo.TermsVersion;

    private readonly AppSettingsStore _settings;

    public TermsService(AppSettingsStore settings)
    {
        _settings = settings;
    }

    public bool HasAcceptedCurrent =>
        string.Equals(_settings.Current.TermsAcceptedVersion, CurrentVersion, StringComparison.Ordinal);

    public string LoadText()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("TermsOfUse.txt", StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return "Terms and conditions could not be loaded.";

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null)
            return "Terms and conditions could not be loaded.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public bool EnsureAccepted(Window owner)
    {
        if (HasAcceptedCurrent)
            return true;

        var window = new TermsWindow(requireAccept: true) { Owner = owner };
        var ok = window.ShowDialog() == true;
        return ok && HasAcceptedCurrent;
    }

    public void Show(Window owner)
    {
        var window = new TermsWindow(requireAccept: !HasAcceptedCurrent) { Owner = owner };
        window.ShowDialog();
    }

    public void Accept()
    {
        _settings.Current.TermsAcceptedVersion = CurrentVersion;
        _settings.Save();
    }
}
