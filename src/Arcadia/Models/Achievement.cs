using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Arcadia.Models;

/// <summary>
/// An official achievement of a game (list read from Steam), ticked by the player when obtained.
/// Used for games that do not come from a store with its own achievements (plain .exe, shortcuts).
/// </summary>
public partial class Achievement : ObservableObject
{
    /// <summary>Steam internal name: stable, used to keep what was ticked when the list is refreshed.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Hidden by the game until obtained: its description is not shown before (spoilers).</summary>
    public bool Hidden { get; set; }
    /// <summary>Share of Steam players who obtained it (0-100).</summary>
    public double? Percent { get; set; }
    public string? Icon { get; set; }
    public string? IconGray { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUnlocked), nameof(IsSecret), nameof(IconUrl), nameof(ShownDescription), nameof(StatusText))]
    private DateTime? _unlockedAt;

    [JsonIgnore] public bool IsUnlocked => UnlockedAt != null;
    [JsonIgnore] public string? IconUrl => IsUnlocked ? Icon : IconGray ?? Icon;
    [JsonIgnore] public bool IsSecret => Hidden && !IsUnlocked;

    [JsonIgnore]
    public string ShownDescription => Hidden && !IsUnlocked
        ? "Succès secret"
        : string.IsNullOrWhiteSpace(Description) ? "" : Description!;

    [JsonIgnore]
    public string StatusText
    {
        get
        {
            var percent = Percent is double p ? $"{p.ToString(p < 10 ? "0.#" : "0", Services.Format.Fr)} % des joueurs" : null;
            if (UnlockedAt is DateTime d) return percent != null ? $"Obtenu le {d.ToString("d MMM yyyy", Services.Format.Fr)}  ·  {percent}" : $"Obtenu le {d.ToString("d MMM yyyy", Services.Format.Fr)}";
            return percent ?? "";
        }
    }
}
