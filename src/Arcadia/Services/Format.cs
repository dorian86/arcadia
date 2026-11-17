using System.Globalization;

namespace Arcadia.Services;

public static class Format
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Duration(long seconds)
    {
        if (seconds <= 0) return "—";
        if (seconds < 60) return "< 1 min";
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours < 1) return $"{t.Minutes} min";
        return t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes:00}";
    }

    public static string DurationShort(long seconds)
    {
        if (seconds < 3600) return $"{Math.Max(1, seconds / 60)} min";
        return (seconds / 3600.0).ToString("0.#", Fr) + " h";
    }

    public static string Relative(DateTime date)
    {
        var now = DateTime.Now;
        var diff = now - date;
        if (diff.TotalMinutes < 1) return "À l'instant";
        if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes} min";
        if (date.Date == now.Date) return $"Aujourd'hui, {date:HH:mm}";
        if (date.Date == now.Date.AddDays(-1)) return "Hier";
        if (diff.TotalDays < 7) return $"Il y a {(int)Math.Ceiling(diff.TotalDays)} jours";
        return date.ToString("d MMM yyyy", Fr);
    }
}
