using System.Diagnostics;
using Arcadia.Models;
using Arcadia.Services;
using Arcadia.Services.Online;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>Latest official announcements of a game, shown in its page.</summary>
public partial class MainViewModel
{
    private readonly Dictionary<Game, DateTime> _newsLoadedAt = new();

    /// <summary>Loaded when the page opens, then kept for an hour.</summary>
    private async Task LoadNewsAsync(Game game)
    {
        if (game.NewsAppId is not int appId) return;
        if (_newsLoadedAt.TryGetValue(game, out var at) && DateTime.Now - at < TimeSpan.FromHours(1)) return;
        _newsLoadedAt[game] = DateTime.Now;
        try
        {
            var news = await SteamNews.GetAsync(appId);
            game.News = news.Take(4).ToList();
        }
        catch (Exception ex)
        {
            _newsLoadedAt.Remove(game); // offline: try again next time
            Log.Warn($"News unavailable for '{game.Name}': {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenNews(GameNews? news)
    {
        if (news == null) return;
        try { Process.Start(new ProcessStartInfo(news.Url) { UseShellExecute = true }); }
        catch (Exception ex) { ShowToast("Impossible d'ouvrir l'article : " + ex.Message, ToastKind.Error); }
    }
}
