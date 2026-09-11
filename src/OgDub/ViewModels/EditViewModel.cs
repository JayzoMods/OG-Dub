using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgDub.Core;
using OgDub.Services;
using OgDub.Services.LocalAi;

namespace OgDub.ViewModels;

public sealed class EditTargetItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string WavPath { get; init; }
}

public partial class EditViewModel : ObservableObject
{
    private readonly DeckViewModel _deck;
    private readonly AppSettingsStore _settings;
    private readonly LocalRuntimeProbe _probe = new();
    private CancellationTokenSource? _jobCts;
    private bool _opened;
    private bool _loadingSelection;

    public EditViewModel(DeckViewModel deck, AppSettingsStore settings)
    {
        _deck = deck;
        _settings = settings;
        ExtraBaseUrl = settings.Current.ExtraLocalBaseUrl ?? "";
        _deck.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeckViewModel.SelectedCassette)
                || e.PropertyName == nameof(DeckViewModel.Cassettes))
                RefreshTargets();
        };
        RefreshTargets();
    }

    public ObservableCollection<LocalRuntimeItem> Providers { get; } = [];
    public ObservableCollection<string> Models { get; } = [];
    public ObservableCollection<ChatTurn> Messages { get; } = [];
    public ObservableCollection<EditTargetItem> Targets { get; } = [];

    [ObservableProperty] private LocalRuntimeItem? selectedProvider;
    [ObservableProperty] private string? selectedModel;
    [ObservableProperty] private EditTargetItem? selectedTarget;
    [ObservableProperty] private string extraBaseUrl = "";
    [ObservableProperty] private string draft = "";
    [ObservableProperty] private string lcdLine = "NO LOCAL MODEL";
    [ObservableProperty] private string lcdMode = "EDIT";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool probing;
    [ObservableProperty] private bool hasNoProviders = true;

    public string EmptyHint =>
        "Install and start Ollama, LM Studio, or LocalAI on this PC, then punch Refresh. OG Dub only talks to localhost. Record still works with no model.";

    public void DisposeProbe() => _probe.Dispose();

    public void CancelJob()
    {
        _jobCts?.Cancel();
    }

    public async Task OnOpenedAsync()
    {
        if (_opened)
            return;
        _opened = true;
        await RefreshAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    partial void OnDraftChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProviderChanged(LocalRuntimeItem? value)
    {
        if (_loadingSelection)
            return;
        _settings.Current.LastProviderId = value?.Id ?? "";
        _settings.Save();
        FillModels(value);
        UpdateLcd();
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedModelChanged(string? value)
    {
        if (_loadingSelection)
            return;
        _settings.Current.LastModelId = value ?? "";
        _settings.Save();
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnExtraBaseUrlChanged(string value)
    {
        _settings.Current.ExtraLocalBaseUrl = value ?? "";
        _settings.Save();
    }

    private bool CanSend() =>
        !IsBusy
        && !Probing
        && !string.IsNullOrWhiteSpace(Draft)
        && SelectedProvider is not null
        && (SelectedProvider.CanChat || SelectedProvider.CanTransform);

    private bool CanCancel() => IsBusy;

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh() => await RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => CancelJob();

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        var provider = SelectedProvider;
        var model = SelectedModel ?? "";
        var prompt = Draft.Trim();
        if (provider is null || prompt.Length == 0)
            return;

        Draft = "";
        Messages.Add(new ChatTurn { Role = "You", Text = prompt });
        IsBusy = true;
        _jobCts?.Dispose();
        _jobCts = new CancellationTokenSource();
        var cancel = _jobCts.Token;
        try
        {
            if (provider.CanTransform && SelectedTarget is not null && File.Exists(SelectedTarget.WavPath))
            {
                LcdMode = "EDIT";
                LcdLine = "REWRITING TAPE";
                Messages.Add(new ChatTurn
                {
                    Role = "Deck",
                    Text = "Sending the cassette to the local audio runtime. GPU load is that server, not this boombox. Cancel stops the HTTP call."
                });
                var wav = SelectedTarget.WavPath;
                var bytes = await _probe.TransformAsync(provider, string.IsNullOrWhiteSpace(model) ? provider.Models.FirstOrDefault() ?? "" : model, wav, prompt, cancel);
                var title = SelectedTarget.Label;
                var imported = await _deck.ImportEditedCassetteAsync(bytes, title, cancel);
                Messages.Add(new ChatTurn
                {
                    Role = "Deck",
                    Text = imported
                        ? "New cassette in the crate. The original take was not overwritten."
                        : "The rewrite came back but the crate refused it."
                });
                LcdMode = "EJECT";
                LcdLine = "EDIT SAVED";
            }
            else if (provider.CanChat)
            {
                LcdMode = "CHAT";
                LcdLine = "TALKING TO " + provider.Label.ToUpperInvariant();
                var history = Messages
                    .Where(m => m.Role is "You" or "Model")
                    .Select(m => new ChatTurn
                    {
                        Role = string.Equals(m.Role, "You", StringComparison.Ordinal) ? "user" : "assistant",
                        Text = m.Text
                    })
                    .ToList();
                var reply = await _probe.ChatAsync(provider, string.IsNullOrWhiteSpace(model) ? provider.Models.FirstOrDefault() ?? "" : model, history, cancel);
                Messages.Add(new ChatTurn { Role = "Model", Text = reply });
                if (SelectedTarget is not null && !provider.CanTransform)
                {
                    Messages.Add(new ChatTurn
                    {
                        Role = "Deck",
                        Text = "That runtime can chat, but it cannot rewrite the WAV. Start LocalAI (or another local audio server) and punch Refresh."
                    });
                }
            }
            else if (provider.CanTransform)
            {
                Messages.Add(new ChatTurn
                {
                    Role = "Deck",
                    Text = "Pick a cassette in the crate (Side A or the live voice take) before rewriting audio."
                });
            }

            UpdateLcd();
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new ChatTurn { Role = "Deck", Text = "Cancelled." });
            LcdMode = "STOP";
            LcdLine = "CANCELLED";
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatTurn { Role = "Deck", Text = ex.Message });
            LcdMode = "ERR";
            LcdLine = "EDIT FAILED";
        }
        finally
        {
            IsBusy = false;
            _jobCts?.Dispose();
            _jobCts = null;
        }
    }

    private async Task RefreshAsync()
    {
        Probing = true;
        IsBusy = true;
        LcdMode = "SCAN";
        LcdLine = "LOOKING FOR LOCAL MODELS";
        try
        {
            var found = await _probe.ProbeAsync(ExtraBaseUrl, CancellationToken.None);
            _loadingSelection = true;
            Providers.Clear();
            foreach (var item in found)
                Providers.Add(item);

            var keep = _settings.Current.LastProviderId ?? "";
            SelectedProvider = Providers.FirstOrDefault(p => p.Id == keep) ?? Providers.FirstOrDefault();
            FillModels(SelectedProvider);
            _loadingSelection = false;
            HasNoProviders = Providers.Count == 0;
            UpdateLcd();
            if (Providers.Count == 0)
            {
                _deck.Status = "NO LOCAL MODEL. Record still works. Start Ollama or LocalAI on this PC, then Refresh.";
            }
        }
        catch (Exception ex)
        {
            LcdMode = "ERR";
            LcdLine = "PROBE FAILED";
            _deck.Status = ex.Message;
        }
        finally
        {
            _loadingSelection = false;
            Probing = false;
            IsBusy = false;
            SendCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private void FillModels(LocalRuntimeItem? provider)
    {
        Models.Clear();
        if (provider is null)
        {
            SelectedModel = null;
            return;
        }

        foreach (var model in provider.Models)
            Models.Add(model);

        var keep = _settings.Current.LastModelId ?? "";
        SelectedModel = Models.FirstOrDefault(m => string.Equals(m, keep, StringComparison.OrdinalIgnoreCase))
            ?? Models.FirstOrDefault();
    }

    private void RefreshTargets()
    {
        var keep = SelectedTarget?.Id;
        Targets.Clear();
        var cassette = _deck.SelectedCassette;
        if (cassette is null)
        {
            SelectedTarget = null;
            return;
        }

        if (File.Exists(cassette.WavPath))
        {
            Targets.Add(new EditTargetItem
            {
                Id = cassette.Record.Id + "-a",
                Label = cassette.Title + " · Side A",
                WavPath = cassette.WavPath
            });
        }

        if (cassette.HasSideB)
        {
            Targets.Add(new EditTargetItem
            {
                Id = cassette.Record.Id + "-b",
                Label = cassette.Title + " · live voice",
                WavPath = cassette.SideBPath
            });
        }

        SelectedTarget = Targets.FirstOrDefault(t => t.Id == keep) ?? Targets.FirstOrDefault();
    }

    private void UpdateLcd()
    {
        if (Providers.Count == 0)
        {
            LcdMode = "EDIT";
            LcdLine = "NO LOCAL MODEL";
            return;
        }

        var provider = SelectedProvider;
        if (provider is null)
        {
            LcdMode = "EDIT";
            LcdLine = "PICK A RUNTIME";
            return;
        }

        LcdMode = provider.CanTransform ? "EDIT" : "CHAT";
        LcdLine = provider.Label.ToUpperInvariant();
    }
}
