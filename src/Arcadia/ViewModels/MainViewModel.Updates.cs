using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>Checks GitHub for a newer Arcadia, then downloads it, swaps the exe and restarts.</summary>
public partial class MainViewModel
{
    public bool UpdatesConfigured => UpdateService.IsConfigured;

    public string UpdateStatus => !UpdateService.IsConfigured
        ? "Mises à jour non configurées : indiquez le dépôt des versions dans Arcadia.csproj (UpdateRepository)."
        : !UpdateService.IsPublishedBuild
            ? $"Version de développement : seul l'exécutable publié se met à jour ({UpdateService.Repository})."
            : $"Les nouvelles versions sont publiées sur github.com/{UpdateService.Repository}.";

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!Settings.AutoCheckUpdates || !UpdateService.IsConfigured || !UpdateService.IsPublishedBuild) return;
        await Task.Delay(TimeSpan.FromSeconds(8));
        await CheckForUpdatesAsync(silent: true);
    }

    [RelayCommand]
    private Task CheckUpdates() => CheckForUpdatesAsync(silent: false);

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (!UpdateService.IsConfigured)
        {
            if (!silent) ShowToast("Les mises à jour ne sont pas encore configurées pour cette version d'Arcadia", ToastKind.Warning);
            return;
        }
        try
        {
            var latest = await UpdateService.GetLatestAsync();
            if (latest == null || !UpdateService.IsNewer(latest))
            {
                if (!silent) ShowToast($"Vous avez la dernière version d'Arcadia ({UpdateService.Current})", ToastKind.Success);
                return;
            }
            Log.Info($"Update available: {latest.Version} (current {UpdateService.Current})");
            if (Environment.GetEnvironmentVariable("ARCADIA_UPDATE_AUTOINSTALL") == "1")
            {
                // test hook: install straight away (end-to-end update test)
                var dialog = new UpdateDialogViewModel(this, latest);
                Dialogs.Add(dialog);
                await dialog.InstallCommand.ExecuteAsync(null);
                return;
            }
            if (silent)
            {
                if (Settings.SkippedUpdate == latest.Tag) return;
                ShowToast($"Arcadia {latest.Version} est disponible", ToastKind.Info, "Voir", () => ShowUpdate(latest), seconds: 12);
            }
            else ShowUpdate(latest);
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            if (!silent) ShowToast("Impossible de vérifier les mises à jour (connexion ?)", ToastKind.Error);
        }
    }

    private void ShowUpdate(ReleaseInfo release)
    {
        if (Dialogs.OfType<UpdateDialogViewModel>().Any()) return;
        Dialogs.Add(new UpdateDialogViewModel(this, release));
    }

    /// <summary>First start after an update.</summary>
    public void AnnounceUpdated() =>
        ShowToast($"Arcadia a été mis à jour en version {UpdateService.Current}", ToastKind.Success, seconds: 8);

    /// <summary>Quits so that the new version (already started) can take over.</summary>
    internal void ShutdownForUpdate()
    {
        Shutdown();
        System.Windows.Application.Current.Shutdown();
    }
}

/// <summary>"Arcadia x.y est disponible": notes, then download with progress and restart.</summary>
public partial class UpdateDialogViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cts;

    public UpdateDialogViewModel(MainViewModel main, ReleaseInfo release)
    {
        _main = main;
        Release = release;
    }

    public ReleaseInfo Release { get; }
    public string Title => $"Arcadia {Release.Version} est disponible";
    public string Subtitle => $"Vous utilisez la version {UpdateService.Current}. Votre bibliothèque et vos réglages sont conservés.";
    public string Notes => string.IsNullOrWhiteSpace(Release.Notes) ? "Pas de notes pour cette version." : Release.Notes.Trim();
    public bool CanInstallHere => UpdateService.IsPublishedBuild && Release.DownloadUrl != null;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isDownloading;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _error;

    public bool IsIdle => !IsDownloading;

    [RelayCommand]
    private async Task Install()
    {
        if (IsDownloading) return;
        if (!CanInstallHere)
        {
            OpenPage();
            return;
        }
        IsDownloading = true;
        Error = null;
        _cts = new CancellationTokenSource();
        try
        {
            var file = await UpdateService.DownloadAsync(Release, new Progress<double>(p => Progress = p), _cts.Token);
            if (UpdateService.InstallAndRestart(file))
            {
                _main.ShutdownForUpdate();
                return;
            }
            Error = "Arcadia ne peut pas se remplacer dans ce dossier. La page de téléchargement va s'ouvrir.";
            OpenPage();
        }
        catch (OperationCanceledException) { /* closed */ }
        catch (Exception ex)
        {
            Log.Warn($"Update download failed: {ex.Message}");
            Error = "Téléchargement impossible : " + ex.Message;
        }
        IsDownloading = false;
    }

    [RelayCommand]
    private void OpenPage()
    {
        var url = string.IsNullOrEmpty(Release.PageUrl) ? UpdateService.ReleasesPage : Release.PageUrl;
        if (url != null) _main.OpenUrlCommand.Execute(url);
    }

    [RelayCommand]
    private void Skip()
    {
        _main.Settings.SkippedUpdate = Release.Tag;
        _main.SaveSettings();
        Close();
    }

    [RelayCommand]
    private void Close()
    {
        _cts?.Cancel();
        _main.CloseDialog(this);
    }
}
