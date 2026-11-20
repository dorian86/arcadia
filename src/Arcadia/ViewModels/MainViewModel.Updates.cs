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
                // the patch notes open by themselves, unless the user is busy (another window, TV mode, a game running)
                if (Dialogs.Count == 0 && !IsTvMode && !Library.Games.Any(g => g.IsRunning)) ShowUpdate(latest);
                else ShowToast($"Arcadia {latest.Version} est disponible", ToastKind.Info, "Voir les nouveautés", () => ShowUpdate(latest), seconds: 15);
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

    /// <summary>
    /// First start of a newer version (updated by Arcadia or by hand): shows its patch notes once ("Quoi de neuf").
    /// The version is remembered at every start.
    /// </summary>
    public async Task ShowWhatsNewAsync()
    {
        var current = UpdateService.Current;
        var last = Settings.LastRunVersion;
        if (last != current.ToString())
        {
            Settings.LastRunVersion = current.ToString();
            SaveSettings();
        }
        if (!UpdateService.IsPublishedBuild || last == null || !UpdateService.TryParseVersion(last, out var previous) || previous >= current) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var notes = await UpdateService.GetNotesAsync(previous, current);
            if (string.IsNullOrWhiteSpace(notes) || Dialogs.Count > 0 || IsTvMode) return;
            Dialogs.Add(UpdateDialogViewModel.WhatsNew(this, current, notes));
        }
        catch (Exception ex) { Log.Warn($"Patch notes unavailable: {ex.Message}"); }
    }

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
        _notes = string.IsNullOrWhiteSpace(release.Notes) ? "Pas de notes pour cette version." : release.Notes.Trim();
        _ = LoadSkippedNotesAsync();
    }

    private UpdateDialogViewModel(MainViewModel main, ReleaseInfo release, string notes)
    {
        _main = main;
        Release = release;
        IsWhatsNew = true;
        _notes = notes;
    }

    /// <summary>"Quoi de neuf": the notes of the version just installed, with a single "OK" button.</summary>
    public static UpdateDialogViewModel WhatsNew(MainViewModel main, Version version, string notes) =>
        new(main, new ReleaseInfo(version, "v" + version, "", notes, null, 0, ""), notes);

    public ReleaseInfo Release { get; }
    public bool IsWhatsNew { get; }
    public bool IsOffer => !IsWhatsNew;
    public string Title => IsWhatsNew ? $"Quoi de neuf dans Arcadia {Release.Version}" : $"Arcadia {Release.Version} est disponible";
    public string Subtitle => IsWhatsNew
        ? "Arcadia vient d'être mis à jour. Voici ce qui change."
        : $"Vous utilisez la version {UpdateService.Current}. Votre bibliothèque et vos réglages sont conservés.";
    /// <summary>Patch notes (light Markdown: "#" titles, "-" lists, **bold**).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowOverline))] private string _notes;
    /// <summary>The "NOUVEAUTÉS" label, unless the notes start with their own heading.</summary>
    public bool ShowOverline => !Notes.TrimStart().StartsWith('#');

    /// <summary>Versions published in between (the user skipped some): their notes are shown too.</summary>
    private async Task LoadSkippedNotesAsync()
    {
        try
        {
            var all = await UpdateService.GetNotesAsync(UpdateService.Current, Release.Version);
            if (!string.IsNullOrWhiteSpace(all)) Notes = all;
        }
        catch (Exception ex) { Log.Warn($"Notes of skipped versions unavailable: {ex.Message}"); }
    }
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
