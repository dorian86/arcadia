using Arcadia.Models;

namespace Arcadia.Services;

public sealed record EmulatorDefinition(string Name, string Console, string Arguments, string Extensions, params string[] ExeNames);

/// <summary>
/// ROM library: known emulators, detection from the existing library, and ROM folder scanning.
/// Each ROM becomes a game launched with "emulator.exe + arguments".
/// </summary>
public static class EmulatorService
{
    public static readonly EmulatorDefinition[] Catalog =
    {
        new("Yuzu / Eden / Citron", "Nintendo Switch", "-f -g \"{rom}\"", "xci,nsp,nsz,xcz", "yuzu", "suyu", "sudachi", "eden", "citron"),
        new("Ryujinx", "Nintendo Switch", "--fullscreen \"{rom}\"", "xci,nsp,nsz,xcz", "ryujinx"),
        new("Cemu", "Nintendo Wii U", "-f -g \"{rom}\"", "wua,wux,wud,rpx", "cemu"),
        new("Dolphin", "GameCube / Wii", "-b -e \"{rom}\"", "iso,rvz,wbfs,gcm,gcz,ciso,wad", "dolphin"),
        new("PCSX2", "PlayStation 2", "-fullscreen \"{rom}\"", "iso,chd,cso,bin", "pcsx2", "pcsx2-qt", "pcsx2-qtx64", "pcsx2-qtx64-avx2"),
        new("DuckStation", "PlayStation", "-fullscreen \"{rom}\"", "cue,chd,pbp,m3u", "duckstation", "duckstation-qt-x64-releaseltcg"),
        new("PPSSPP", "PlayStation Portable", "\"{rom}\"", "iso,cso,pbp", "ppsspp", "ppssppwindows64"),
        new("Citra / Azahar", "Nintendo 3DS", "\"{rom}\"", "3ds,cia,cci,cxi", "citra", "citra-qt", "lime3ds", "azahar"),
        new("melonDS", "Nintendo DS", "\"{rom}\"", "nds", "melonds"),
        new("mGBA", "Game Boy Advance", "-f \"{rom}\"", "gba,gb,gbc", "mgba"),
        new("Project64", "Nintendo 64", "\"{rom}\"", "z64,n64,v64", "project64"),
        new("Snes9x", "Super Nintendo", "-fullscreen \"{rom}\"", "sfc,smc", "snes9x", "snes9x-x64"),
        new("xemu", "Xbox", "-dvd_path \"{rom}\"", "iso,xiso", "xemu"),
    };

    public static EmulatorDefinition? Match(string exePath) =>
        Catalog.FirstOrDefault(d => d.ExeNames.Contains(Path.GetFileNameWithoutExtension(exePath), StringComparer.OrdinalIgnoreCase));

    public static EmulatorProfile CreateProfile(string exePath, string? romFolder = null)
    {
        var def = Match(exePath);
        return new EmulatorProfile
        {
            Name = def?.Name.Split(" / ")[0] ?? Path.GetFileNameWithoutExtension(exePath),
            Console = def?.Console ?? "Émulateur",
            ExePath = exePath,
            Arguments = def?.Arguments ?? "\"{rom}\"",
            Extensions = def?.Extensions ?? "iso,rom,bin",
            RomFolder = romFolder,
        };
    }

    /// <summary>Emulators found among the games already in the library (e.g. "Cemu.exe - Raccourci").</summary>
    public static List<EmulatorProfile> DetectFromLibrary(IEnumerable<Game> games, IEnumerable<EmulatorProfile> existing)
    {
        var known = new HashSet<string>(existing.Select(e => e.ExePath), StringComparer.OrdinalIgnoreCase);
        var found = new List<EmulatorProfile>();
        foreach (var g in games)
        {
            var exe = g.TargetPath is { Length: > 0 } t ? t : g.LaunchTarget;
            if (LaunchHelper.IsUri(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) continue;
            if (Match(exe) == null || known.Contains(exe)) continue;
            // a ROM in the shortcut arguments tells us where the games are
            var rom = NameCleaner.FindRomInArguments(g.Arguments);
            var folder = rom != null ? Path.GetDirectoryName(rom.Replace('/', '\\')) : null;
            if (folder != null && !Directory.Exists(folder)) folder = null;
            found.Add(CreateProfile(exe, folder));
            known.Add(exe);
        }
        return found;
    }

    public static IEnumerable<string> FindRoms(EmulatorProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.RomFolder) || !Directory.Exists(profile.RomFolder)) yield break;
        var exts = profile.Extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                     .Select(e => "." + e.TrimStart('.').ToLowerInvariant()).ToHashSet();
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
        foreach (var f in Directory.EnumerateFiles(profile.RomFolder, "*", options))
            if (exts.Contains(Path.GetExtension(f).ToLowerInvariant())) yield return f;
    }

    public static string BuildArguments(EmulatorProfile profile, string rom) =>
        profile.Arguments.Contains("{rom}") ? profile.Arguments.Replace("{rom}", rom) : $"{profile.Arguments} \"{rom}\"".Trim();

    /// <summary>True when a game of the library already launches this ROM (manual shortcut or previous scan).</summary>
    public static bool IsRomInLibrary(IEnumerable<Game> games, string rom)
    {
        var a = Normalize(rom);
        return games.Any(g =>
            (g.TargetPath != null && Normalize(g.TargetPath) == a) ||
            (g.Arguments != null && Normalize(g.Arguments).Contains(a, StringComparison.Ordinal)));
    }

    private static string Normalize(string s) => s.Replace('/', '\\').ToLowerInvariant();

    public static Game CreateGame(EmulatorProfile profile, string rom)
    {
        var name = NameCleaner.CleanCore(Path.GetFileNameWithoutExtension(rom));
        return new Game
        {
            Name = name,
            NameHints = new List<string> { name },
            SourceKind = GameSourceKind.Executable,
            SourcePath = rom,
            LaunchTarget = profile.ExePath,
            Arguments = BuildArguments(profile, rom),
            WorkingDirectory = Path.GetDirectoryName(profile.ExePath),
            TargetPath = rom,
            Store = GameStore.Emulator,
            Console = profile.Console,
            AddedAt = DateTime.Now,
            MetadataState = MetadataState.Pending,
        };
    }
}
