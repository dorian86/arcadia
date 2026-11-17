using System.Diagnostics;
using Arcadia.Models;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Arcadia.Services;

/// <summary>Notification-area icon with a dark menu (open, recent games, TV mode, quit).</summary>
public sealed class TrayService : IDisposable
{
    private Forms.NotifyIcon? _icon;
    private readonly Func<IEnumerable<Game>> _recent;

    public event Action? ShowRequested;
    public event Action? QuitRequested;
    public event Action? TvModeRequested;
    public event Action<Game>? PlayRequested;

    public TrayService(Func<IEnumerable<Game>> recent) => _recent = recent;

    public void SetVisible(bool visible)
    {
        if (visible && _icon == null)
        {
            _icon = new Forms.NotifyIcon { Text = "Arcadia", Icon = LoadIcon(), Visible = true };
            _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowRequested?.Invoke(); };
            var menu = new Forms.ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = new Drawing.Font("Segoe UI", 9.5f) };
            menu.Opening += (_, _) => BuildMenu(menu);
            _icon.ContextMenuStrip = menu;
        }
        else if (!visible && _icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }

    public void ShowBalloon(string title, string text)
    {
        try { _icon?.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.None); } catch { /* ignore */ }
    }

    private void BuildMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        var open = new Forms.ToolStripMenuItem("Ouvrir Arcadia") { Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Bold) };
        open.Click += (_, _) => ShowRequested?.Invoke();
        menu.Items.Add(open);
        var recent = _recent().Take(6).ToList();
        if (recent.Count > 0)
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripLabel("JOUER") { ForeColor = Drawing.Color.FromArgb(110, 116, 135), Font = new Drawing.Font("Segoe UI", 8f, Drawing.FontStyle.Bold) });
            foreach (var g in recent)
            {
                var item = new Forms.ToolStripMenuItem("▶  " + g.Name);
                item.Click += (_, _) => PlayRequested?.Invoke(g);
                menu.Items.Add(item);
            }
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        var tv = new Forms.ToolStripMenuItem("Mode TV (manette)");
        tv.Click += (_, _) => TvModeRequested?.Invoke();
        menu.Items.Add(tv);
        var quit = new Forms.ToolStripMenuItem("Quitter");
        quit.Click += (_, _) => QuitRequested?.Invoke();
        menu.Items.Add(quit);
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Arcadia;component/Assets/Arcadia.ico"));
            if (info != null) return new Drawing.Icon(info.Stream, Forms.SystemInformation.SmallIconSize);
        }
        catch { /* fallback below */ }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose() => SetVisible(false);

    private sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is not Forms.ToolStripLabel) e.TextColor = Drawing.Color.FromArgb(242, 244, 248);
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColors : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Bg = Drawing.Color.FromArgb(26, 31, 43);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(40, 47, 62);
        private static readonly Drawing.Color Line = Drawing.Color.FromArgb(47, 55, 73);
        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Bg;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
    }
}

/// <summary>Self-installation (per user, no admin rights), start with Windows, and uninstallation.</summary>
public static class InstallService
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Arcadia";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Arcadia");
    public static string InstalledExe => Path.Combine(InstallDir, "Arcadia.exe");
    public static string? CurrentExe => Environment.ProcessPath;

    /// <summary>A published single-file exe can be installed; a development build (with Arcadia.dll next to it) cannot.</summary>
    public static bool CanInstall => CurrentExe != null && !File.Exists(Path.Combine(Path.GetDirectoryName(CurrentExe)!, "Arcadia.dll"));

    public static bool IsInstalled => File.Exists(InstalledExe);

    public static bool IsRunningInstalled =>
        CurrentExe != null && string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    private static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Arcadia.lnk");
    private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Arcadia.lnk");

    public static async Task InstallAsync(bool desktopShortcut)
    {
        if (!CanInstall || CurrentExe == null) throw new InvalidOperationException("Cette version de développement ne peut pas être installée.");
        Directory.CreateDirectory(InstallDir);
        if (!IsRunningInstalled) File.Copy(CurrentExe, InstalledExe, overwrite: true);

        await Sta.Run(() =>
        {
            ShortcutResolver.CreateLnk(StartMenuShortcut, InstalledExe, null, InstallDir);
            if (desktopShortcut) ShortcutResolver.CreateLnk(DesktopShortcut, InstalledExe, null, InstallDir);
        });

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Arcadia");
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("DisplayVersion", typeof(InstallService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        key.SetValue("Publisher", "Arcadia");
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        Log.Info($"Installed to {InstallDir}");
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Arcadia") != null;
        }
    }

    public static void SetStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = IsInstalled ? InstalledExe : CurrentExe;
                if (exe != null) key.SetValue("Arcadia", $"\"{exe}\" --tray");
            }
            else key.DeleteValue("Arcadia", false);
        }
        catch (Exception ex) { Log.Warn($"Start-with-Windows change failed: {ex.Message}"); }
    }

    /// <summary>Removes shortcuts, registry entries, optionally the user data, then deletes the program folder.</summary>
    public static void Uninstall(bool removeUserData, string dataDir)
    {
        foreach (var f in new[] { StartMenuShortcut, DesktopShortcut })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* ignore */ }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { /* ignore */ }
        SetStartWithWindows(false);
        if (removeUserData)
            try { Directory.Delete(dataDir, true); } catch { /* files in use */ }

        // the running exe cannot delete itself: let cmd do it once we have exited
        var cmd = $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{InstallDir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }
}
