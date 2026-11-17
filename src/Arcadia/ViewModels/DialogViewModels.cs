using System.Collections.ObjectModel;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Arcadia.ViewModels;

public enum ToastKind { Info, Success, Warning, Error }

public sealed class ToastMessage
{
    public ToastMessage(string text, ToastKind kind, string? actionText = null, Action? action = null)
    {
        Text = text;
        Kind = kind;
        ActionText = action != null ? actionText : null;
        ActionCommand = new RelayCommand(() => action?.Invoke());
    }

    public string? ActionText { get; }
    public IRelayCommand ActionCommand { get; }

    public string Text { get; }
    public ToastKind Kind { get; }

    public string Glyph => Kind switch
    {
        ToastKind.Success => "",
        ToastKind.Warning => "",
        ToastKind.Error => "",
        _ => "",
    };
}

// ----------------------------------------------------------------------------------------
// Confirmation
// ----------------------------------------------------------------------------------------

public partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ConfirmDialogViewModel(MainViewModel main) => _main = main;

    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string ConfirmText { get; init; } = "Confirmer";
    public string CancelText { get; init; } = "Annuler";
    public bool IsDanger { get; init; }
    public string Glyph { get; init; } = "";
    public Action? OnConfirm { get; init; }

    [RelayCommand]
    private void Confirm()
    {
        _main.CloseDialog(this);
        OnConfirm?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _main.CloseDialog(this);
}

// ----------------------------------------------------------------------------------------
// Edit game
// ----------------------------------------------------------------------------------------

