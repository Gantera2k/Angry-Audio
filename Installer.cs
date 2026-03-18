using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using AngryAudio;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AngryAudioInstaller
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "AngryAudioInstallerSingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Kill any existing installer instances — search broadly for renamed copies too
                    try
                    {
                        int myPid = Process.GetCurrentProcess().Id;
                        foreach (var proc in Process.GetProcesses())
                        {
                            try {
                                if (proc.Id != myPid && proc.ProcessName.StartsWith("Angry_Audio_Setup"))
                                    proc.Kill();
                            } catch { }
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(500);
                    try { mutex.WaitOne(3000); } catch { }
                }

                try { SetProcessDPIAware(); } catch { }

            // Auto-elevate to admin if not already
            if (!IsAdmin())
            {
                // Resolve our own exe path — try multiple methods for reliability
                string self = null;
                try { self = Application.ExecutablePath; } catch { }
                if (string.IsNullOrEmpty(self) || !System.IO.File.Exists(self))
                    try { self = Process.GetCurrentProcess().MainModule.FileName; } catch { }
                if (string.IsNullOrEmpty(self) || !System.IO.File.Exists(self))
                    try { self = Assembly.GetExecutingAssembly().Location; } catch { }
                if (string.IsNullOrEmpty(self) || !System.IO.File.Exists(self))
                    try { self = Environment.GetCommandLineArgs()[0]; } catch { }

                bool elevated = false;
                if (!string.IsNullOrEmpty(self) && System.IO.File.Exists(self))
                {
                    try
                    {
                        // Fallback elevation via runas verb.
                        // NOTE: For foreground UAC prompt, build with BUILD.bat (csc.exe)
                        // which embeds app.manifest with requireAdministrator. mcs on Linux
                        // cannot embed Win32 RT_MANIFEST resources, so runas is the fallback.
                        var psi = new ProcessStartInfo
                        {
                            FileName = self,
                            Arguments = string.Join(" ", args),
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                        elevated = true;
                    }
                    catch { } // User declined UAC or elevation failed
                }

                if (!elevated)
                {
                    DarkMessage.Show(
                        "Angry Audio Setup requires administrator privileges to install.\n\n" +
                        "Please right-click the installer and select \"Run as administrator\".",
                        "Angry Audio Setup");
                }
                return; // Always exit — either elevated copy is running, or user was told to retry
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Kill any running Angry Audio instances IMMEDIATELY on installer launch
            try
            {
                // Try graceful shutdown first via kill event (same mechanism the app uses)
                try
                {
                    System.Threading.EventWaitHandle killEvent;
                    if (System.Threading.EventWaitHandle.TryOpenExisting("Global\\Angry_Audio_Kill_Event", out killEvent))
                    {
                        killEvent.Set();
                        killEvent.Dispose();
                        System.Threading.Thread.Sleep(1500); // Give it time to shut down gracefully
                    }
                }
                catch { }

                // Force-kill anything still running
                foreach (var proc in Process.GetProcessesByName("Angry Audio"))
                    try { proc.Kill(); proc.WaitForExit(3000); } catch { }
            }
            catch { }

            bool uninstallMode = false;
            bool updateMode = false;
            foreach (string arg in args)
            {
                string a = arg.Trim().ToLowerInvariant();
                if (a == "/uninstall") uninstallMode = true;
                if (a == "/update")    updateMode = true;
            }

            if (updateMode)
            {
                // Silent update mode — minimal popup, no full installer UI
                Application.Run(new SilentUpdateForm());
            }
            else
            {
                Application.Run(new InstallerForm(uninstallMode));
            }
            } // end mutex using
        }

        static bool IsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    class InstallerForm : Form
    {
        // Colors — match Angry Audio dark theme
        static readonly Color BG = AngryAudio.DarkTheme.BG;
        // CARD color removed — StarBackground uses DarkTheme.CardBG directly
        static readonly Color ACC = AngryAudio.DarkTheme.Accent;
        static readonly Color TXT = Color.FromArgb(220, 220, 220);
        static readonly Color TXT2 = Color.FromArgb(160, 160, 160);
        static readonly Color TXT3 = Color.FromArgb(100, 100, 100);
        static readonly Color BDR = AngryAudio.DarkTheme.Border;
        static readonly Color GREEN = Color.FromArgb(80, 200, 120);
        static readonly Color ERR = Color.FromArgb(220, 60, 60);

        // Registry
        const string UNINSTKEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Angry Audio";
        const string STARTUPKEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string STARTUPVAL = "AngryAudio";

        string _installDir;
        string _version;
        bool _installed;
        bool _installing;
        float _progress;
        string _status = "";
        string _error = "";
        Timer _paintTimer;
        float _dpi = 1f;

        // Uninstall mode
        bool _uninstallMode;
        bool _uninstalled;
        bool _uninstalling;

        float _orbitPhase;
        StarBackground _stars;

        // Hit-test rectangles — computed during OnPaint, used by OnClick
        Rectangle _btnRect1;  // Primary: Install / Launch / Retry / Uninstall
        bool _hoverBtn1, _hoverBtn2, _hoverBrowse;
        Rectangle _btnRect2;  // Secondary: Close (error state) / Cancel (uninstall confirm)
        Rectangle _browseRect; // Browse folder button

        // DPI scale for layout positions only — NOT for fonts
        int S(int v) { return (int)(v * _dpi); }

        public InstallerForm(bool uninstallMode = false)
        {
            _uninstallMode = uninstallMode;
            using (var g = CreateGraphics()) _dpi = g.DpiX / 96f;

            Text = uninstallMode ? "Angry Audio — Uninstall" : "Angry Audio Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ClientSize = new Size(S(500), S(329));

            // Force center on primary screen (CenterScreen unreliable after UAC elevation)
            var screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - ClientSize.Width) / 2,
                screen.Top + (screen.Height - ClientSize.Height) / 2);
            BackColor = BG;
            ForeColor = TXT;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);

            // Drop TopMost after showing so it doesn't stay pinned above everything
            Shown += (s, e) => { TopMost = false; Activate(); BringToFront(); };

            // Read previous install path from registry, fall back to Program Files
            _installDir = ReadRegistryInstallDir()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Angry Audio");
            _version = GetEmbeddedVersion();

            try { Icon = ExtractEmbeddedIcon(); } catch { }
            AngryAudio.DarkTheme.DarkTitleBar(Handle);

            // Stars now use DarkTheme.PaintCardStars for consistency
            _stars = new StarBackground(() => { try { if (!IsDisposed) Invalidate(); } catch { } });

            _paintTimer = new Timer { Interval = 33 }; // 30fps — smooth enough, halves CPU
            _paintTimer.Tick += (s, e) => { _orbitPhase += 0.08f; Invalidate(); };
            _paintTimer.Start();

            Paint += OnPaint;
            MouseClick += OnClick;
            MouseMove += (s, e) => {
                bool h1 = !_btnRect1.IsEmpty && _btnRect1.Contains(e.Location);
                bool h2 = !_btnRect2.IsEmpty && _btnRect2.Contains(e.Location);
                bool hb = !_browseRect.IsEmpty && _browseRect.Contains(e.Location);
                if (h1 != _hoverBtn1 || h2 != _hoverBtn2 || hb != _hoverBrowse) {
                    _hoverBtn1 = h1; _hoverBtn2 = h2; _hoverBrowse = hb;
                    Invalidate();
                }
            };
            MouseLeave += (s, e) => {
                if (_hoverBtn1 || _hoverBtn2 || _hoverBrowse) {
                    _hoverBtn1 = _hoverBtn2 = _hoverBrowse = false;
                    Invalidate();
                }
            };
        }

        string GetEmbeddedVersion()
        {
            try
            {
                // Try namespaced resource name first (mcs embeds as AngryAudio.version.txt)
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("AngryAudio.version.txt") ?? asm.GetManifestResourceStream("version.txt"))
                    if (stream != null)
                        using (var reader = new StreamReader(stream))
                            return reader.ReadToEnd().Trim();
            }
            catch { }
            return AppVersion.Version;
        }

        string ReadRegistryInstallDir()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(UNINSTKEY))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("InstallLocation");
                        if (val != null)
                        {
                            string dir = val.ToString().Trim();
                            if (dir.Length > 0) return dir;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        string ReadRegistryVersion()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(UNINSTKEY))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("DisplayVersion");
                        if (val != null) return val.ToString().Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        Icon ExtractEmbeddedIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                if (stream != null) return new Icon(stream);
            return null;
        }

        byte[] GetResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var ms = new MemoryStream()) { stream.CopyTo(ms); return ms.ToArray(); }
            }
        }

        void OnClick(object sender, MouseEventArgs e)
        {
            // --- Uninstall mode ---
            if (_uninstallMode)
            {
                if (_uninstalled)
                {
                    if (_btnRect1.Contains(e.Location)) Close();
                    return;
                }
                if (_uninstalling) return;
                if (_error != "")
                {
                    if (_btnRect1.Contains(e.Location)) Close();
                    return;
                }
                // Confirm screen: Uninstall / Cancel
                if (_btnRect1.Contains(e.Location)) DoUninstall();
                if (_btnRect2.Contains(e.Location)) Close();
                return;
            }

            // --- Install mode ---
            if (_error != "")
            {
                // Error state: Retry + Close
                if (_btnRect1.Contains(e.Location))
                {
                    _error = "";
                    Invalidate();
                    DoInstall();
                    return;
                }
                if (_btnRect2.Contains(e.Location))
                {
                    Close();
                    return;
                }
                return;
            }
            if (_installed)
            {
                if (_btnRect1.Contains(e.Location))
                {
                    try
                    {
                        string exe = Path.Combine(_installDir, "Angry Audio.exe");
                        if (File.Exists(exe))
                        {
                            // Launch via Explorer.exe to de-elevate — the installer runs as admin,
                            // and child processes inherit that token. Discord (and other normal-user apps)
                            // can't see keyboard input from elevated windows (Windows UIPI).
                            // Explorer.exe always runs as normal user, so it brokers a non-elevated launch.
                            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false });
                        }
                    }
                    catch { }
                    Close();
                }
                return;
            }
            if (_installing) return;
            if (_browseRect.Contains(e.Location))
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Select installation folder";
                    dlg.SelectedPath = _installDir;
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        string sel = dlg.SelectedPath;
                        if (!sel.EndsWith("Angry Audio", StringComparison.OrdinalIgnoreCase))
                            sel = Path.Combine(sel, "Angry Audio");
                        _installDir = sel;
                        Invalidate();
                    }
                }
                return;
            }
            if (_btnRect1.Contains(e.Location)) DoInstall();
        }

        void DoInstall()
        {
            _installing = true;
            _progress = 0;
            _status = "Preparing...";
            Invalidate();

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += (s, e) =>
            {
                try
                {
                    Report(worker, 0.05f, "Closing running instances...");
                    try
                    {
                        foreach (var proc in Process.GetProcessesByName("Angry Audio"))
                            try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(300);

                    Report(worker, 0.15f, "Checking for previous version...");
                    string prevVer = ReadRegistryVersion();
                    if (prevVer != null)
                    {
                        Report(worker, 0.2f, "Upgrading from v" + prevVer + "...");
                        // Kill running instance if still alive
                        try
                        {
                            foreach (var proc in Process.GetProcessesByName("Angry Audio"))
                                try { proc.Kill(); proc.WaitForExit(2000); } catch { }
                        }
                        catch { }
                    }
                    System.Threading.Thread.Sleep(200);

                    Report(worker, 0.25f, "Creating install directory...");
                    Directory.CreateDirectory(_installDir);
                    System.Threading.Thread.Sleep(100);

                    Report(worker, 0.4f, "Installing Angry Audio...");
                    byte[] exeData = GetResource("app.exe");
                    if (exeData != null)
                        File.WriteAllBytes(Path.Combine(_installDir, "Angry Audio.exe"), exeData);
                    System.Threading.Thread.Sleep(200);

                    Report(worker, 0.55f, "Installing resources...");
                    byte[] icoData = GetResource("app.ico");
                    if (icoData != null)
                        File.WriteAllBytes(Path.Combine(_installDir, "Angry Audio.ico"), icoData);
                    System.Threading.Thread.Sleep(150);

                    Report(worker, 0.65f, "Creating uninstaller...");
                    try
                    {
                        string self = Assembly.GetExecutingAssembly().Location;
                        if (!string.IsNullOrEmpty(self) && File.Exists(self))
                            File.Copy(self, Path.Combine(_installDir, "Uninstall.exe"), true);
                    }
                    catch { }
                    System.Threading.Thread.Sleep(100);

                    Report(worker, 0.75f, "Creating shortcuts...");
                    try
                    {
                        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "Angry Audio");
                        Directory.CreateDirectory(startMenu);
                        CreateShortcut(Path.Combine(startMenu, "Angry Audio.lnk"), Path.Combine(_installDir, "Angry Audio.exe"), Path.Combine(_installDir, "Angry Audio.ico"));
                    }
                    catch { }
                    try
                    {
                        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Angry Audio.lnk"),
                            Path.Combine(_installDir, "Angry Audio.exe"), Path.Combine(_installDir, "Angry Audio.ico"));
                    }
                    catch { }
                    System.Threading.Thread.Sleep(150);

                    Report(worker, 0.9f, "Registering application...");
                    try
                    {
                        using (var key = Registry.LocalMachine.CreateSubKey(UNINSTKEY))
                        {
                            key.SetValue("DisplayName", "Angry Audio");
                            key.SetValue("DisplayVersion", _version);
                            key.SetValue("Publisher", "Andrew Ganter");
                            key.SetValue("URLInfoAbout", "https://angryaudio.com");
                            key.SetValue("DisplayIcon", Path.Combine(_installDir, "Angry Audio.ico"));
                            key.SetValue("UninstallString", "\"" + Path.Combine(_installDir, "Uninstall.exe") + "\" /uninstall");
                            key.SetValue("InstallLocation", _installDir);
                            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        }
                    }
                    catch { }

                    // Reset FirstRunComplete so welcome wizard shows after fresh install
                    // Settings are stored in %AppData%/Angry Audio/settings.json, not registry
                    try
                    {
                        string settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Angry Audio");
                        string settingsFile = Path.Combine(settingsDir, "settings.json");
                        if (File.Exists(settingsFile))
                            File.Delete(settingsFile);
                    }
                    catch { }
                    System.Threading.Thread.Sleep(200);

                    // ── Equalizer APO ──
                    Report(worker, 0.92f, "Installing Equalizer APO...");
                    string apoDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "EqualizerAPO");
                    bool apoAlreadyInstalled = Directory.Exists(apoDir);
                    if (!apoAlreadyInstalled)
                    {
                        byte[] apoData = GetResource("apo.exe");
                        if (apoData != null)
                        {
                            string apoTemp = Path.Combine(Path.GetTempPath(), "EqualizerAPO_setup.exe");
                            try
                            {
                                File.WriteAllBytes(apoTemp, apoData);
                                var apoProc = new Process {
                                    StartInfo = new ProcessStartInfo {
                                        FileName = apoTemp,
                                        Arguments = "/S",
                                        UseShellExecute = true,
                                        Verb = "runas"
                                    }
                                };
                                apoProc.Start();
                                apoProc.WaitForExit(120000);

                                // APO's installer launches Configurator.exe post-install — kill it,
                                // we register devices ourselves programmatically below.
                                System.Threading.Thread.Sleep(1000);
                                try {
                                    foreach (var p in Process.GetProcessesByName("Configurator"))
                                        try { p.Kill(); } catch { }
                                } catch { }

                                // Disable APO's scheduled update checker — we manage APO ourselves
                                try {
                                    var schtasksPsi = new ProcessStartInfo {
                                        FileName = "schtasks.exe",
                                        Arguments = "/change /tn \"EqualizerAPOUpdateChecker\" /disable",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    };
                                    using (var p = Process.Start(schtasksPsi)) p.WaitForExit(5000);
                                } catch { }
                            }
                            catch { }
                            try { File.Delete(apoTemp); } catch { }
                        }
                    }

                    // Register APO on all active render devices — no Configurator needed
                    Report(worker, 0.96f, "Configuring EQ on your audio devices...");
                    try { RegisterAPOOnAllDevices(); } catch { }

                    // Restart audio service so APO loads immediately — no reboot needed
                    Report(worker, 0.98f, "Activating EQ...");
                    try { RestartAudioService(); } catch { }

                    // ── Whisper AI ──
                    // Extracts whisper-cli.exe and its runtime DLLs to %AppData%\Angry Audio\whisper\
                    // Model (ggml-tiny.bin) is downloaded automatically on first use.
                    Report(worker, 0.99f, "Installing Whisper AI...");
                    try
                    {
                        string whisperDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "Angry Audio", "whisper");
                        Directory.CreateDirectory(whisperDir);

                        var whisperFiles = new[] {
                            new[] { "whisper.exe",      "whisper-cli.exe" },
                            new[] { "whisper_dll.dll",  "whisper.dll"     },
                            new[] { "ggml.dll",         "ggml.dll"        },
                            new[] { "ggml_base.dll",    "ggml-base.dll"   },
                            new[] { "ggml_cpu.dll",     "ggml-cpu.dll"    },
                        };
                        foreach (var f in whisperFiles)
                        {
                            byte[] data = GetResource(f[0]);
                            if (data != null)
                                File.WriteAllBytes(Path.Combine(whisperDir, f[1]), data);
                        }
                    }
                    catch { }

                    Report(worker, 1.0f, "Installation complete!");
                    System.Threading.Thread.Sleep(300);
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }
            };
            worker.ProgressChanged += (s, e) =>
            {
                _progress = e.ProgressPercentage / 100f;
                _status = (string)e.UserState;
                Invalidate();
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                _installing = false;
                if (e.Error != null) _error = e.Error.Message;
                else if (_error == "") { _installed = true; _progress = 1f; }
                Invalidate();
            };
            worker.RunWorkerAsync();
        }

        // Registers Equalizer APO as the GFX APO on every active render device,
        // mirroring what the Configurator does — but silently and automatically.
        void RegisterAPOOnAllDevices()
        {
            // Equalizer APO's registered GFX/LFX CLSID (from its setup)
            const string APO_CLSID = "{E4B8D70A-CB30-4AE6-8596-E2DCFC84FCBC}";
            const string RENDER_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
            const string CHILD_APO_KEY = @"SOFTWARE\EqualizerAPO\Child APOs";
            // FxProperties value names (Windows Vista/7 and 8.1+ keys)
            string[] LFX_GFX_VALS = new string[] {
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1",  // LFX (Vista+)
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2",  // GFX (Vista+)
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5",  // LFX (Win8.1+)
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6",  // GFX (Win8.1+)
            };

            using (var renderKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RENDER_KEY, false))
            {
                if (renderKey == null) return;
                foreach (string deviceGuid in renderKey.GetSubKeyNames())
                {
                    try
                    {
                        // Check device state: 1 = DEVICE_STATE_ACTIVE
                        using (var devKey = renderKey.OpenSubKey(deviceGuid, false))
                        {
                            if (devKey == null) continue;
                            object stateObj = devKey.GetValue("DeviceState");
                            if (stateObj == null) continue;
                            int state = 0;
                            try { state = Convert.ToInt32(stateObj); } catch { continue; }
                            if (state != 1) continue; // skip disabled/unplugged devices
                        }

                        string fxPath = RENDER_KEY + @"\" + deviceGuid + @"\FxProperties";
                        string childPath = CHILD_APO_KEY + @"\" + deviceGuid;

                        using (var fxKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(fxPath, true))
                        {
                            if (fxKey == null) continue;

                            // Save existing values under EqualizerAPO\Child APOs (like the Configurator does)
                            using (var childKey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(childPath, true))
                            {
                                if (childKey != null)
                                {
                                    foreach (string valName in LFX_GFX_VALS)
                                    {
                                        object existing = fxKey.GetValue(valName);
                                        if (existing != null && childKey.GetValue(valName) == null)
                                            childKey.SetValue(valName, existing, Microsoft.Win32.RegistryValueKind.String);
                                    }
                                }
                            }

                            // Register APO on all applicable slots
                            foreach (string valName in LFX_GFX_VALS)
                                fxKey.SetValue(valName, APO_CLSID, Microsoft.Win32.RegistryValueKind.String);

                            // Set processing mode to default (required on Win8.1+)
                            string[] processingModeVals = new string[] {
                                "{d3993a3f-99c2-4402-b5ec-a92a0367664b},5",
                                "{d3993a3f-99c2-4402-b5ec-a92a0367664b},6"
                            };
                            const string DEFAULT_MODE = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
                            foreach (string valName in processingModeVals)
                            {
                                object existing = fxKey.GetValue(valName);
                                if (existing == null)
                                    fxKey.SetValue(valName, DEFAULT_MODE, Microsoft.Win32.RegistryValueKind.String);
                            }
                        }
                    }
                    catch { /* skip this device, try next */ }
                }
            }
        }

        void RestartAudioService()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c net stop audiosrv /yes && net start audiosrv",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var proc = Process.Start(psi))
                proc.WaitForExit(15000);
        }

        void Report(System.ComponentModel.BackgroundWorker w, float pct, string status)
        {
            w.ReportProgress((int)(pct * 100), status);
        }

        void DoUninstall()
        {
            _uninstalling = true;
            _progress = 0;
            _status = "Preparing...";
            Invalidate();

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += (s, e) =>
            {
                try
                {
                    Report(worker, 0.1f, "Closing Angry Audio...");
                    try
                    {
                        foreach (var proc in Process.GetProcessesByName("Angry Audio"))
                            try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(500);

                    Report(worker, 0.3f, "Removing files...");
                    if (Directory.Exists(_installDir))
                    {
                        try { File.Delete(Path.Combine(_installDir, "Angry Audio.exe")); } catch { }
                        try { File.Delete(Path.Combine(_installDir, "Angry Audio.ico")); } catch { }
                        try { File.Delete(Path.Combine(_installDir, "Uninstall.exe")); } catch { }
                        // Remove dir if empty
                        try
                        {
                            if (Directory.GetFiles(_installDir).Length == 0 && Directory.GetDirectories(_installDir).Length == 0)
                                Directory.Delete(_installDir);
                        }
                        catch { }
                    }
                    System.Threading.Thread.Sleep(300);

                    Report(worker, 0.45f, "Cleaning up downloaded models...");
                    try
                    {
                        // Remove ALL AppData — whisper models/CLI, settings, logs
                        string appDataDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "Angry Audio");
                        if (Directory.Exists(appDataDir))
                            Directory.Delete(appDataDir, true); // recursive — nukes everything
                    }
                    catch { }
                    System.Threading.Thread.Sleep(200);

                    Report(worker, 0.5f, "Removing shortcuts...");
                    try
                    {
                        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "Angry Audio");
                        if (Directory.Exists(startMenu))
                        {
                            try { File.Delete(Path.Combine(startMenu, "Angry Audio.lnk")); } catch { }
                            try { Directory.Delete(startMenu); } catch { }
                        }
                    }
                    catch { }
                    try
                    {
                        string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Angry Audio.lnk");
                        if (File.Exists(desktopLnk)) File.Delete(desktopLnk);
                    }
                    catch { }
                    System.Threading.Thread.Sleep(200);

                    Report(worker, 0.7f, "Removing startup entry...");
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(STARTUPKEY, true))
                            if (key != null) key.DeleteValue(STARTUPVAL, false);
                    }
                    catch { }
                    System.Threading.Thread.Sleep(200);

                    Report(worker, 0.9f, "Cleaning registry...");
                    try { Registry.LocalMachine.DeleteSubKey(UNINSTKEY, false); } catch { }
                    System.Threading.Thread.Sleep(200);

                    Report(worker, 1.0f, "Uninstall complete!");
                    System.Threading.Thread.Sleep(300);
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }
            };
            worker.ProgressChanged += (s, e) =>
            {
                _progress = e.ProgressPercentage / 100f;
                _status = (string)e.UserState;
                Invalidate();
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                _uninstalling = false;
                if (e.Error != null) _error = e.Error.Message;
                else if (_error == "") { _uninstalled = true; _progress = 1f; }
                Invalidate();
            };
            worker.RunWorkerAsync();
        }

        void CreateShortcut(string lnkPath, string targetPath, string iconPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic lnk = shell.CreateShortcut(lnkPath);
                lnk.TargetPath = targetPath;
                lnk.IconLocation = iconPath + ",0";
                lnk.WorkingDirectory = Path.GetDirectoryName(targetPath);
                lnk.Save();
                Marshal.ReleaseComObject(lnk);
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        // ===== PAINT — fonts at natural pt sizes, S() for layout only =====

        void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width, h = ClientSize.Height;

            // Background stars
            _stars.Paint(g, w, h);

            // Shooting stars painted AFTER measuring card so they pass behind it
            // (deferred below after card is drawn)

            // --- Dynamic card layout: measure content, then size card to fit ---
            int cx = S(24);
            int cy = S(16);
            int cw = w - S(48);
            int pad = S(20);
            int contentX = cx + pad;
            int contentW = cw - pad * 2;

            // Header: mascot + title + version + separator
            // Mascot is at cy+S(16), height S(64), bottom at cy+S(80)
            // Separator must be BELOW the mascot
            int sepY = cy + S(94);
            int contentY = sepY + S(12);

            // Measure content bottom based on current state
            int contentBottom = contentY;
            int btnW = S(160);
            int btnH = S(38);

            // Reset hit-test rects
            _btnRect1 = Rectangle.Empty;
            _btnRect2 = Rectangle.Empty;
            _browseRect = Rectangle.Empty;

            if (_error != "" && !_uninstallMode)
            {
                // Title height
                contentBottom = contentY + S(24) + S(8);
                // Error message
                using (var f = new Font("Segoe UI", 9f))
                {
                    var msgSize = g.MeasureString(_error, f, contentW);
                    contentBottom += (int)Math.Ceiling(msgSize.Height);
                }
                contentBottom += S(16);
                // Two buttons side by side
                int twoBtnW = S(130);
                _btnRect1 = new Rectangle(w / 2 - twoBtnW - S(6), contentBottom, twoBtnW, btnH);
                _btnRect2 = new Rectangle(w / 2 + S(6), contentBottom, twoBtnW, btnH);
                contentBottom += btnH;
            }
            else if (_uninstallMode)
            {
                if (_error != "")
                {
                    contentBottom = contentY + S(60);
                    _btnRect1 = new Rectangle(w / 2 - btnW / 2, contentBottom, btnW, btnH);
                    contentBottom = _btnRect1.Bottom;
                }
                else if (_uninstalled)
                {
                    contentBottom = contentY + S(50);
                    contentBottom += S(14);
                    _btnRect1 = new Rectangle(w / 2 - btnW / 2, contentBottom, btnW, btnH);
                    contentBottom = _btnRect1.Bottom;
                }
                else if (_uninstalling)
                {
                    contentBottom = contentY + S(50);
                    int minCardBottom = cy + S(180);
                    if (contentBottom + pad < minCardBottom)
                        contentBottom = minCardBottom - pad;
                }
                else
                {
                    // Confirm screen
                    contentBottom = contentY + S(60);
                    int twoBtnW = S(130);
                    _btnRect1 = new Rectangle(w / 2 - twoBtnW - S(6), contentBottom, twoBtnW, btnH);
                    _btnRect2 = new Rectangle(w / 2 + S(6), contentBottom, twoBtnW, btnH);
                    contentBottom += btnH;
                }
            }
            else if (!_installed && !_installing)
            {
                // Description
                string desc = "Your audio, your rules.\n\nAngry Audio guards your privacy by keeping your mic muted when you're not using it. It prevents apps from silently changing your audio levels and auto-mutes when you step away.";
                using (var f = new Font("Segoe UI", 9.5f))
                {
                    var descSize = g.MeasureString(desc, f, contentW);
                    contentBottom = contentY + (int)Math.Ceiling(descSize.Height);
                }
                // Path row: label + path field + browse button
                contentBottom += S(6);
                int pathRowY = contentBottom;
                int browseW = S(70);
                int browseH = S(24);
                _browseRect = new Rectangle(contentX + contentW - browseW, pathRowY, browseW, browseH);
                contentBottom += browseH + S(8);
                // Button
                _btnRect1 = new Rectangle(w / 2 - btnW / 2, contentBottom, btnW, btnH);
                contentBottom = _btnRect1.Bottom;
            }
            else if (_installing)
            {
                // Status + bar + percentage — need enough visual space
                contentBottom = contentY + S(50);
                // Ensure a minimum card height so it doesn't collapse
                int minCardBottom = cy + S(180);
                if (contentBottom + pad < minCardBottom)
                    contentBottom = minCardBottom - pad;
            }
            else if (_installed)
            {
                // Title + subtitle
                contentBottom = contentY + S(50);
                // Button
                contentBottom += S(14);
                _btnRect1 = new Rectangle(w / 2 - btnW / 2, contentBottom, btnW, btnH);
                contentBottom = _btnRect1.Bottom;
            }

            // Card wraps everything: card bottom = content bottom + padding
            int ch = (contentBottom + pad) - cy;
            // Ensure card doesn't overlap footer separator
            int maxCardBottom = h - S(36);
            if (cy + ch > maxCardBottom) ch = maxCardBottom - cy;

            // Draw card with frosted glass — stars already painted on background,
            // just apply glass tint + dimmed stars on top (same visual as Options)
            using (var path = RoundRectPath(new Rectangle(cx, cy, cw, ch), S(10)))
            {
                // Frosted glass card — single call handles tint + dim stars
                _stars.PaintGlassTint(g, w, h, path);
                // Border
                using (var pen = new Pen(BDR))
                    g.DrawPath(pen, path);
            }
            // Top accent glow line
            using (var p = new Pen(Color.FromArgb(30, ACC.R, ACC.G, ACC.B), 1.5f))
                g.DrawLine(p, cx + S(16), cy, cx + cw - S(16), cy);

            // (shooting stars already painted by _stars.PaintBackground above)

            // Mascot
            try { AngryAudio.Mascot.DrawMascot(g, cx + S(20), cy + S(16), S(64)); } catch { }

            // Title — premium "Angry Audio" with version badge
            int tx = cx + S(94);
            using (var f = new Font("Segoe UI", 18f, FontStyle.Bold))
            {
                var szA = g.MeasureString("Angry ", f);
                g.DrawString("Angry", f, new SolidBrush(TXT), tx, cy + S(22));
                g.DrawString("Audio", f, new SolidBrush(ACC), tx + szA.Width - S(5), cy + S(22));
            }

            // Version badge — pill with subtle bg
            string verText = "v" + _version;
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                var vs = g.MeasureString(verText, f);
                int vx = tx, vy = cy + S(58);
                int vw = (int)vs.Width + S(10);
                int vh = (int)vs.Height + S(2);
                using (var pill = MakePillBar(vx, vy, vw, vh, vh / 2))
                {
                    using (var b = new SolidBrush(Color.FromArgb(ACC.R / 10, ACC.G / 10, ACC.B / 10)))
                        g.FillPath(b, pill);
                    using (var p = new Pen(Color.FromArgb(ACC.R / 4, ACC.G / 4, ACC.B / 4)))
                        g.DrawPath(p, pill);
                }
                g.DrawString(verText, f, new SolidBrush(Color.FromArgb(ACC.R / 2, ACC.G / 2, ACC.B / 2)), vx + S(5), vy + S(1));
            }

            // Separator
            using (var pen = new Pen(BDR))
                g.DrawLine(pen, cx + S(16), sepY, cx + cw - S(16), sepY);

            // --- Draw state-specific content ---

            if (_uninstallMode)
            {
                if (_error != "")
                {
                    using (var f = new Font("Segoe UI", 10f, FontStyle.Bold))
                        g.DrawString("Uninstall Failed", f, new SolidBrush(ERR), contentX, contentY);
                    using (var f = new Font("Segoe UI", 9f))
                        g.DrawString(_error, f, new SolidBrush(TXT2), new RectangleF(contentX, contentY + S(28), contentW, S(100)));
                    DrawButton(g, _btnRect1, "Close", ERR, false, _hoverBtn1);
                }
                else if (_uninstalled)
                {
                    using (var f = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                        g.DrawString("\u2714  Uninstall Complete", f, new SolidBrush(GREEN), contentX, contentY);
                    using (var f = new Font("Segoe UI", 9f))
                        g.DrawString("Angry Audio has been removed from your computer.", f, new SolidBrush(TXT2), contentX, contentY + S(28));
                    DrawButton(g, _btnRect1, "Close", ACC, false, _hoverBtn1);
                }
                else if (_uninstalling)
                {
                    using (var f = new Font("Segoe UI", 9f))
                        g.DrawString(_status, f, new SolidBrush(TXT2), contentX, contentY + S(6));
                    int barY = contentY + S(34);
                    int pctTextW = S(40);
                    int barW = contentW - pctTextW;
                    int barH = S(6);
                    int barR = barH / 2;
                    using (var trackPath = MakePillBar(contentX, barY, barW, barH, barR))
                    using (var brush = new SolidBrush(Color.FromArgb(30, 30, 36)))
                        g.FillPath(brush, trackPath);
                    if (_progress > 0)
                    {
                        int fillW = Math.Max(barH, (int)(barW * Math.Min(1f, _progress)));
                        using (var fillPath = MakePillBar(contentX, barY, fillW, barH, barR))
                        using (var brush = new LinearGradientBrush(new Point(contentX, barY), new Point(contentX + fillW, barY), ERR, Color.FromArgb(Math.Min(255, ERR.R + 30), Math.Min(255, ERR.G + 20), ERR.B)))
                            g.FillPath(brush, fillPath);
                    }
                    using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                        g.DrawString((int)(_progress * 100) + "%", f, new SolidBrush(ERR), contentX + barW + S(6), barY - S(2));
                }
                else
                {
                    // Confirm screen
                    using (var f = new Font("Segoe UI", 10f, FontStyle.Bold))
                        g.DrawString("Remove Angry Audio?", f, new SolidBrush(TXT), contentX, contentY);
                    using (var f = new Font("Segoe UI", 9f))
                        g.DrawString("This will remove the application, shortcuts, downloaded\nmodels, settings, and all associated data.", f, new SolidBrush(TXT2), new RectangleF(contentX, contentY + S(24), contentW, S(40)));
                    DrawButton(g, _btnRect1, "Uninstall", ERR, false, _hoverBtn1);
                    DrawButton(g, _btnRect2, "Cancel", Color.FromArgb(100, 100, 100), true, _hoverBtn2);
                }
            }
            else if (_error != "")
            {
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold))
                    g.DrawString("Installation Failed", f, new SolidBrush(ERR), contentX, contentY);

                int errTextY = contentY + S(24) + S(8);
                using (var f = new Font("Segoe UI", 9f))
                    g.DrawString(_error, f, new SolidBrush(TXT2), new RectangleF(contentX, errTextY, contentW, S(200)));

                DrawButton(g, _btnRect1, "Retry", ACC, false, _hoverBtn1);
                DrawButton(g, _btnRect2, "Close", Color.FromArgb(140, 140, 140), true, _hoverBtn2);
            }
            else if (!_installed && !_installing)
            {
                string desc = "Your audio, your rules.\n\nAngry Audio guards your privacy by keeping your mic muted when you're not using it. It prevents apps from silently changing your audio levels and auto-mutes when you step away.";
                float descBottom;
                using (var f = new Font("Segoe UI", 9.5f))
                {
                    var descSize = g.MeasureString(desc, f, contentW);
                    g.DrawString(desc, f, new SolidBrush(TXT2), new RectangleF(contentX, contentY, contentW, descSize.Height + S(4)));
                    descBottom = contentY + descSize.Height;
                }

                // Path field with Browse button
                int pathRowY = (int)(descBottom + S(6));
                int browseW = S(70);
                int browseH = S(24);
                int pathFieldW = contentW - browseW - S(6);

                // Dark path field background
                using (var path2 = RoundRectPath(new Rectangle(contentX, pathRowY, pathFieldW, browseH), S(4)))
                {
                    using (var fb = new SolidBrush(Color.FromArgb(12, 12, 16)))
                        g.FillPath(fb, path2);
                    using (var bp = new Pen(Color.FromArgb(40, 40, 48)))
                        g.DrawPath(bp, path2);
                }
                // Path text inside field
                using (var f = new Font("Segoe UI", 7.5f))
                using (var brush = new SolidBrush(TXT2))
                {
                    var clip = g.ClipBounds;
                    g.SetClip(new Rectangle(contentX + S(6), pathRowY, pathFieldW - S(12), browseH));
                    g.DrawString(_installDir, f, brush, contentX + S(6), pathRowY + S(5));
                    g.SetClip(clip);
                }

                // Browse button
                DrawButton(g, _browseRect, "Browse...", Color.FromArgb(100, 140, 180), true, _hoverBrowse);

                DrawButton(g, _btnRect1, "Install", ACC, false, _hoverBtn1);
            }
            else if (_installing)
            {
                using (var f = new Font("Segoe UI", 9f))
                    g.DrawString(_status, f, new SolidBrush(TXT2), contentX, contentY + S(6));

                int barY = contentY + S(34);
                int pctTextW = S(40);
                int barW = contentW - pctTextW;
                int barH = S(8);
                int barR = barH / 2;
                // Track (pill-shaped)
                using (var trackPath = MakePillBar(contentX, barY, barW, barH, barR))
                using (var brush = new SolidBrush(Color.FromArgb(30, 30, 36)))
                    g.FillPath(brush, trackPath);
                if (_progress > 0)
                {
                    int fillW = Math.Max(barH, (int)(barW * Math.Min(1f, _progress)));
                    using (var fillPath = MakePillBar(contentX, barY, fillW, barH, barR))
                    using (var brush = new LinearGradientBrush(new Point(contentX, barY), new Point(contentX + fillW, barY), ACC, Color.FromArgb(Math.Min(255, ACC.R + 30), Math.Min(255, ACC.G + 20), Math.Min(255, ACC.B + 10))))
                        g.FillPath(brush, fillPath);
                    // Glow on progress tip
                    int tipX = contentX + fillW;
                    using (var brush = new SolidBrush(Color.FromArgb(30, ACC.R, ACC.G, ACC.B)))
                        g.FillEllipse(brush, tipX - S(6), barY - S(3), S(12), barH + S(6));
                }
                using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    g.DrawString((int)(_progress * 100) + "%", f, new SolidBrush(ACC), contentX + barW + S(6), barY - S(2));
            }
            else if (_installed)
            {
                using (var f = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                    g.DrawString("\u2714  Installation Complete", f, new SolidBrush(GREEN), contentX, contentY);
                using (var f = new Font("Segoe UI", 9f))
                    g.DrawString("Angry Audio is installed and ready to protect your audio.", f, new SolidBrush(TXT2), contentX, contentY + S(28));

                DrawButton(g, _btnRect1, "Launch", GREEN, false, _hoverBtn1);
            }

            // Footer with subtle separator
            using (var p = new Pen(Color.FromArgb(20, 255, 255, 255)))
                g.DrawLine(p, S(40), h - S(30), w - S(40), h - S(30));
            using (var f = new Font("Segoe UI", 7f))
            {
                string copy = "\u00A9 2026 Andrew Ganter";
                var sz = g.MeasureString(copy, f);
                g.DrawString(copy, f, new SolidBrush(TXT3), (w - sz.Width) / 2, h - S(20));
            }
        }

        void DrawButton(Graphics g, Rectangle rect, string text, Color color, bool outline = false, bool hover = false)
        {
            if (rect.IsEmpty) return;
            int cr = S(8);
            using (var path = RoundRectPath(rect, cr))
            {
                if (outline)
                {
                    // Outline style — very visible hover change
                    if (hover)
                    {
                        using (var brush = new SolidBrush(Color.FromArgb(color.R / 3, color.G / 3, color.B / 3)))
                            g.FillPath(brush, path);
                        using (var pen = new Pen(color, 2f))
                            g.DrawPath(pen, path);
                    }
                    else
                    {
                        using (var brush = new SolidBrush(Color.FromArgb(color.R / 8, color.G / 8, color.B / 8)))
                            g.FillPath(brush, path);
                        using (var pen = new Pen(Color.FromArgb(color.R / 3, color.G / 3, color.B / 3), 1.5f))
                            g.DrawPath(pen, path);
                    }
                }
                else
                {
                    // Solid fill with pulsing color (matching Options footer)
                    float phase = _orbitPhase;
                    float pulse = (float)((Math.Sin(phase * 0.8) + 1.0) / 2.0);
                    int lift = hover ? 45 : 0;
                    int pr = (int)(Math.Min(255, color.R * 0.3 + color.R * 0.7 * pulse + lift));
                    int pg = (int)(Math.Min(255, color.G * 0.3 + color.G * 0.7 * pulse + lift));
                    int pb = (int)(Math.Min(255, color.B * 0.3 + color.B * 0.7 * pulse + lift));
                    Color top = Color.FromArgb(Math.Min(255, pr + 15), Math.Min(255, pg + 15), Math.Min(255, pb + 15));
                    Color bot = Color.FromArgb(pr, pg, pb);
                    using (var brush = new LinearGradientBrush(
                        new Point(rect.X, rect.Y), new Point(rect.X, rect.Bottom), top, bot))
                        g.FillPath(brush, path);
                    // Top highlight
                    using (var pen = new Pen(Color.FromArgb(hover ? 100 : 30, 255, 255, 255)))
                        g.DrawLine(pen, rect.X + S(12), rect.Y + 1, rect.Right - S(12), rect.Y + 1);
                    if (hover)
                        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f))
                            g.DrawPath(pen, path);
                    // Orbiting star effect (matching Options/Welcome buttons)
                    var saved = g.Save();
                    g.TranslateTransform(rect.X, rect.Y);
                    DarkTheme.PaintOrbitingStar(g, rect.Width, rect.Height, phase, cr);
                    g.Restore(saved);
                }
                float fontSize = rect.Height < S(30) ? 8f : 10.5f;
                Color textColor = outline ? color : Color.White;
                using (var f = new Font("Segoe UI", fontSize, FontStyle.Bold))
                using (var brush = new SolidBrush(textColor))
                {
                    var sz = g.MeasureString(text, f);
                    g.DrawString(text, f, brush,
                        rect.X + (rect.Width - sz.Width) / 2,
                        rect.Y + (rect.Height - sz.Height) / 2);
                }
            }
        }

        GraphicsPath MakePillBar(int x, int y, int w, int h, int r) {
            var p = new GraphicsPath();
            if (w < h) w = h;
            p.AddArc(x, y, h, h, 90, 180);
            p.AddArc(x + w - h, y, h, h, 270, 180);
            p.CloseFigure();
            return p;
        }

        GraphicsPath RoundRectPath(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_installing || _uninstalling) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (_paintTimer != null) _paintTimer.Dispose(); if (_stars != null) _stars.Dispose(); }
            base.Dispose(disposing);
        }
    }

    class StyledMsgBox : Form
    {
        static readonly Color BG = Color.FromArgb(12, 12, 12);
        static readonly Color BDR = Color.FromArgb(38, 38, 38);
        static readonly Color TXT = Color.FromArgb(220, 220, 220);
        static readonly Color TXT2 = Color.FromArgb(160, 160, 160);
        static readonly Color ACC = Color.FromArgb(74, 158, 204);
        static readonly Color BTN_HOVER = Color.FromArgb(94, 178, 224);

        public StyledMsgBox(string message)
        {
            Text = "Angry Audio Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);

            float dpi;
            using (var g = CreateGraphics()) dpi = g.DpiX / 96f;
            Func<int, int> S = v => (int)(v * dpi);

            ClientSize = new Size(S(380), S(200));

            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (stream != null) Icon = new Icon(stream);
            }
            catch { }
            AngryAudio.DarkTheme.DarkTitleBar(Handle);

            // Mascot — bigger so it's crisp
            var mascotPanel = new Panel { Size = new Size(S(64), S(64)), Location = new Point(S(158), S(10)), BackColor = Color.Transparent };
            mascotPanel.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                try { AngryAudio.Mascot.DrawMascot(e.Graphics, 0, 0, S(64)); } catch { }
            };
            Controls.Add(mascotPanel);

            // Title
            var title = new Label {
                Text = "Already Running",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = ACC, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(S(380), S(28)), Location = new Point(0, S(78))
            };
            Controls.Add(title);

            // Message
            var msg = new Label {
                Text = message,
                Font = new Font("Segoe UI", 9f),
                ForeColor = TXT2, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(S(340), S(30)), Location = new Point(S(20), S(110))
            };
            Controls.Add(msg);

            // OK button
            int btnW = S(120), btnH = S(32);
            var btn = new Panel { Location = new Point(S(130), S(152)), Size = new Size(btnW, btnH) };
            bool hovering = false;

            btn.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
                Color fill = hovering ? BTN_HOVER : ACC;
                using (var path = RoundRectPath(rect, S(6)))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var p = new Pen(ACC)) g.DrawPath(p, path);
                }
                using (var f = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("OK", f, b, new RectangleF(0, 0, btn.Width, btn.Height), sf);
                }
            };
            btn.MouseEnter += (s, e) => { hovering = true; btn.Invalidate(); };
            btn.MouseLeave += (s, e) => { hovering = false; btn.Invalidate(); };
            btn.MouseClick += (s, e) => Close();
        }

        static GraphicsPath RoundRectPath(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SilentUpdateForm — minimal dark popup for /update mode
    // Shows progress bar + status label, auto-installs, relaunches, exits.
    // ═══════════════════════════════════════════════════════════════════════
    class SilentUpdateForm : Form
    {
        static readonly Color BG  = Color.FromArgb(18, 18, 18);
        static readonly Color TXT = Color.FromArgb(220, 220, 220);
        static readonly Color ACC = AngryAudio.DarkTheme.Accent;
        static readonly Color BDR = Color.FromArgb(40, 40, 40);

        const string UNINSTKEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Angry Audio";

        float  _progress;
        string _status = "Updating...";
        string _installDir;
        string _version;
        float  _dpi = 1f;

        int S(int v) { return (int)(v * _dpi); }

        public SilentUpdateForm()
        {
            using (var g = CreateGraphics()) _dpi = g.DpiX / 96f;

            // Read existing install dir from registry
            _installDir = ReadRegistryInstallDir();
            _version    = GetEmbeddedVersion();

            // If no existing install found, abort — shouldn't happen during update
            if (string.IsNullOrEmpty(_installDir))
            {
                _installDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Angry Audio");
            }

            Text            = "Angry Audio Update";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = BG;
            DoubleBuffered  = true;
            AutoScaleMode   = AutoScaleMode.None;
            ClientSize      = new Size(S(350), S(120));

            // Center on primary screen
            var screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width  - ClientSize.Width)  / 2,
                screen.Top  + (screen.Height - ClientSize.Height) / 2);

            try { Icon = ExtractEmbeddedIcon(); } catch { }
            AngryAudio.DarkTheme.DarkTitleBar(Handle);

            // Apply rounded region
            try
            {
                using (var path = RoundRectPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), S(10)))
                    Region = new Region(path);
            }
            catch { }

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Paint += OnPaint;

            // Prevent closing during update
            FormClosing += PreventClose;

            // Start install on next tick so the form is visible first
            var startTimer = new Timer { Interval = 100 };
            startTimer.Tick += (s, e) => { startTimer.Stop(); startTimer.Dispose(); DoSilentUpdate(); };
            startTimer.Start();
        }

        string GetEmbeddedVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("AngryAudio.version.txt") ?? asm.GetManifestResourceStream("version.txt"))
                    if (stream != null)
                        using (var reader = new StreamReader(stream))
                            return reader.ReadToEnd().Trim();
            }
            catch { }
            return AngryAudio.AppVersion.Version;
        }

        string ReadRegistryInstallDir()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(UNINSTKEY))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("InstallLocation");
                        if (val != null)
                        {
                            string dir = val.ToString().Trim();
                            if (dir.Length > 0) return dir;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        Icon ExtractEmbeddedIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                if (stream != null) return new Icon(stream);
            return null;
        }

        byte[] GetResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var ms = new MemoryStream()) { stream.CopyTo(ms); return ms.ToArray(); }
            }
        }

        void DoSilentUpdate()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += (s, e) =>
            {
                try
                {
                    // 1. Kill running instances
                    Report(worker, 0.10f, "Closing Angry Audio...");
                    try
                    {
                        // Graceful shutdown via kill event
                        System.Threading.EventWaitHandle killEvent;
                        if (System.Threading.EventWaitHandle.TryOpenExisting("Global\\Angry_Audio_Kill_Event", out killEvent))
                        {
                            killEvent.Set();
                            killEvent.Dispose();
                            System.Threading.Thread.Sleep(1500);
                        }
                    }
                    catch { }
                    try
                    {
                        foreach (var proc in Process.GetProcessesByName("Angry Audio"))
                            try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(300);

                    // 2. Create install directory (should already exist)
                    Report(worker, 0.25f, "Preparing update...");
                    Directory.CreateDirectory(_installDir);

                    // 3. Overwrite exe
                    Report(worker, 0.40f, "Installing update...");
                    byte[] exeData = GetResource("app.exe");
                    if (exeData != null)
                        File.WriteAllBytes(Path.Combine(_installDir, "Angry Audio.exe"), exeData);
                    System.Threading.Thread.Sleep(200);

                    // 4. Overwrite icon
                    Report(worker, 0.55f, "Updating resources...");
                    byte[] icoData = GetResource("app.ico");
                    if (icoData != null)
                        File.WriteAllBytes(Path.Combine(_installDir, "Angry Audio.ico"), icoData);
                    System.Threading.Thread.Sleep(100);

                    // 5. Update uninstaller
                    Report(worker, 0.65f, "Updating uninstaller...");
                    try
                    {
                        string self = Assembly.GetExecutingAssembly().Location;
                        if (!string.IsNullOrEmpty(self) && File.Exists(self))
                            File.Copy(self, Path.Combine(_installDir, "Uninstall.exe"), true);
                    }
                    catch { }

                    // 6. Update registry version
                    Report(worker, 0.80f, "Updating registry...");
                    try
                    {
                        using (var key = Registry.LocalMachine.CreateSubKey(UNINSTKEY))
                        {
                            key.SetValue("DisplayVersion", _version);
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(200);

                    // 7. Done — relaunch
                    Report(worker, 1.0f, "Restarting...");
                    System.Threading.Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    // Best-effort — try to relaunch even if something failed
                    System.Diagnostics.Debug.WriteLine("Silent update error: " + ex.Message);
                }
            };
            worker.ProgressChanged += (s, e) =>
            {
                _progress = e.ProgressPercentage / 100f;
                _status   = (string)e.UserState;
                Invalidate();
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                // Relaunch app via explorer.exe (de-elevated)
                try
                {
                    string exe = Path.Combine(_installDir, "Angry Audio.exe");
                    if (File.Exists(exe))
                        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false });
                }
                catch { }

            // Allow closing now, then close
                FormClosing -= PreventClose;
                try { Close(); } catch { }
                Application.Exit();
            };
            worker.RunWorkerAsync();
        }

        void PreventClose(object sender, FormClosingEventArgs e) { e.Cancel = true; }

        void Report(System.ComponentModel.BackgroundWorker w, float pct, string status)
        {
            w.ReportProgress((int)(pct * 100), status);
        }

        void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;

            // Background with rounded corners
            using (var path = RoundRectPath(new Rectangle(0, 0, w - 1, h - 1), S(10)))
            {
                using (var br = new SolidBrush(BG))
                    g.FillPath(br, path);
                using (var pen = new Pen(BDR))
                    g.DrawPath(pen, path);
            }

            // Top accent line
            using (var p = new Pen(Color.FromArgb(60, ACC.R, ACC.G, ACC.B), 2f))
                g.DrawLine(p, S(20), 1, w - S(20), 1);

            // Mascot (small)
            int mascotSize = S(32);
            try { AngryAudio.Mascot.DrawMascot(g, S(16), S(16), mascotSize); } catch { }

            // Title: "Updating Angry Audio..."
            int tx = S(16) + mascotSize + S(10);
            using (var f = new Font("Segoe UI", 11f, FontStyle.Bold))
                g.DrawString("Updating Angry Audio", f, new SolidBrush(TXT), tx, S(16));

            // Status text
            using (var f = new Font("Segoe UI", 8.5f))
                g.DrawString(_status, f, new SolidBrush(Color.FromArgb(160, 160, 160)), tx, S(40));

            // Progress bar
            int barX = S(16);
            int barW = w - S(32);
            int barY = h - S(36);
            int barH = S(8);
            int barR = barH / 2;

            // Track
            using (var trackPath = MakePillBar(barX, barY, barW, barH, barR))
            using (var brush = new SolidBrush(Color.FromArgb(30, 30, 36)))
                g.FillPath(brush, trackPath);

            // Fill
            if (_progress > 0)
            {
                int fillW = Math.Max(barH, (int)(barW * Math.Min(1f, _progress)));
                using (var fillPath = MakePillBar(barX, barY, fillW, barH, barR))
                using (var brush = new LinearGradientBrush(
                    new Point(barX, barY), new Point(barX + fillW, barY),
                    ACC, Color.FromArgb(Math.Min(255, ACC.R + 30), Math.Min(255, ACC.G + 20), Math.Min(255, ACC.B + 10))))
                    g.FillPath(brush, fillPath);
            }

            // Percentage
            string pctStr = ((int)(_progress * 100)) + "%";
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                var sz = g.MeasureString(pctStr, f);
                g.DrawString(pctStr, f, new SolidBrush(Color.FromArgb(120, 120, 120)),
                    barX + barW - sz.Width, barY - S(14));
            }

            // Version badge at bottom right
            string ver = "v" + _version;
            using (var f = new Font("Segoe UI", 7f))
            {
                var sz = g.MeasureString(ver, f);
                g.DrawString(ver, f, new SolidBrush(Color.FromArgb(60, 60, 60)),
                    w - sz.Width - S(12), h - S(18));
            }
        }

        GraphicsPath MakePillBar(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            if (w < h) w = h;
            p.AddArc(x, y, h, h, 90, 180);
            p.AddArc(x + w - h, y, h, h, 270, 180);
            p.CloseFigure();
            return p;
        }

        GraphicsPath RoundRectPath(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
