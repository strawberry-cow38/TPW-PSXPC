using Avalonia;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TPW.Launcher;

// The launcher window. Code-only Avalonia, no XAML. It owns the flow -- resolve tools, clone/refresh, build,
// check the user's game data, launch -- and nothing else: every decision it makes lives in core/TPW.Launcher
// where the test suite can run it. That split is not tidiness. A launcher window cannot be driven headlessly
// on a build box, so logic left in here is only ever verified by a human clicking it, which is exactly how two
// bugs shipped in the launcher this one is modelled on.
public class MainWindow : Window
{
    const string RepoUrl = "https://github.com/strawberry-cow38/TPW-PSXPC.git";
    const string DefaultBranch = "main";
    const string Solution = "TPW.sln";
    const string BuildConfig = "Debug";

    // ⚠ BUMP THIS WITH EVERY LAUNCHER CHANGE **AND PUBLISH THE RELEASE**. Self-update only fires when the
    // published launcher.version is strictly GREATER than this, so a code change without both halves reaches
    // nobody -- the change ships, no one's launcher updates, and the feature simply does not exist for them.
    // The number is the release; the note beside it is what shipped in that release. Move both together or
    // the note rots into a lie, which is precisely what happened to unturnedGD's.
    const int LauncherVersion = 4;   // v4: no functional change -- published to prove v3 self-updates, which is only testable against a HIGHER published version
    // v3: self-update -- downloads the published exe, verifies shape AND sha256, swaps via a shim that waits on this PID
    // v2: fixed a null-Text crash on the first log line; startup probe moved off the UI thread; unhandled exceptions now reach the panel and launcher-crash.log
    // v1: first TPW launcher -- clone/build, game-data identification, Play
    const string VersionUrl = "https://github.com/strawberry-cow38/TPW-PSXPC/releases/download/launcher/launcher.version";
    const string ExeUrl = "https://github.com/strawberry-cow38/TPW-PSXPC/releases/download/launcher/TPWLauncher-win-x64.exe";
    // ⚠ Publish this beside the exe. Without it the update can only check the download LOOKS like a program,
    // not that it is the one published -- see SelfUpdate.Check, which says so in its reason rather than
    // quietly downgrading itself.
    const string Sha256Url = "https://github.com/strawberry-cow38/TPW-PSXPC/releases/download/launcher/launcher.sha256";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    // Palette lifted from the in-game UI so launcher and game read as one product.
    static SolidColorBrush B(string hex) => new(Color.Parse(hex));
    static readonly IBrush BgSolid   = B("#212124");
    static readonly IBrush BarSolid  = B("#303033");
    static readonly IBrush PanelEdge = B("#3c3c41");
    static readonly IBrush TextMain  = B("#E0E0E8");
    static readonly IBrush TextBody  = B("#C9C9C9");
    static readonly IBrush TextDim   = B("#8A8A93");
    static readonly IBrush Good      = B("#7FCB8A");
    static readonly IBrush Bad       = B("#D98A7F");