public partial class EditGameViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string? _pendingCoverFile;
    private bool _resetCover;
    private string? _relocatePath;

    public EditGameViewModel(MainViewModel main, Game game)
    {
        _main = main;
        Game = game;
        _name = game.Name;
        _launchTarget = game.LaunchTarget;
        _arguments = game.Arguments ?? "";
        _workingDirectory = game.WorkingDirectory ?? "";
        _developer = game.Developer ?? "";
        _publisher = game.Publisher ?? "";
        _genres = game.Genres ?? "";
        _releaseYear = game.ReleaseYear?.ToString() ?? "";
        _description = game.Description ?? "";
        _coverPreview = game.CoverFullPath;
        _runAsAdmin = game.RunAsAdmin;
        _preLaunchCommand = game.PreLaunchCommand ?? "";
        _postExitCommand = game.PostExitCommand ?? "";
        _windowOnLaunch = game.WindowOnLaunch;
    }

    public Game Game { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUrl))] private string _launchTarget;
    [ObservableProperty] private string _arguments;
    [ObservableProperty] private string _workingDirectory;
    [ObservableProperty] private string _developer;
    [ObservableProperty] private string _publisher;
    [ObservableProperty] private string _genres;
    [ObservableProperty] private string _releaseYear;
    [ObservableProperty] private string _description;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCoverPreview))] private string? _coverPreview;
    [ObservableProperty] private bool _runAsAdmin;
    [ObservableProperty] private string _preLaunchCommand;
    [ObservableProperty] private string _postExitCommand;
    [ObservableProperty] private WindowOnLaunch _windowOnLaunch;
    [ObservableProperty] private string? _error;

    public bool IsUrl => LaunchHelper.IsUri(LaunchTarget);
    public bool HasCoverPreview => !string.IsNullOrEmpty(CoverPreview);
    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand]
    private void BrowseTarget()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir le fichier de lancement",
            Filter = "Jeux et raccourcis (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Tous les fichiers (*.*)|*.*",
            DereferenceLinks = false,
        };
        var current = IsUrl ? null : Path.GetDirectoryName(LaunchTarget);
        if (current != null && Directory.Exists(current)) dlg.InitialDirectory = current;
        if (dlg.ShowDialog() != true) return;

        _relocatePath = dlg.FileName;
        LaunchTarget = dlg.FileName;
        if (dlg.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            WorkingDirectory = Path.GetDirectoryName(dlg.FileName) ?? "";
    }

    [RelayCommand]
    private void BrowseWorkingDirectory()
    {
        var dlg = new OpenFolderDialog { Title = "Dossier de travail" };
        if (Directory.Exists(WorkingDirectory)) dlg.InitialDirectory = WorkingDirectory;
        if (dlg.ShowDialog() == true) WorkingDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private void ImportCover()
    {
        var file = MainViewModel.PickImageFile();
        if (file == null) return;
        _pendingCoverFile = file;
        _resetCover = false;
        CoverPreview = file;
    }

    [RelayCommand]
    private void SearchCover()
    {
        _main.Dialogs.Add(new CoverSearchViewModel(_main, Game, string.IsNullOrWhiteSpace(Name) ? Game.Name : Name, path =>
        {
            _pendingCoverFile = path;
            _resetCover = false;
            CoverPreview = path;
        }));
    }

    [RelayCommand]
    private void ResetCover()
    {
        _pendingCoverFile = null;
        _resetCover = true;
        CoverPreview = null;
    }

    [RelayCommand]
    private async Task Save()
    {
        Error = null;
        if (!CanSave) { Error = "Le nom ne peut pas être vide."; return; }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(ReleaseYear))
        {
            if (!int.TryParse(ReleaseYear.Trim(), out var y) || y < 1950 || y > 2100) { Error = "Année de sortie invalide."; return; }
            year = y;
        }

        try
        {
            var g = Game;
            var newName = Name.Trim();
            if (newName != g.Name) { g.Name = newName; g.NameLocked = true; }

            var targetChanged = !string.Equals(LaunchTarget.Trim(), g.LaunchTarget, StringComparison.OrdinalIgnoreCase);
            if (targetChanged && _relocatePath != null && string.Equals(_relocatePath, LaunchTarget, StringComparison.OrdinalIgnoreCase))
            {
                await _main.Importer.RelocateAsync(g, _relocatePath);
                if (!string.IsNullOrWhiteSpace(Arguments) && g.SourceKind == GameSourceKind.Executable) g.Arguments = Arguments.Trim();
            }
            else
            {
                if (targetChanged) g.LaunchTarget = LaunchTarget.Trim();
                g.Arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim();
                g.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim();
            }

            g.Developer = NullIfEmpty(Developer);
            g.Publisher = NullIfEmpty(Publisher);
            g.Genres = NullIfEmpty(Genres);
            g.ReleaseYear = year;
            g.Description = NullIfEmpty(Description);
            g.RunAsAdmin = RunAsAdmin;
            g.PreLaunchCommand = NullIfEmpty(PreLaunchCommand);
            g.PostExitCommand = NullIfEmpty(PostExitCommand);
            g.WindowOnLaunch = WindowOnLaunch;

            if (_pendingCoverFile != null)
                await _main.Metadata.SetCustomCoverFromFileAsync(g, _pendingCoverFile);
            else if (_resetCover)
            {
                AppPaths.TryDeleteManaged(g.Cover);
                g.Cover = null;
                g.CustomCover = false;
                g.MetadataState = MetadataState.Pending;
                _main.Metadata.Enqueue(g);
            }

            g.IsMissing = !GameLauncher.TargetExists(g);
            _main.CloseDialog(this);
            _main.ShowToast("Modifications enregistrées", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void SetWindowOnLaunch(object? arg)
    {
        if (Enum.TryParse<WindowOnLaunch>(arg?.ToString(), out var w)) WindowOnLaunch = w;
    }

    [RelayCommand]
    private void Cancel() => _main.CloseDialog(this);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ----------------------------------------------------------------------------------------
// Cover search
// ----------------------------------------------------------------------------------------

public partial class CoverSearchViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Action<string>? _onPickedFile;
    private CancellationTokenSource? _cts;

    public CoverSearchViewModel(MainViewModel main, Game game, string query, Action<string>? onPickedFile = null)
    {
        _main = main;
        _onPickedFile = onPickedFile;
        Game = game;
        _query = query;
        _ = SearchAsync();
    }

    public Game Game { get; }
    public ObservableCollection<CoverCandidate> Results { get; } = new();
    public bool ShowUpdateInfo => _onPickedFile == null;

    [ObservableProperty] private string _query;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _updateInfo = true;

    [RelayCommand]
    private Task Search() => SearchAsync();

    private async Task SearchAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var q = Query?.Trim();
        if (string.IsNullOrEmpty(q)) return;

        IsSearching = true;
        Status = null;
        Results.Clear();
        try
        {
            var list = await _main.Metadata.SearchCandidatesAsync(q, cts.Token);
            if (cts.IsCancellationRequested) return;
            foreach (var c in list) Results.Add(c);
            if (list.Count == 0) Status = "Aucun résultat. Essayez un autre nom ou importez votre propre image.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn($"Cover search failed: {ex.Message}");
            Status = "Impossible de joindre les services d'images. Vérifiez votre connexion Internet, ou importez une image depuis votre PC.";
        }
        finally
        {
            if (cts == _cts) IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task Pick(CoverCandidate? candidate)
    {
        if (candidate == null || IsApplying) return;
        IsApplying = true;
        Status = null;
        try
        {
            if (_onPickedFile != null)
            {
                var path = await _main.Metadata.DownloadCandidateToTempAsync(candidate);
                if (path == null) { Status = "Le téléchargement de cette image a échoué. Essayez-en une autre."; return; }
                _onPickedFile(path);
            }
            else
            {
                await _main.Metadata.ApplyCandidateAsync(Game, candidate, UpdateInfo);
                _main.ShowToast($"Jaquette de « {Game.Name} » mise à jour", ToastKind.Success);
            }
            Close();
        }
        catch (Exception ex)
        {
            Status = IsNetwork(ex) ? "Connexion impossible. Réessayez plus tard." : ex.Message;
        }
        finally { IsApplying = false; }
    }

    private static bool IsNetwork(Exception ex) => Services.Online.Http.IsNetworkError(ex);

    [RelayCommand]
    private async Task ImportFile()
    {
        var file = MainViewModel.PickImageFile();
        if (file == null) return;
        try
        {
            if (_onPickedFile != null) _onPickedFile(file);
            else
            {
                await _main.Metadata.SetCustomCoverFromFileAsync(Game, file);
                _main.ShowToast("Image personnalisée appliquée", ToastKind.Success);
            }
            Close();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void Close()
    {
        _cts?.Cancel();
        _main.CloseDialog(this);
    }
}
