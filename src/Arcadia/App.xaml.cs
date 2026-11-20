using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Arcadia.Services;
using Arcadia.ViewModels;

namespace Arcadia;

/// <summary>
/// Command line (mostly for testing / portable use):
///   --data &lt;dir&gt;            use another data folder (also: ARCADIA_DATA_DIR)
///   --import &lt;path&gt;         import a file or folder at startup (repeatable)
///   --snapshot &lt;png&gt;        render the window to a PNG and exit (developer tool)
///   --snapshot-delay &lt;ms&gt;   delay before the snapshot (default 2500)
///   --section &lt;name&gt;       home | all | favorites | recent | settings
///   --detail &lt;index&gt;       open the detail page of the n-th visible game
///   --dialog edit|cover|confirm   open a dialog on the first game
///   --wait-metadata         wait for artwork downloads before the snapshot
///   --size WxH              window size
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private MainViewModel? _vm;

    private sealed class Options
    {
        public string? DataDir;
        public readonly List<string> Imports = new();
        public string? Snapshot;
        public int SnapshotDelay = 2500;
        public string? Section;
        public int? Detail;
        public string? DetailName;
        public string? Wallpaper;
        public bool Boot;
        public string? Dialog;
        public bool WaitMetadata;
        public string? Play;
        public bool Perf;
        public bool Hover;
        public string? Theme;
        public string? MakeDemo;
        public int? UpdatedFrom;
        public string? Keys;
        public string? Mouse;
        public int Scroll;
        public int Select;
        public bool Tray;
        public bool Uninstall;
        public bool Tv;
        public bool ForceTray;
        public Size? Size;

        public bool IsDeveloperRun => Snapshot != null || DataDir != null;

        public static Options Parse(string[] args)
        {
            var o = new Options { DataDir = Environment.GetEnvironmentVariable("ARCADIA_DATA_DIR") };
            for (var i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : "";
                switch (args[i].ToLowerInvariant())
                {
                    case "--data": o.DataDir = Next(); break;
                    case "--import": o.Imports.Add(Next()); break;
                    case "--snapshot": o.Snapshot = Next(); break;
                    case "--snapshot-delay": int.TryParse(Next(), out o.SnapshotDelay); break;
                    case "--section": o.Section = Next(); break;
                    case "--detail": o.Detail = int.TryParse(Next(), out var d) ? d : 0; break;
                    case "--detail-name": o.DetailName = Next(); break;
                    case "--wallpaper": o.Wallpaper = Next(); break;
                    case "--boot": o.Boot = true; break; // startup screen of the theme in a developer run
                    case "--dialog": o.Dialog = Next(); break;
                    case "--wait-metadata": o.WaitMetadata = true; break;
                    case "--play": o.Play = Next(); break;
                    case "--perf": o.Perf = true; break;
                    case "--hover": o.Hover = true; break;
                    case "--theme": o.Theme = Next(); break;
                    case "--make-demo": o.MakeDemo = Next(); break;
                    case "--updated": o.UpdatedFrom = int.TryParse(Next(), out var pid) ? pid : 0; break;
                    case "--keys": o.Keys = Next(); break;
                    case "--mouse": o.Mouse = Next(); break;
                    case "--scroll": int.TryParse(Next(), out o.Scroll); break;
                    case "--select": int.TryParse(Next(), out o.Select); break;
                    case "--tray": o.Tray = true; break;
                    case "--uninstall": o.Uninstall = true; break;
                    case "--tv": o.Tv = true; break;
                    case "--force-tray": o.ForceTray = true; break;
                    case "--size":
                        var parts = Next().Split('x');
                        if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h)) o.Size = new Size(w, h);
                        break;
                    default:
                        // Files dropped on the exe / "Open with Arcadia"
                        if (File.Exists(args[i]) || Directory.Exists(args[i])) o.Imports.Add(args[i]);
                        break;
                }
            }
            return o;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = Options.Parse(e.Args);

        AppPaths.Initialize(options.DataDir);
        Log.Init();
        Log.Info($"Arcadia starting (data: {AppPaths.DataDir}, render tier {RenderCapability.Tier >> 16})");

        if (options.Uninstall)
        {
            RunUninstall();
            return;
        }

        if (options.MakeDemo != null)
        {
            // dev tool: build a library of fictional games for public screenshots, then quit
            DemoLibrary.Build(options.MakeDemo);
            Shutdown();
            return;
        }

        // started by an update: let the previous version quit (it holds the single-instance lock), then clean up
        if (options.UpdatedFrom is int previous) UpdateService.FinishUpdate(previous);
        else UpdateService.DeleteLeftover();

        if (!options.IsDeveloperRun && !AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
            _vm?.ShowToast("Une erreur inattendue s'est produite : " + args.Exception.Message, ToastKind.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Fatal exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        ShadowFactory.Register(Resources);
        var firstRun = !File.Exists(AppPaths.SettingsFile) && !File.Exists(AppPaths.SettingsFile + ".bak");
        var settings = new SettingsService();
        // libraries created before the assistant existed: never show it to them
        if (!firstRun && !settings.Settings.OnboardingDone) settings.Settings.OnboardingDone = true;
        ThemeService.Apply(Resources, settings.Settings.AccentPreset);
        if (!options.IsDeveloperRun && settings.Settings.StartWithWindows != InstallService.StartsWithWindows)
            InstallService.SetStartWithWindows(settings.Settings.StartWithWindows);

        _vm = new MainViewModel(settings);
        _vm.AccentChanged += name => ThemeService.Apply(Resources, name);
        _vm.Sound.Initialize();
        _vm.Initialize();
        if (options.UpdatedFrom != null) _vm.AnnounceUpdated();
        if (!options.IsDeveloperRun) _ = _vm.ShowWhatsNewAsync();

        var window = new MainWindow(_vm);
        if (options.Size is Size size)
        {
            window.WindowState = WindowState.Normal;
            window.Width = size.Width;
            window.Height = size.Height;
        }
        MainWindow = window;
        _vm.RestartInstalledRequested += () => RestartInstalled(window);
        if (!options.IsDeveloperRun || options.ForceTray) SetupTray(window, settings.Settings);
        if (!(options.Tray && settings.Settings.ShowTrayIcon))
        {
            window.Show();
            // a console theme has its own startup screen (and sound); the Arcadia interface its startup sound
            if (options.Boot || !options.IsDeveloperRun && options.UpdatedFrom == null)
            {
                if (!window.PlayBootScreen() && !options.IsDeveloperRun) _vm.Sound.PlayStartup();
            }
            if (!settings.Settings.OnboardingDone && options.Snapshot == null) _vm.OpenOnboardingCommand.Execute(null);
        }
        if (options.Tv) _vm.EnterTvModeCommand.Execute(null);

        if (options.Imports.Count > 0) await _vm.ImportPathsAsync(options.Imports);
        if (options.Wallpaper != null) await _vm.SetWallpaperAsync(options.Wallpaper); // same path as "Choisir une image…"
        if (options.Snapshot != null) await RunSnapshotAsync(window, _vm, options);
    }

    private TrayService? _tray;

    private void SetupTray(MainWindow window, Models.AppSettings settings)
    {
        _tray = new TrayService(() => _vm!.Library.Games.Where(g => g.LastPlayedAt != null && g.IsShown)
                                                        .OrderByDescending(g => g.LastPlayedAt));
        _tray.ShowRequested += window.ShowFromBackground;
        _tray.QuitRequested += () =>
        {
            window.AllowClose = true;
            window.Close();
        };
        _tray.TvModeRequested += () =>
        {
            window.ShowFromBackground();
            _vm!.EnterTvModeCommand.Execute(null);
        };
        _tray.PlayRequested += g => _vm!.Play(g);
        _tray.SetVisible(settings.ShowTrayIcon);
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Models.AppSettings.ShowTrayIcon)) _tray.SetVisible(settings.ShowTrayIcon);
        };
    }

    private void RestartInstalled(MainWindow window)
    {
        try
        {
            try { _mutex?.ReleaseMutex(); } catch { /* not owner */ }
            _mutex?.Dispose();
            _mutex = null;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(InstallService.InstalledExe) { UseShellExecute = true });
            window.AllowClose = true;
            window.Close();
        }
        catch (Exception ex) { Log.Error("Restart from installed copy failed", ex); }
    }

    private void RunUninstall()
    {
        var answer = MessageBox.Show("Désinstaller Arcadia de ce PC ?\n\nVos jeux ne seront ni modifiés ni supprimés.",
            "Désinstaller Arcadia", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) { Shutdown(); return; }
        var data = MessageBox.Show("Supprimer aussi votre bibliothèque Arcadia (liste des jeux, jaquettes en cache, préférences) ?\n\n" +
                                   "Choisissez « Non » pour la conserver en cas de réinstallation.",
            "Désinstaller Arcadia", MessageBoxButton.YesNo, MessageBoxImage.Question);
        InstallService.Uninstall(data == MessageBoxResult.Yes, AppPaths.DataDir);
        MessageBox.Show("Arcadia a été désinstallé.", "Arcadia", MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown();
    }

    private bool AcquireSingleInstance()
    {
        var user = Environment.UserName;
        _mutex = new Mutex(true, $"Arcadia.Launcher.{user}", out var created);
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"Arcadia.Launcher.Activate.{user}");
        if (!created)
        {
            _activateEvent.Set();   // ask the running instance to show itself
            return false;
        }

        var thread = new Thread(() =>
        {
            while (_activateEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow w) w.ShowFromBackground();
                });
            }
        }) { IsBackground = true, Name = "Arcadia activation listener" };
        thread.Start();
        return true;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in FindAll<T>(child)) yield return d;
        }
    }

    /// <summary>Dev check (--hover): draws every card in its hovered look (zoom + accent ring) to spot clipping.</summary>
    private static void ShowHoverState(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { IsVisible: false }) continue; // pages kept alive in the background
            if (child is System.Windows.Controls.Button { Template: { } t } b && t.FindName("CoverHost", b) is UIElement host
                && t.FindName("AccentRing", b) is UIElement ring)
            {
                Arcadia.Controls.HoverZoom.Apply(b);
                host.RenderTransform = new System.Windows.Media.ScaleTransform(1.05, 1.05);
                ring.Opacity = 1;                continue;
            }
            ShowHoverState(child);
        }
    }

    private static async Task RunSnapshotAsync(MainWindow window, MainViewModel vm, Options o)
    {
        if (o.WaitMetadata)
        {
            var deadline = DateTime.Now.AddMinutes(4);
            await Task.Delay(1500);
            while (vm.Metadata.IsBusy && DateTime.Now < deadline) await Task.Delay(500);
        }

        if (o.Play != null)
        {
            var target = vm.Library.Games.FirstOrDefault(g => g.Name.Contains(o.Play, StringComparison.OrdinalIgnoreCase));
            if (target != null) vm.PlayCommand.Execute(target);
        }

        if (o.Theme != null)
            foreach (var theme in o.Theme.Split('+')) { vm.ApplyUiTheme(theme); await Task.Delay(700); } // "switch+ps5": several themes in a row
        if (o.Section != null && Enum.TryParse<Section>(o.Section, true, out var section)) vm.Section = section;
        await Task.Delay(400);
        if (o.Detail is int index && vm.VisibleGames.Count > 0)
            vm.DetailGame = (vm.Section == Section.Home ? vm.RecentlyAdded : vm.VisibleGames)[Math.Clamp(index, 0, vm.VisibleGames.Count - 1)];
        if (o.DetailName != null)
            vm.DetailGame = vm.Library.Games.FirstOrDefault(g => g.Name.Contains(o.DetailName, StringComparison.OrdinalIgnoreCase)) ?? vm.DetailGame;
        if (o.Dialog?.StartsWith("onboarding") == true)
        {
            // shown on an empty library too; "onboarding2" opens it on the 3rd step (layout check)
            vm.OpenOnboardingCommand.Execute(null);
            if (int.TryParse(o.Dialog["onboarding".Length..], out var step) && vm.Dialogs.OfType<OnboardingViewModel>().FirstOrDefault() is { } onb) onb.Step = step;
        }
        // --dialog whatsnew: the "Quoi de neuf" window with sample notes (layout check)
        if (o.Dialog == "whatsnew")
            vm.Dialogs.Add(UpdateDialogViewModel.WhatsNew(vm, UpdateService.Current,
                Environment.GetEnvironmentVariable("ARCADIA_NOTES_FILE") is { } notesFile && File.Exists(notesFile) ? File.ReadAllText(notesFile)
                : "# Arcadia 1.1.7\n\n## Nouveautés\n\n- **Barre de sélection** sur l'accueil\n- Filtre **Jeux introuvables**\n  - avec le nombre de jeux\n\n"
                  + "# Arcadia 1.1.6\n\n- 📂 **Recherche des jeux dans un dossier** : `Chercher dans un dossier…`"));
        // --select n: the first n games are selected (multi-selection bar)
        foreach (var g in vm.Library.Games.OrderByDescending(g => g.LastPlayedAt).Take(o.Select)) g.IsSelected = true;
        var first = vm.DetailGame ?? vm.VisibleGames.FirstOrDefault() ?? vm.Library.Games.FirstOrDefault();
        if (first != null)
        {
            switch (o.Dialog)
            {
                case "edit": vm.EditCommand.Execute(first); break;
                case "cover": vm.SearchCoverCommand.Execute(first); break;
                case "confirm": vm.RemoveCommand.Execute(first); break;
                case "scan": vm.ScanPcCommand.Execute(null); break;
                case "collections": vm.ManageCollectionsCommand.Execute(first); break;
                case "emulators": vm.ManageEmulatorsCommand.Execute(null); break;
                case "roulette": vm.OpenRouletteCommand.Execute(null); break;
                case "themes": vm.OpenThemesCommand.Execute(null); break;
                case "recap": vm.OpenRecapCommand.Execute(null); break;
                case "duplicates": vm.ShowDuplicatesCommand.Execute(null); break;
            }
        }

        await Task.Delay(o.SnapshotDelay);
        if (o.Mouse != null) await DevMouse.RunAsync(window, o.Mouse);
        if (o.Keys != null)
        {
            foreach (var name in o.Keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (name.StartsWith("wait", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[4..], out var ms)) { await Task.Delay(ms); continue; }
                if (Enum.TryParse<System.Windows.Input.Key>(name, true, out var key)) window.DevKey(key);
                await Task.Delay(350);
            }
            await Task.Delay(900);
        }
        if (o.Scroll > 0)
        {
            // scroll the page's main list (the widest scrollable viewer) to check cards cut by its edge
            // (equal widths: the last one in the tree is drawn on top, e.g. a game page over the home page)
            var main = FindAll<System.Windows.Controls.ScrollViewer>(window).Where(s => s.IsVisible && s.ScrollableHeight > 0).OrderBy(s => s.ActualWidth).LastOrDefault();
            main?.ScrollToVerticalOffset(o.Scroll);
            window.UpdateLayout();
            Log.Info($"--scroll: {main?.ActualWidth} offset {main?.VerticalOffset} / {main?.ScrollableHeight}");
            await Task.Delay(300);
        }
        if (o.Hover)
        {
            ShowHoverState(window);
            await Task.Delay(900); // a card under the list edge scrolls itself into view
            ShowHoverState(window);
            window.UpdateLayout(); // RenderTransform is only applied to the visual at the next arrange pass
        }
        if (o.Perf)
        {
            var result = await PerfProbe.RunAsync(window);
            Log.Info(result);
            File.WriteAllText(o.Snapshot + ".perf.txt", result);
        }
        try
        {
            var root = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(window);
            var w = (int)(root.ActualWidth * dpi.DpiScaleX);
            var h = (int)(root.ActualHeight * dpi.DpiScaleY);
            var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen())
                dc.DrawRectangle((Brush)window.FindResource("Bg0"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            rtb.Render(bg);
            rtb.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.Snapshot!))!);
            await using (var fs = File.Create(o.Snapshot!)) encoder.Save(fs);
            Log.Info($"Snapshot written to {o.Snapshot}");
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot failed", ex);
        }
        DevMouse.Restore();
        window.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* not owner */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
