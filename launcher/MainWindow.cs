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
    const int LauncherVersion = 7;   // v7: --path pointed at the repo root, so Play opened Godot's project manager
    // v6: Install/Update/Play merged into ONE mode-driven button; build marker
    // v5: branch dropdown, Godot auto-download, current-vs-latest commit, Options panel, settings persisted beside the exe
    // v4: no functional change -- published to prove v3 self-updates, which is only testable against a HIGHER published version
    // v3: self-update -- downloads the published exe, verifies shape AND sha256, swaps via a shim that waits on this PID
    // v2: fixed a null-Text crash on the first log line; startup probe moved off the UI thread; unhandled exceptions now reach the panel and launcher-crash.log
    // v1: first TPW launcher -- clone/build, game-data identification, Play
    const string VersionUrl = "https://github.com/strawberry-cow38/TPW-PSXPC/releases/download/launcher/launcher.version";
    const string ExeUrl = "https://github.com/strawberry-cow38/TPW-PSXPC/releases/download/launcher/TPWLauncher-win-x64.exe";
    // ⚠ Publish this beside the exe. Without it the update can only check the download LOOKS like a program,
    // not that it is the one published -- see SelfUpdate.Check, which says so in its reason rather than
    // quietly downgrading itself.
    const string Sha256Url = "https://github.com/strawberry-cow38/TPW-PSXPC/releases/download/launcher/launcher.sha256";

    // Godot 4.6 mono win64, matching the game csproj's Godot.NET.Sdk/4.6.2. Auto-downloaded when absent, so a
    // user needs git and the dotnet SDK and nothing else.
    const string GodotUrl = "https://downloads.godotengine.org/?version=4.6&flavor=stable&slug=mono_win64.zip&platform=windows.64";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

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
    // ⭐ ONE BUTTON. Its label and what it does come from _mode, so the user is never asked to work out
    // whether this install needs a clone, a rebuild, a disc or nothing. Install/Update/Play are STAGES OF
    // ONE INTENT ("I want to play"), not three choices -- and a launcher that offers Play next to Update
    // invites clicking Play on a stale build, which is the one combination that produces a confusing crash
    // rather than an honest refusal.
    enum Mode { Busy, NeedData, Build, Play, Broken }
    readonly Button _action = new() { MinWidth = 168, MinHeight = 40, FontSize = 15, IsEnabled = false };
    readonly TextBlock _status = new() { Foreground = TextDim, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    Mode _mode = Mode.Busy;
    readonly CheckBox _console = new() { Content = "Debug console", Foreground = TextBody };
    readonly ComboBox _branches = new() { MinWidth = 180 };
    readonly TextBlock _buildState = new() { Foreground = TextDim, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _latestState = new() { Foreground = TextDim, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    string _branch = DefaultBranch;

    readonly string _baseDir = AppContext.BaseDirectory;
    string _repoDir => Path.Combine(_baseDir, "TPW-PSXPC");
    // ⚠ SETTINGS LIVE ON DISK BESIDE THE EXE, NOT IN THE CONTROLS. Two reasons, both learned the hard way in
    // the launcher this one is modelled on: an action can run on a launcher whose window was never shown, so
    // the control may not be initialised; and a self-update replaces the exe, so anything held only in the
    // process is lost on every upgrade. The checkbox WRITES the file; the file is what anything else reads.
    string BranchFile => Path.Combine(_baseDir, "branch.txt");
    string ConsoleFile => Path.Combine(_baseDir, "debug_console.txt");
    string DataPathFile => Path.Combine(_baseDir, "game_data_path.txt");
    // ⚠ "BUILT" MEANS *WE* BUILT THIS EXACT COMMIT -- nothing else is evidence. Do not infer it from
    // game/.godot or a bin/ folder existing: a repo can ship a committed, machine-specific .godot with stale
    // assemblies, and then a fresh clone looks "ready" and Plays a mismatched dll. Only our own marker counts.
    // It also makes a branch switch self-correcting: the marker still holds the OLD commit, which no longer
    // matches HEAD, so a rebuild is forced without anyone having to detect "the branch changed".
    string BuiltMarker => Path.Combine(_baseDir, "built_commit.txt");

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

        _action.Click += async (_, _) => await WithBusy(OnActionAsync);

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

        _branches.SelectionChanged += async (_, _) =>
        {
            // ⚠ RECORD THE CHOICE BEFORE ANYTHING CAN DECLINE TO ACT ON IT. The launcher this is modelled on
            // returned early when busy BEFORE persisting the selection, so the dropdown showed one branch
            // while branch.txt held another and Update fetched the old one -- the visible half was the wrong
            // half. Persist first, then let the refresh wait its turn.
            if (_branches.SelectedItem is not string b || b == _branch) return;
            _branch = b;
            TryWrite(BranchFile, b);
            Log($"Branch set to {b}.");
            await WithBusy(RefreshAsync);
        };

        var checkUpdate = new Button { Content = "Check for update", MinWidth = 130 };
        checkUpdate.Click += async (_, _) => await WithBusy(async () =>
        {
            if (!await CheckSelfUpdateAsync()) Log($"Launcher is up to date (v{LauncherVersion}).");
            await RefreshAsync();
        });

        var options = new Expander
        {
            Header = "Options", Foreground = TextBody,
            Content = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0),
                Children = { _console, checkUpdate, OpenFolderButton() } },
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Branch", Foreground = TextDim, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                _branches, _action, _status,
            },
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 12,
                Children = { head, sub, dataCard, actions, _buildState, _latestState, options, Card(_log) },
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
        // Restore what the user chose last time. A remembered game-data path matters most: re-hashing a
        // 500 MB image on every launch is slow, and re-asking for it is worse.
        _branch = TryRead(BranchFile) ?? DefaultBranch;
        _console.IsChecked = (TryRead(ConsoleFile) ?? "1") == "1";
        _console.IsCheckedChanged += (_, _) => TryWrite(ConsoleFile, _console.IsChecked == true ? "1" : "0");
        _gameDataPath = TryRead(DataPathFile);

        Log("Looking for your copy of Theme Park World…");
        var found = await Task.Run(() => string.IsNullOrEmpty(_gameDataPath)
            ? GameDataLocator.Probe()
            : GameDataLocator.Identify(_gameDataPath));
        RefreshGameData(found);

        await PopulateBranchesAsync();
        await RefreshAsync();
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
        TryWrite(DataPathFile, picked);
        Log($"Checking {picked} …");
        // Hashing a 500 MB image takes a moment; keep the UI alive.
        var r = await Task.Run(() => GameDataLocator.Identify(picked));
        RefreshGameData(r);
        await RefreshAsync();
    }

    void RefreshGameData(GameDataResult r)
    {
        _gameData = r;
        _dataStatus.Text = r.Message;
        _dataStatus.Foreground = r.CanPlay ? Good : Bad;
        Log(r.Message);
    }

    // ---- flow ---------------------------------------------------------------------------------------

    async Task WithBusy(Func<Task> op)
    {
        // ⚠ RECORD BUSY BEFORE DECIDING ANYTHING, and gate every action on one flag. The launcher this is
        // modelled on returned early when busy BEFORE writing the user's branch choice anywhere, so the
        // dropdown showed one branch while the file held another and the update fetched the old one. The
        // visible half was the wrong half.
        if (_busy) { Log("Busy — wait for the current step to finish."); return; }
        _busy = true;
        try { await op(); }
        catch (Exception e) { Log("ERROR: " + e.Message); SetMode(Mode.Broken, "—", e.Message); }
        finally { _busy = false; }
    }

    void SetMode(Mode m, string label, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _mode = m;
            _action.Content = label;
            _action.IsEnabled = m is Mode.NeedData or Mode.Build or Mode.Play;
            _action.Background = m == Mode.Play ? B("#3E6B45") : m == Mode.NeedData ? B("#7A5A2E") : B("#3A5A78");
            _action.Foreground = TextBody;
            _status.Text = status;
        });
    }

    void SetBusy(string status) => Dispatcher.UIThread.Post(() =>
        { _mode = Mode.Busy; _action.IsEnabled = false; _action.Content = "…"; _status.Text = status; });

    async Task OnActionAsync()
    {
        switch (_mode)
        {
            case Mode.NeedData: await PickGameDataAsync(); break;
            // "& Play" is a promise: build, then go straight in. Stopping at a second click after a
            // multi-minute build is the launcher asking permission for something already decided.
            // Chain Play only if it can actually succeed. Installing without a disc present is a perfectly
            // good outcome -- the button becomes "Locate game data…" -- and firing Play at it just to watch
            // it refuse would print an error for a state that is not an error.
            case Mode.Build: if (await UpdateAsync() && _gameData.CanPlay) await PlayAsync(); break;
            case Mode.Play: await PlayAsync(); break;
        }
    }

    /// <summary>Clone-or-update, build, import. Returns true only if the build actually succeeded -- the
    /// caller chains Play onto that, so a failed build must never read as "ready".</summary>
    async Task<bool> UpdateAsync()
    {
        SetBusy("Updating…");
        string git = Which("git") ?? throw new Exception("git not found on PATH.");
        if (!Directory.Exists(_repoDir))
        {
            Log("Cloning …");
            await RunAsync(git, new[] { "clone", "--branch", _branch, RepoUrl, _repoDir }, _baseDir);
        }
        else
        {
            Log("Updating …");
            await RunAsync(git, new[] { "fetch", "--all", "--prune" }, _repoDir);
            // ⚠ EXPLICIT REFSPEC so origin/<branch> exists locally for any branch, not just the cloned one.
            // A --branch clone only configures tracking for that one, so a plain fetch of another updates
            // FETCH_HEAD without creating the remote-tracking ref the reset below needs.
            await RunAsync(git, new[] { "fetch", "origin", $"+{_branch}:refs/remotes/origin/{_branch}" }, _repoDir);
            await RunAsync(git, new[] { "reset", "--hard", "origin/" + _branch }, _repoDir);
        }

        string dotnet = Which("dotnet") ?? throw new Exception("dotnet SDK not found on PATH.");
        SetBusy("Building…");
        Log("Building …");
        int rc = await RunAsync(dotnet, new[] { "build", Solution, "-c", BuildConfig, "--nologo" }, _repoDir);
        if (rc != 0)
        {
            Log($"Build FAILED (exit {rc}).");
            SetMode(Mode.Build, "Retry", "Build failed — see the log.");
            return false;
        }
        Log("Build OK.");

        // Godot imports assets on first run. Doing it here, headless, means the first thing the user sees is
        // the game rather than an import bar -- and it surfaces an import error in THIS log, next to its cause.
        string godot = await EnsureGodotAsync();
        if (godot != null)
        {
            SetBusy("Importing assets…");
            await RunAsync(godot, new[] { "--path", Path.Combine(_repoDir, "game"), "--headless", "--import" }, _repoDir);
        }

        // Stamp the marker with the commit we just built -- not with what we asked to fetch. If the reset
        // silently landed somewhere else, the marker must record where we ACTUALLY are or it certifies a lie.
        string head = (await Capture(git, new[] { "-C", _repoDir, "rev-parse", "HEAD" })).Trim();
        TryWrite(BuiltMarker, head);
        await RefreshAsync();
        return true;
    }

    async Task PlayAsync()
    {
        if (!_gameData.CanPlay) { Log("No recognised game data — cannot start."); return; }

        string godot = await EnsureGodotAsync();
        if (godot == null) { Log("No Godot available — cannot start."); return; }

        // ⚠ READ THE PREFERENCE FROM DISK, not from the checkbox. Play can run on a launcher whose window was
        // never shown, and the file is also what survives a self-update. The checkbox writes it; this reads it.
        bool wantConsole = (TryRead(ConsoleFile) ?? "1") == "1";
        // Ask core which binary to run, and then LOG WHAT WE DID rather than what was asked for.
        var choice = LauncherRules.GodotExeFor(godot, wantConsole, File.Exists);
        if (!choice.Satisfied)
            Log($"Note: the {(wantConsole ? "console" : "windowed")} build was not found; starting {Path.GetFileName(choice.Path)} instead.");

        // ⚠⚠ THE GODOT PROJECT IS IN game/, NOT AT THE REPO ROOT. Pointing --path at the repo root does not
        // fail: Godot finds no project.godot, shrugs, and opens the PROJECT MANAGER. The user gets an empty
        // Godot window and no error anywhere, which looks like the game failing to start for some deep reason.
        // Shipped exactly that in v6 — and my own "it runs" test missed it because I typed the game/ path by
        // hand instead of running the arguments the launcher actually builds. Testing the game is not testing
        // the launcher launching the game.
        string projectDir = Path.Combine(_repoDir, "game");
        if (!File.Exists(Path.Combine(projectDir, "project.godot")))
        {
            // Say it plainly rather than letting Godot silently substitute its own UI.
            Log($"No project.godot in {projectDir} — the build may be incomplete. Run Update first.");
            await RefreshAsync();
            return;
        }

        var psi = new ProcessStartInfo(choice.Path) { UseShellExecute = false, WorkingDirectory = projectDir };
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(projectDir);
        // The port reads the user's own game data from here. Never bundled, never redistributed.
        psi.Environment["TPW_DATA"] = _gameDataPath ?? "";
        psi.Environment["TPW_VARIANT"] = _gameData.Variant?.Id ?? "";
        Log($"Starting {Path.GetFileName(choice.Path)} ({_gameData.Variant?.Id}) …");
        Process.Start(psi);
    }

    // ---- branches, build state, Godot ---------------------------------------------------------------

    async Task PopulateBranchesAsync()
    {
        var names = new List<string> { DefaultBranch };
        string git = Which("git");
        if (git != null)
        {
            string outp = await Capture(git, new[] { "ls-remote", "--heads", RepoUrl });
            foreach (string line in outp.Split('\n'))
            {
                int i = line.IndexOf("refs/heads/", StringComparison.Ordinal);
                if (i < 0) continue;
                string n = line[(i + "refs/heads/".Length)..].Trim();
                if (n.Length > 0 && !names.Contains(n)) names.Add(n);
            }
        }
        Dispatcher.UIThread.Post(() =>
        {
            _branches.ItemsSource = names;
            // ⚠ A remembered branch that no longer exists must not silently become "whatever is first".
            _branches.SelectedItem = names.Contains(_branch) ? _branch : DefaultBranch;
            if (!names.Contains(_branch)) { Log($"Branch '{_branch}' no longer exists — using {DefaultBranch}."); _branch = DefaultBranch; }
        });
    }

    /// <summary>The single place that decides what the button is. Everything else calls this and lets it
    /// choose: one decision site means the label, the enabled state and the action can never disagree.</summary>
    async Task RefreshAsync()
    {
        string git = Which("git");
        if (git == null || Which("dotnet") == null)
        {
            var broke = LauncherState.Decide("", "", "", false, _branch, git != null, Which("dotnet") != null);
            SetMode(Mode.Broken, broke.Label, broke.Status);
            return;
        }

        string remote = (await Capture(git, new[] { "ls-remote", RepoUrl, _branch })).Split('\t')[0].Trim();
        string local = Directory.Exists(_repoDir)
            ? (await Capture(git, new[] { "-C", _repoDir, "rev-parse", "HEAD" })).Trim() : "";
        string subject = local.Length > 0
            ? (await Capture(git, new[] { "-C", _repoDir, "log", "-1", "--format=%h · %cr · %s" })).Trim() : "";

        string built = TryRead(BuiltMarker) ?? "";

        static string Short(string h) => h.Length >= 7 ? h[..7] : (h.Length > 0 ? h : "—");
        Dispatcher.UIThread.Post(() =>
        {
            _buildState.Text = local.Length == 0 ? "Installed build:   none yet" : $"Installed build:   {subject}";
            _latestState.Text = $"Latest on {_branch}:   {Short(remote)}"
                + (remote.Length == 0 ? "   (couldn't reach the remote)" : "");
        });

        // ⭐ ONE DECISION SITE, AND IT IS IN CORE. This window cannot be run on the build machine, so every
        // branch of this choice would otherwise ship unexercised -- and the button's caption IS the feature.
        // LauncherState.Decide is a pure function of the world state with tests that name the wrong answer
        // each one rejects. Keeping a second copy here to "save a call" is how the two drift apart.
        var st = LauncherState.Decide(local, remote, built, _gameData.CanPlay, _branch);
        var kind = st.Kind switch
        {
            ActionKind.Play => Mode.Play,
            ActionKind.NeedData => Mode.NeedData,
            ActionKind.Build => Mode.Build,
            _ => Mode.Broken,
        };
        // Only the Play status gains anything from the window's own state: which copy of the game it matched.
        string status = st.Kind == ActionKind.Play && _gameData.Variant != null
            ? $"Up to date · {_gameData.Variant.Id}" : st.Status;
        SetMode(kind, st.Label, status);
    }

    /// <summary>Find Godot, or fetch it. A user should need git and the dotnet SDK and nothing else.</summary>
    async Task<string> EnsureGodotAsync()
    {
        string onPath = Which("godot") ?? Environment.GetEnvironmentVariable("TPW_GODOT_EXE");
        if (!string.IsNullOrEmpty(onPath) && File.Exists(onPath)) return onPath;

        string dir = Path.Combine(_baseDir, "godot");
        string have = FindGodotExe(dir);
        if (have != null) { Log("Godot (downloaded earlier): " + Path.GetFileName(have)); return have; }

        try
        {
            Log("Godot not found — downloading Godot 4.6 mono (win64), ~104 MB…");
            Directory.CreateDirectory(dir);
            byte[] bytes = await Http.GetByteArrayAsync(GodotUrl);
            // ⚠ A zip begins "PK". Same reasoning as the launcher update: an error page is a successful HTTP
            // response, and extracting one produces a confusing failure far from its cause.
            if (bytes.Length < 20_000_000 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
            { Log($"Godot download looks wrong ({bytes.Length:n0} bytes) — skipped."); return null; }

            string zip = Path.Combine(dir, "godot.zip");
            await File.WriteAllBytesAsync(zip, bytes);
            Log("Extracting…");
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
            try { File.Delete(zip); } catch { }
            string exe = FindGodotExe(dir);
            Log(exe != null ? "Godot ready: " + Path.GetFileName(exe) : "Godot extracted but no editor exe was found.");
            return exe;
        }
        catch (Exception e) { Log("Godot download failed: " + e.Message); return null; }
    }

    // The mono win64 zip extracts a Godot_v4.6-stable_mono_win64/ folder. Take the editor exe and skip the
    // *_console.exe -- GodotExeFor adds that suffix back when the user wants it, and picking the console
    // build here would make the toggle start from the wrong base.
    static string FindGodotExe(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (string f in Directory.GetFiles(dir, "Godot_v*_win64.exe", SearchOption.AllDirectories))
            if (!f.Contains("console", StringComparison.OrdinalIgnoreCase)) return f;
        return null;
    }

    Button OpenFolderButton()
    {
        var b = new Button { Content = "Open launcher folder", MinWidth = 160 };
        b.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(_baseDir) { UseShellExecute = true }); }
            catch (Exception e) { Log("Could not open folder: " + e.Message); }
        };
        return b;
    }

    static string TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; } catch { return null; }
    }

    static void TryWrite(string path, string value)
    {
        try { File.WriteAllText(path, value); } catch { /* a read-only install must not crash on a preference */ }
    }

    async Task<string> Capture(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = new Process { StartInfo = psi };
            p.Start();
            string o = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return o;
        }
        catch { return ""; }
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
