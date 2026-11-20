using System.Collections.ObjectModel;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>One copy of a game found twice (radio button "Garder celui-ci").</summary>
public partial class DuplicateCopy : ObservableObject
{
    public DuplicateCopy(Game game, string group) { Game = game; Group = group; }
    public Game Game { get; }
    public string Group { get; }
    [ObservableProperty] private bool _isKept;

    public string Detail
    {
        get
        {
            var parts = new List<string> { Game.IsNotInstalled ? $"{Game.StoreLabel} (non installé)" : Game.StoreLabel };
            parts.Add(Game.PlayTimeSeconds >= 60 ? Game.PlayTimeText : "jamais joué");
            if (Game.IsMissing) parts.Add("introuvable");
            return string.Join("  ·  ", parts);
        }
    }

    public string Path => Game.LaunchDisplay;
}

public partial class DuplicateGroup : ObservableObject
{
    public DuplicateGroup(List<Game> games)
    {
        var id = Guid.NewGuid().ToString("N");
        Copies = games.Select(g => new DuplicateCopy(g, id)).ToList();
        Copies[0].IsKept = true;
    }

    public List<DuplicateCopy> Copies { get; }
    public string Title => Copies[0].Game.Name;

    /// <summary>Click on a copy: that one is kept.</summary>
    [RelayCommand]
    private void Keep(DuplicateCopy? copy)
    {
        if (copy == null) return;
        foreach (var c in Copies) c.IsKept = c == copy;
    }
    public string CountText => $"{Copies.Count} entrées";
}

/// <summary>"Jeux en double": each group can be merged (keeping the chosen copy) or marked "not duplicates".</summary>
public partial class DuplicatesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public DuplicatesViewModel(MainViewModel main, List<List<Game>> groups)
    {
        _main = main;
        foreach (var g in groups) Groups.Add(new DuplicateGroup(g));
    }

    public ObservableCollection<DuplicateGroup> Groups { get; } = new();
    public string Subtitle => Groups.Count == 1
        ? "1 jeu apparaît plusieurs fois. Choisissez l'entrée à garder : le temps de jeu, les notes et les favoris sont regroupés."
        : $"{Groups.Count} jeux apparaissent plusieurs fois. Choisissez l'entrée à garder : le temps de jeu, les notes et les favoris sont regroupés.";
    public bool IsEmpty => Groups.Count == 0;

    [RelayCommand]
    private void Merge(DuplicateGroup? group)
    {
        if (group == null) return;
        MergeOne(group);
        Done(1);
    }

    [RelayCommand]
    private void MergeAll()
    {
        var n = Groups.Count;
        foreach (var g in Groups.ToList()) MergeOne(g);
        Done(n);
    }

    private void MergeOne(DuplicateGroup group)
    {
        var keep = (group.Copies.FirstOrDefault(c => c.IsKept) ?? group.Copies[0]).Game;
        var others = group.Copies.Select(c => c.Game).Where(g => g != keep).ToList();
        Duplicates.Merge(keep, others);
        foreach (var o in others) _main.RemoveFromLibrary(o);
        Log.Info($"Duplicates merged into '{keep.Name}': {others.Count} entr(y/ies) removed");
        Groups.Remove(group);
    }

    [RelayCommand]
    private void NotDuplicate(DuplicateGroup? group)
    {
        if (group == null) return;
        var games = group.Copies.Select(c => c.Game).ToList();
        for (var i = 0; i < games.Count; i++)
            for (var j = i + 1; j < games.Count; j++)
            {
                var key = Duplicates.PairKey(games[i], games[j]);
                if (!_main.Settings.NotDuplicates.Contains(key)) _main.Settings.NotDuplicates.Add(key);
            }
        _main.SaveSettings();
        Groups.Remove(group);
        _main.Sound.Play(Sfx.Tick);
        Refresh();
        if (Groups.Count == 0) Close();
    }

    private void Done(int merged)
    {
        _main.Library.SaveNow();
        _main.Sound.Play(Sfx.Notify);
        _main.ShowToast(merged == 1 ? "Doublon fusionné" : $"{merged} doublons fusionnés", ToastKind.Success);
        Refresh();
        if (Groups.Count == 0) Close();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Close() => _main.CloseDialog(this);
}
