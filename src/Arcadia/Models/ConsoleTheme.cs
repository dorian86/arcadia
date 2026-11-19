namespace Arcadia.Models;

/// <summary>An interface theme: the default Arcadia interface or a full-screen layout in the spirit of a console menu.</summary>
public sealed record ConsoleTheme(string Key, string Name, string Description, bool IsLight)
{
    public const string Default = "default";

    /// <summary>
    /// The console themes recreate the layout, colours and feel of each menu with Arcadia's own drawings;
    /// no logo, font, music or sound from the consoles is used.
    /// </summary>
    public static readonly ConsoleTheme[] All =
    {
        new(Default, "Arcadia", "L'interface d'origine : barre latérale, accueil et bibliothèque.", false),
        new("wii", "Wii", "Les chaînes sur fond rayé, l'horloge et les boutons ronds.", true),
        new("wiiu", "Wii U", "Une grande bannière et une grille d'icônes lumineuses.", true),
        new("switch", "Switch", "Une rangée de grandes vignettes et des boutons ronds, en sombre.", false),
        new("switch-light", "Switch (clair)", "La même disposition en version blanche.", true),
        new("ps3", "PS3", "La barre de menus horizontale et sa vague animée.", false),
        new("ps4", "PS4", "Une rangée de vignettes sur fond bleu et la zone d'informations.", false),
        new("ps5", "PS5", "Des petites vignettes en haut et le jeu en plein écran.", false),
    };

    public static ConsoleTheme Get(string? key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];

    public bool IsDefault => Key == Default;

    /// <summary>Visual family shared by the game page, notifications and startup screen: "ps", "switch", "wii" or "default".</summary>
    public string Family => Key switch
    {
        "ps3" or "ps4" or "ps5" => "ps",
        "switch" or "switch-light" => "switch",
        "wii" or "wiiu" => "wii",
        _ => "default",
    };
}
