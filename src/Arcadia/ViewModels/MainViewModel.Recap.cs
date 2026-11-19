using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>"Ton année de jeu": opened from the Statistics page, and announced once at the end of each year.</summary>
public partial class MainViewModel
{
    [RelayCommand]
    private void OpenRecap(object? year)
    {
        if (Dialogs.OfType<RecapViewModel>().Any()) return;
        Dialogs.Add(new RecapViewModel(this, year as int?));
        Sound.Play(Services.Sfx.Open);
    }

    /// <summary>First-launch assistant (also offered in the settings).</summary>
    [RelayCommand]
    private void OpenOnboarding()
    {
        if (Dialogs.OfType<OnboardingViewModel>().Any()) return;
        Dialogs.Clear();
        Dialogs.Add(new OnboardingViewModel(this));
    }

    /// <summary>From mid-December to mid-January: "Ton récap 2026 est prêt", once per year.</summary>
    private void AnnounceRecap()
    {
        var today = DateTime.Today;
        int year;
        if (today is { Month: 12, Day: >= 15 }) year = today.Year;
        else if (today is { Month: 1, Day: <= 15 }) year = today.Year - 1;
        else return;
        if (Settings.RecapAnnounced >= year) return;
        if (!Library.Games.Any(g => g.Sessions.Any(s => s.Start.Year == year))) return;
        Settings.RecapAnnounced = year;
        ShowToast($"Ton année de jeu {year} est prête !", ToastKind.Success, "Voir", () => OpenRecap(year), seconds: 14);
    }
}