    readonly TextBox _log = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
        Background = Brushes.Transparent, Foreground = TextBody, BorderThickness = new Thickness(0),
        FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, MinHeight = 220,
        Text = "",   // see Log(): a default TextBox carries null here, and every append would dereference it
    };
    readonly TextBlock _dataStatus = new() { Foreground = TextDim, FontSize = 13, TextWrapping = TextWrapping.Wrap };
    readonly Button _play = new() { Content = "Play", IsEnabled = false, MinWidth = 110 };
    readonly Button _update = new() { Content = "Update", MinWidth = 110 };
    readonly CheckBox _console = new() { Content = "Debug console", Foreground = TextBody };

    readonly string _baseDir = AppContext.BaseDirectory;
    string _repoDir => Path.Combine(_baseDir, "TPW-PSXPC");
    string _gameDataPath;          // what the user pointed us at (file or folder)
    GameDataResult _gameData;      // the result of identifying it
    bool _busy;

    public MainWindow()
    {
        Title = "Theme Park World — Godot";
        Width = 720; Height = 560;
        Background = BgSolid;

        var head = new TextBlock { Text = "Theme Park World", Foreground = TextMain, FontSize = 16, FontWeight = FontWeight.SemiBold };
        var sub = new TextBlock { Text = "PlayStation release, reimplemented in Godot", Foreground = TextDim, FontSize = 13 };

        var locate = new Button { Content = "Locate…", MinWidth = 90 };
        locate.Click += async (_, _) => await PickGameDataAsync();

        _update.Click += async (_, _) => await WithBusy(UpdateAsync);
        _play.Click += async (_, _) => await WithBusy(PlayAsync);

        var dataCard = Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Game data", Foreground = TextMain, FontSize = 13, FontWeight = FontWeight.SemiBold },
                _dataStatus,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { locate } },
            },
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _update, _play, _console },
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 12,
                Children = { head, sub, dataCard, actions, Card(_log) },
            },
        };

        // ⚠ `Opened += async ...` is effectively async void: an exception inside it takes the PROCESS down
        // with no message a user can report. That is exactly how the first crash presented -- "crashes after
        // a few seconds" and nothing else to go on. Now anything unhandled lands in the log panel AND in a
        // file next to the exe, so the next one arrives as a stack trace instead of a symptom.
        Opened += async (_, _) =>
        {
            try { await InitAsync(); }
            catch (Exception e) { Fatal(e); }
        };
    }

    void Fatal(Exception e)
    {
        try { File.AppendAllText(Path.Combine(_baseDir, "launcher-crash.log"), $"{DateTime.Now:s}  {e}\n\n"); }
        catch { /* if even that fails, the panel below is still worth trying */ }
        Log("UNEXPECTED ERROR — please report this:");
        Log(e.ToString());
    }

    Border Card(Control inner) => new()
    {
        Background = BarSolid, BorderBrush = PanelEdge, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Child = inner,
    };

    async Task InitAsync()
    {
        Log($"TPW launcher v{LauncherVersion}");
        // ⚠ If a handoff is underway this process is closing; doing anything further -- especially hashing a
        // 500 MB disc -- races the swap and wastes the user's time on a launcher that is going away.
        if (await CheckSelfUpdateAsync()) return;
        // ⚠ OFF THE UI THREAD. Probing hashes whatever it finds, and the common case is a ~500 MB disc
        // image -- doing that inline freezes the window for seconds at startup, which reads to a user as a
        // hang or a crash. I had already put the MANUAL pick on a background thread and then called the same
        // work synchronously from startup: fixed in the place I was thinking about, missed the other caller.
        Log("Looking for your copy of Theme Park World…");
        var found = await Task.Run(() => GameDataLocator.Probe());
        RefreshGameData(found);
    }

    // ---- game data ----------------------------------------------------------------------------------

    async Task PickGameDataAsync()
    {
        // Offer a file first: the common case is a single .bin disc image. A folder picker is the fallback
        // for an already-extracted copy.
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select your Theme Park World disc image or boot executable",
            AllowMultiple = false,
        });
        string picked = files?.Count > 0 ? files[0].Path.LocalPath : null;
        if (picked == null)
        {
            var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "…or the folder you extracted it to", AllowMultiple = false });
            picked = dirs?.Count > 0 ? dirs[0].Path.LocalPath : null;
        }
        if (picked == null) return;

        _gameDataPath = picked;
        Log($"Checking {picked} …");
        // Hashing a 500 MB image takes a moment; keep the UI alive.
        var r = await Task.Run(() => GameDataLocator.Identify(picked));
        RefreshGameData(r);
    }

    void RefreshGameData(GameDataResult r)
    {
        _gameData = r;
        _dataStatus.Text = r.Message;
        _dataStatus.Foreground = r.CanPlay ? Good : Bad;
        Log(r.Message);
        UpdateButtons();
    }

    // ---- flow ---------------------------------------------------------------------------------------

    async Task WithBusy(Func<Task> op)
    {
        // ⚠ RECORD BUSY BEFORE DECIDING ANYTHING, and gate every action on one flag. The launcher this is
        // modelled on returned early when busy BEFORE writing the user's branch choice anywhere, so the
        // dropdown showed one branch while the file held another and the update fetched the old one. The
        // visible half was the wrong half.
        if (_busy) { Log("Busy — wait for the current step to finish."); return; }
        _busy = true; UpdateButtons();
        try { await op(); }
        catch (Exception e) { Log("ERROR: " + e.Message); }
        finally { _busy = false; UpdateButtons(); }
    }

    void UpdateButtons()
    {
        _update.IsEnabled = !_busy;
        // ⚠ Play stays OFF without identified game data. That refusal IS the bring-your-own-assets promise:
        // a port that half-runs on an unrecognised copy produces plausible nonsense, which is worse than not
        // starting. See GameData.Identify.
        _play.IsEnabled = !_busy && _gameData.CanPlay && Directory.Exists(_repoDir);
    }

    async Task UpdateAsync()
    {
        string git = Which("git") ?? throw new Exception("git not found on PATH.");
        if (!Directory.Exists(_repoDir))
        {
            Log("Cloning …");
            await RunAsync(git, new[] { "clone", "--branch", DefaultBranch, RepoUrl, _repoDir }, _baseDir);
        }
        else
        {
            Log("Updating …");
            await RunAsync(git, new[] { "fetch", "--all", "--prune" }, _repoDir);
            await RunAsync(git, new[] { "reset", "--hard", "origin/" + DefaultBranch }, _repoDir);
        }

        string dotnet = Which("dotnet") ?? throw new Exception("dotnet SDK not found on PATH.");
        Log("Building …");
        int rc = await RunAsync(dotnet, new[] { "build", Solution, "-c", BuildConfig, "--nologo" }, _repoDir);
        Log(rc == 0 ? "Build OK." : $"Build FAILED (exit {rc}).");
        UpdateButtons();
    }

    async Task PlayAsync()
    {
        if (!_gameData.CanPlay) { Log("No recognised game data — cannot start."); return; }

        string godot = Which("godot");
        if (godot == null) { Log("Godot not found on PATH. (Auto-download lands with the game project.)"); return; }

        // Ask core which binary to run, and then LOG WHAT WE DID rather than what was asked for.
        var choice = LauncherRules.GodotExeFor(godot, _console.IsChecked == true, File.Exists);
        if (!choice.Satisfied)
            Log($"Note: the {( _console.IsChecked == true ? "console" : "windowed")} build was not found; starting {Path.GetFileName(choice.Path)} instead.");

        var psi = new ProcessStartInfo(choice.Path) { UseShellExecute = false, WorkingDirectory = _repoDir };
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(_repoDir);
        // The port reads the user's own game data from here. Never bundled, never redistributed.
        psi.Environment["TPW_DATA"] = _gameDataPath ?? "";
        psi.Environment["TPW_VARIANT"] = _gameData.Variant?.Id ?? "";
        Log($"Starting {Path.GetFileName(choice.Path)} ({_gameData.Variant?.Id}) …");
        Process.Start(psi);
    }

    // ---- self update --------------------------------------------------------------------------------

    /// <summary>Replace this launcher with the published build, via a shim that runs after we exit.
    ///
    /// ⚠ A PROCESS CANNOT OVERWRITE ITS OWN RUNNING EXECUTABLE ON WINDOWS -- the file is locked while it
    /// runs. So the new build is written beside the old one, a tiny batch file is started that WAITS for this
    /// PID to disappear, swaps the files, relaunches and deletes itself, and then we close. The shim exists
    /// solely to be the thing still alive when the launcher is not.
    /// Returns true if a handoff is underway, in which case the caller must stop doing anything else.</summary>
    async Task<bool> CheckSelfUpdateAsync()
    {
        // Self-update is Windows-only because the shim is a .bat. On anything else, say so rather than
        // silently never updating.
        if (!OperatingSystem.IsWindows()) return false;

        string exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        try
        {
            string raw = await Http.GetStringAsync(VersionUrl);
            // Tested in core: strictly greater, and unparseable does nothing -- so a 404 page read as a
            // version can never start a self-replacement.
            if (!LauncherRules.ShouldSelfUpdate(LauncherVersion, raw)) return false;

            Log($"Launcher update available: v{LauncherVersion} -> v{raw.Trim()}. Downloading…");
            byte[] bytes = await Http.GetByteArrayAsync(ExeUrl);

            // The published hash, if there is one. ⚠ Its ABSENCE must not look like success -- Check()
            // returns a reason either way and we log it, so a launcher that has stopped verifying says so.
            string expected = null;
            try { expected = (await Http.GetStringAsync(Sha256Url)).Trim().Split(' ')[0]; } catch { }

            var verdict = SelfUpdate.Check(bytes, expected);
            Log($"Update check: {verdict.Reason}");
            if (!verdict.Accept) { Log("Update ABORTED — still running the current launcher."); return false; }

            string newExe = exePath + ".new";
            await File.WriteAllBytesAsync(newExe, bytes);

            int pid = Environment.ProcessId;
            string bat = Path.Combine(Path.GetTempPath(), "tpw_selfupdate.bat");
            const string q = "\"";
            // ⚠ WAIT ON THE PID, do not just sleep. A fixed delay is a race: too short and the move fails
            // against a locked file, too long and the user stares at nothing. tasklist polling ends exactly
            // when the process is gone.
            await File.WriteAllTextAsync(bat, string.Join("\r\n", new[]
            {
                "@echo off",
                ":wait",
                $"tasklist /FI {q}PID eq {pid}{q} | find {q}{pid}{q} >nul && (ping -n 2 127.0.0.1 >nul & goto wait)",
                $"move /y {q}{newExe}{q} {q}{exePath}{q} >nul",
                $"start {q}{q} {q}{exePath}{q}",
                // ⚠ The shim deletes itself LAST. Leaving it behind means the next update finds a stale file
                // from a previous version and may run that instead of the one just written.
                $"del {q}%~f0{q}",
            }) + "\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c {q}{bat}{q}")
            { UseShellExecute = false, CreateNoWindow = true });

            Log("Restarting into the new launcher…");
            Dispatcher.UIThread.Post(Close);
            return true;
        }
        catch (Exception e)
        {
            // ⚠ Offline is NORMAL and must never block the launcher. Any failure here means "carry on with
            // the version we have", never "stop".
            Log("(launcher self-update skipped: " + e.Message + ")");
            return false;
        }
    }

    // ---- plumbing -----------------------------------------------------------------------------------

    static string Which(string exe)
    {
        string envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in envPath.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (string cand in OperatingSystem.IsWindows() ? new[] { exe + ".exe", exe + ".cmd", exe } : new[] { exe })
            {
                try { string p = Path.Combine(dir, cand); if (File.Exists(p)) return p; } catch { }
            }
        }
        return null;
    }

    async Task<int> RunAsync(string exe, string[] args, string wd)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = wd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
        p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    // ⚠⚠ A FRESH Avalonia TextBox HAS Text == null, NOT "". `_log.Text.Length` therefore threw a
    // NullReferenceException on the very FIRST line logged, which is the first thing the launcher does --
    // so it died a moment after opening, every time, for everyone. Fixed by never assuming the property is
    // initialised.
    //
    // ⚠ AND NOTE WHERE IT WAS: I moved every DECISION into core/TPW.Launcher precisely because a window
    // cannot be tested, and 25 tests prove the decisions are right. Then shipped a null dereference on line
    // one of the window. Moving logic out makes the remaining code MORE dangerous per line, not less --
    // what is left is the part no test will ever touch, so it wants reading, not confidence.
    void Log(string line) => Dispatcher.UIThread.Post(() =>
    {
        string cur = _log.Text ?? "";
        _log.Text = cur.Length > 0 ? cur + "\n" + line : line;
        _log.CaretIndex = _log.Text.Length;
    });
}
