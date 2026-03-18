// DictationManager.cs — Voice-to-text engine for Angry Audio.
//
// Engine 0 = Windows Built-in (Win+H)
//   Fires the Win+H keyboard shortcut to open/close the Windows 10/11 dictation bar.
//   Windows handles the popup, mic, transcription and typing entirely.
//   Push-to-Dictate: Win+H on key down (opens), Win+H on key up (closes).
//   Toggle:          Win+H on first press (opens), Win+H on second press (closes).
//
// Engine 1 = Whisper (offline, subprocess)
//   Records via WaveInRecorder → writes temp WAV → whisper-cli.exe → SendKeys result.
//   Shows DictationPreview overlay with live status + text preview.
//
// Zero registry writes. Zero audio-service changes.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AngryAudio
{
    public class DictationManager : IDisposable
    {
        // Win32
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT {
            public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT {
            public uint uMsg; public ushort wParamL; public ushort wParamH;
        }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_UNICODE = 0x0004;
        const uint KEYEVENTF_KEYUP = 0x0002;
        
        const byte VK_ESCAPE       = 0x1B;
        const byte VK_LWIN         = 0x5B;
        const byte VK_H            = 0x48;

        private readonly AudioSettings _audio;
        private readonly System.Threading.SynchronizationContext _uiContext;

        private System.Windows.Forms.Timer _pollTimer;

        // Independent key tracking for push-to-hold and toggle
        private bool _pushKeyWasDown;
        private bool _toggleKeyWasDown;

        // Whisper push-to-hold state
        private bool           _isRecording;
        private WaveInRecorder _recorder;
        private readonly object _recLock = new object();

        // Windows dictation toggle state
        private bool _winDictOpen;

        private int _captureStartTick;
        private int _lastCaptureStopTick;

        private bool _disposed;

        public event Action<string> StatusChanged;
        public event Action<string> TextReady;
        public Action PlayStartSound;
        public Action PlayStopSound;
        public Action DuckAudio;     // Lower system volume during dictation
        public Action RestoreAudio;  // Restore system volume when dictation ends
        public Action ShowPreview;   // Show dictation overlay instantly (called from UI thread)
        public Action HidePreview;   // Hide dictation overlay instantly

        /// <summary>True when dictation is actively recording or Windows dictation bar is open.</summary>
        public bool IsActive { get { return _isRecording || _winDictOpen; } }

        /// <summary>Static reference so PushToTalk can check dictation state without coupling.</summary>
        public static DictationManager Current { get; private set; }

        public DictationManager(AudioSettings audio)
        {
            if (audio == null) throw new ArgumentNullException("audio");
            _audio = audio;
            _uiContext = System.Threading.SynchronizationContext.Current;
            Current = this;
        }

        public void Start()
        {
            StopPollTimer();
            bool whisper      = DictationManager.IsWhisperEngine(_audio.DictationEngine);
            bool canPush      = whisper;
            bool hasPushKey   = _audio.DictationEnabled       && canPush && (_audio.DictationKey > 0 || _audio.DictationKey2 > 0);
            bool hasToggleKey = _audio.DictationToggleEnabled && (_audio.DictationToggleKey > 0 || _audio.DictationToggleKey2 > 0);
            if (!hasPushKey && !hasToggleKey)
            {
                Logger.Info("DictationManager: not starting — neither mode is enabled or has a hotkey.");
                return;
            }
            _winDictOpen   = false;
            _pushKeyWasDown   = false;
            _toggleKeyWasDown = false;

            // Warm up the mic so it's instant when the hotkey is pressed
            if (whisper)
            {
                lock (_recLock)
                {
                    if (_recorder == null || !_recorder.IsRunning)
                    {
                        try { _recorder = new WaveInRecorder(); _recorder.WarmUp(); }
                        catch (Exception ex) { Logger.Error("DictationManager: failed to warm up recorder.", ex); _recorder = null; }
                    }
                }
            }

            _pollTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _pollTimer.Tick += PollTick;
            _pollTimer.Start();
            Logger.Info("DictationManager: started. PushKey=" + _audio.DictationKey + " ToggleKey=" + _audio.DictationToggleKey + " Engine=" + _audio.DictationEngine);

            // Proactively download engine files in background so user never waits
            if (whisper)
            {
                int modelIdx = Math.Max(0, Math.Min(WhisperModels.Length - 1, _audio.DictationWhisperModel));
                string modelFile = Path.Combine(WhisperDir, WhisperModels[modelIdx]);
                if (!File.Exists(modelFile))
                    ThreadPool.QueueUserWorkItem(_ => { try { DownloadWhisperModel(WhisperDir, WhisperModels[modelIdx]); } catch { } });
                string whisperExe = GetWhisperExe();
                if (!File.Exists(whisperExe))
                    ThreadPool.QueueUserWorkItem(_ => { try { DownloadCudaExe(); } catch { } });
            }
        }

        public void Stop()
        {
            StopPollTimer();
            if (!DictationManager.IsWhisperEngine(_audio.DictationEngine) && _winDictOpen) WinDictClose();
            AbortRecording();
            // Shutdown warm recorder
            lock (_recLock) { if (_recorder != null) { _recorder.Shutdown(); _recorder = null; } }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        public float CurrentPeakLevel {
            get {
                var r = _recorder;
                if (r != null && r.IsRunning && _isRecording) return r.CurrentPeakLevel;
                return 0f;
            }
        }

        [DllImport("user32.dll")] static extern short GetKeyState(int nVirtKey);
        const int VK_CAPS_LOCK = 0x14;
        private int _capsLockDictGuardTick; // Suppress CapsLock detection briefly after recording stops
        /// <summary>Clear CapsLock toggle state after a dictation recording ends.
        /// Only fires ONCE per recording stop, not continuously.</summary>
        void DictClearCapsLock()
        {
            bool dictUsesCaps = _audio.DictationKey  == VK_CAPS_LOCK || _audio.DictationKey2  == VK_CAPS_LOCK ||
                                _audio.DictationToggleKey == VK_CAPS_LOCK || _audio.DictationToggleKey2 == VK_CAPS_LOCK;
            if (!dictUsesCaps) return;
            // Guard: block CapsLock detection for 200ms to let the LL hook settle
            // and prevent phantom re-presses from any residual key events
            _capsLockDictGuardTick = Environment.TickCount + 200;
            // Clear the toggle if it got turned on
            if ((GetKeyState(VK_CAPS_LOCK) & 0x0001) != 0)
            {
                keybd_event(0x14, 0x45, 0x0001, UIntPtr.Zero);
                keybd_event(0x14, 0x45, 0x0001 | 0x0002, UIntPtr.Zero);
            }
        }

        private const int VK_SCROLL_LOCK = 0x91;
        private const int VK_NUM_LOCK = 0x90;

        private bool IsKeyDown(int vk) {
            if (vk == 0) return false;
            // During the post-recording guard window, ignore CapsLock to prevent
            // phantom re-presses from LL hook settling or ForceCapsLockOff injection
            if (vk == VK_CAPS_LOCK && _capsLockDictGuardTick > 0 && Environment.TickCount < _capsLockDictGuardTick) return false;
            
            // If PushToTalk is running its LL hook, it might be eating this key.
            // Check the exact key in the HookKeyHeld dictionary, which PushToTalk populates
            // for ALL keys it suppresses, including Dictation keys.
            if (PushToTalk.IsHookRunning && PushToTalk.HookKeyHeld.ContainsKey(vk) && PushToTalk.HookKeyHeld[vk]) {
                return true;
            }
            
            // Otherwise fall back to GetAsyncKeyState
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        void PollTick(object sender, EventArgs e)
        {
            bool whisper = DictationManager.IsWhisperEngine(_audio.DictationEngine);
            bool canPush = whisper;

            // ── Push-to-Hold (Whisper/Vosk only; only fires if push mode is enabled) ──
            int pushKey  = (_audio.DictationEnabled && canPush) ? _audio.DictationKey  : 0;
            int pushKey2 = (_audio.DictationEnabled && canPush) ? _audio.DictationKey2 : 0;
            if (pushKey > 0 || pushKey2 > 0)
            {
                // Use robust LL hook state to avoid hardware flickering
                bool holdDown = IsKeyDown(pushKey) || IsKeyDown(pushKey2);
                if (holdDown && !_pushKeyWasDown)
                {
                    if (!_isRecording && (Environment.TickCount - _lastCaptureStopTick > 100))
                    {
                        try { if (PlayStartSound != null) PlayStartSound(); } catch { }
                        try { if (DuckAudio != null) DuckAudio(); } catch { }
                        try { if (ShowPreview != null) ShowPreview(); } catch { }
                        _captureStartTick = Environment.TickCount;
                        WhisperStart();
                    }
                }
                else if (!holdDown && _pushKeyWasDown)
                {
                    try { if (HidePreview != null) HidePreview(); } catch { }
                    _lastCaptureStopTick = Environment.TickCount;
                    WhisperStopAndProcess();
                    DictClearCapsLock();
                }
                _pushKeyWasDown = holdDown;
            }
            else { _pushKeyWasDown = false; }

            int toggleKey  = _audio.DictationToggleEnabled ? _audio.DictationToggleKey  : 0;
            int toggleKey2 = _audio.DictationToggleEnabled ? _audio.DictationToggleKey2 : 0;
            if (toggleKey > 0 || toggleKey2 > 0)
            {
                // Use robust LL hook state to avoid hardware flickering
                bool holdDown = IsKeyDown(toggleKey) || IsKeyDown(toggleKey2);
                if (holdDown && !_toggleKeyWasDown)
                {
                    if (whisper)
                    {
                        if (_isRecording) { try { if (HidePreview != null) HidePreview(); } catch { } _lastCaptureStopTick = Environment.TickCount; WhisperStopAndProcess(); DictClearCapsLock(); }
                        else if (Environment.TickCount - _lastCaptureStopTick > 100) { try { if (PlayStartSound != null) PlayStartSound(); } catch { } try { if (DuckAudio != null) DuckAudio(); } catch { } try { if (ShowPreview != null) ShowPreview(); } catch { } _captureStartTick = Environment.TickCount; WhisperStart(); }
                    }
                    else // Windows engine
                    {
                        if (_winDictOpen) WinDictClose();
                        else { try { if (PlayStartSound != null) PlayStartSound(); } catch { } try { if (DuckAudio != null) DuckAudio(); } catch { } WinDictOpen(); }
                    }
                }
                _toggleKeyWasDown = holdDown;
            }
            else { _toggleKeyWasDown = false; }
        }

        // ── Windows Built-in ─────────────────────────────────────────────────

        void WinDictOpen()
        {
            try { Audio.SetMicMute(false); } catch { }
            Logger.Info("DictationManager: Win+H open");
            SendWinH();
            _winDictOpen = true;
        }

        void WinDictClose()
        {
            Logger.Info("DictationManager: Escape close");
            _winDictOpen = false;
            try { if (PlayStopSound != null) PlayStopSound(); } catch { }
            try { if (RestoreAudio != null) RestoreAudio(); } catch { }
            // Send Escape to close the dictation bar.
            // Do NOT send Win+H again — it's a toggle at OS level and will reopen it.
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    Thread.Sleep(80); // let key-up events from hotkey settle first
                    keybd_event(VK_ESCAPE, 0, 0,               UIntPtr.Zero);
                    Thread.Sleep(20);
                    keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
                catch (Exception ex) { Logger.Error("DictationManager: Escape close failed.", ex); }
            });
        }

        static void SendWinH()
        {
            // Run on a background thread — Thread.Sleep on the UI thread freezes the message pump.
            // Release any held modifier keys first — if user assigned e.g. Left Alt as the hotkey,
            // Alt would still be physically down when we fire Win+H → Alt+Win+H = wrong behavior.
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    ReleaseModifiers();
                    Thread.Sleep(20); // let releases settle
                    keybd_event(VK_LWIN, 0, 0,               UIntPtr.Zero);
                    keybd_event(VK_H,    0, 0,               UIntPtr.Zero);
                    Thread.Sleep(40);
                    keybd_event(VK_H,    0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
                catch (Exception ex) { Logger.Error("DictationManager: SendWinH failed.", ex); }
            });
        }

        // Release common modifier keys that might still be physically held.
        static void ReleaseModifiers()
        {
            byte[] mods = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }; // LShift RShift LCtrl RCtrl LAlt RAlt
            foreach (var k in mods)
                if ((GetAsyncKeyState(k) & 0x8000) != 0)
                    keybd_event(k, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ── Whisper ──────────────────────────────────────────────────────────

        void WhisperStart()
        {
            if (_isRecording) return;
            try { Audio.SetMicMute(false); } catch { }
            _captureStartTick = Environment.TickCount;
            try
            {
                lock (_recLock)
                {
                    if (_recorder == null || !_recorder.IsRunning)
                    {
                        // Fallback: create fresh recorder if warm one died
                        _recorder = new WaveInRecorder();
                        _recorder.WarmUp();
                    }
                    _recorder.MarkCaptureStart();
                    _isRecording = true;
                }
                RaiseStatus("Listening...");
                Logger.Info("DictationManager: capture started (instant).");
            }
            catch (Exception ex)
            {
                Logger.Error("DictationManager: failed to start recording.", ex);
                _isRecording = false;
            }
        }

        void WhisperStopAndProcess()
        {
            WaveInRecorder rec;
            lock (_recLock)
            {
                if (!_isRecording || _recorder == null) return;
                _isRecording = false;
                rec = _recorder;
                // Don't null out _recorder — it stays warm for next press
            }

            RaiseStatus("Processing...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] wav = rec.StopCapture();

                    int captureDuration = Environment.TickCount - _captureStartTick;
                    if (wav == null || captureDuration < 150)
                    {
                        Logger.Info("DictationManager: clip too short (" + captureDuration + "ms), skipping.");
                        RaiseStatus("Ready");
                        return;
                    }

                    // Diagnostic: check peak amplitude to verify we have real audio
                    int peak = 0;
                    for (int i = 44; i + 1 < wav.Length; i += 2)
                    {
                        int s = (short)(wav[i] | (wav[i+1] << 8));
                        if (s < 0) s = -s;
                        if (s > peak) peak = s;
                    }
                    Logger.Info("DictationManager: wav=" + wav.Length + " bytes, peak=" + peak + " (" + (peak * 100 / 32767) + "%)");

                    // Save diagnostic WAV for debugging
                    string diagWav = Path.Combine(Path.GetTempPath(), "aa_diag_last.wav");
                    try { File.WriteAllBytes(diagWav, wav); } catch { }

                    string text = TranscribeWhisper(wav);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        string trimmed = CleanTranscription(text);
                        try { if (TextReady != null) TextReady(trimmed); } catch { }
                        try { if (PlayStopSound != null) PlayStopSound(); } catch { }
                        
                        // Use Win32 SendInput to inject unicode string directly
                        // Bypasses WinForm's SendKeys.SendWait thread-queue focus drop issues
                        if (trimmed.Length > 0) {
                            try { InjectUnicodeString(trimmed); }
                            catch (Exception ex) { Logger.Error("DictationManager: SendInput failed.", ex); }
                        }
                        Logger.Info("DictationManager: typed " + trimmed.Length + " chars.");
                    }
                    else
                    {
                        Logger.Info("DictationManager: transcription returned empty.");
                    }
                }
                catch (Exception ex) { Logger.Error("DictationManager: Whisper processing failed.", ex); }
                finally { try { if (RestoreAudio != null) RestoreAudio(); } catch { } RaiseStatus("Ready"); }
            });
        }

        void AbortRecording()
        {
            lock (_recLock)
            {
                _isRecording = false;
                // If warm recorder is active, just stop the capture — don't tear down
                if (_recorder != null && _recorder.IsRunning) { try { _recorder.StopCapture(); } catch { } }
            }
            try { if (RestoreAudio != null) RestoreAudio(); } catch { }
        }


        static readonly string[] WhisperModels = { "ggml-base.bin", "ggml-medium.bin" };
        public static readonly string[] WhisperModelLabels = { "Balanced", "Genius" };
        public static readonly string[] WhisperModelDescs  = { "Fast — everyday dictation, good accuracy", "Slowest — best accuracy, ideal with GPU" };
        static readonly string WhisperModelBaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

        public static string WhisperDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Angry Audio", "whisper"); } }

        /// <summary>True if engine index is any Whisper variant. 1=CPU, 3=NVIDIA GPU.</summary>
        public static bool IsWhisperEngine(int engine) { return engine == 1 || engine == 2; }

        /// <summary>Check if a specific model index is already downloaded.</summary>
        public static bool IsModelDownloaded(int modelIdx)
        {
            modelIdx = Math.Max(0, Math.Min(WhisperModels.Length - 1, modelIdx));
            return File.Exists(Path.Combine(WhisperDir, WhisperModels[modelIdx]));
        }

        public static int ModelCount { get { return WhisperModels.Length; } }

        /// <summary>Get the correct whisper exe based on engine index. 1=CPU, 2=NVIDIA GPU.</summary>
        string GetWhisperExe()
        {
            string dir = WhisperDir;
            string cpuExe = Path.Combine(dir, "whisper-cli.exe");
            int engine = _audio.DictationEngine;
            if (engine == 2) // NVIDIA CUDA
            {
                string cudaExe = Path.Combine(dir, "cuda", "whisper-cli.exe");
                if (File.Exists(cudaExe))
                {
                    // Validate: the CUDA zip sometimes contains talk-llama instead of whisper-cli.
                    // Real whisper-cli supports "-m" flag; talk-llama uses "-mw" and has no "-m".
                    if (!_cudaValidated)
                    {
                        _cudaValidated = true;
                        try
                        {
                            var psi = new ProcessStartInfo {
                                FileName = cudaExe, Arguments = "--help",
                                UseShellExecute = false, CreateNoWindow = true,
                                RedirectStandardOutput = true, RedirectStandardError = true
                            };
                            string helpText = "";
                            using (var p = Process.Start(psi))
                            {
                                helpText = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                                p.WaitForExit(5000);
                            }
                            // Real whisper-cli has "--model FNAME" or "-m FNAME" in help text.
                            // talk-llama has "--model-whisper" instead.
                            if (helpText.Contains("--model-whisper") && !helpText.Contains("--model FNAME"))
                            {
                                Logger.Info("DictationManager: CUDA exe is talk-llama (wrong binary), deleting and using CPU.");
                                _cudaValid = false;
                                try { File.Delete(cudaExe); } catch { }
                            }
                            else
                            {
                                _cudaValid = true;
                            }
                        }
                        catch { _cudaValid = false; }
                    }
                    if (_cudaValid) return cudaExe;
                    Logger.Info("DictationManager: CUDA exe invalid, falling back to CPU.");
                }
                else
                {
                    ThreadPool.QueueUserWorkItem(_ => DownloadCudaExe());
                    Logger.Info("DictationManager: CUDA exe not found, falling back to CPU.");
                }
            }
            // CPU (engine 1) or fallback
            return cpuExe;
        }
        private bool _cudaValidated;
        private bool _cudaValid;

        /// <summary>Check if NVIDIA CUDA is available on this machine.</summary>
        public static bool HasNvidiaCuda()
        {
            try {
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return File.Exists(Path.Combine(sys32, "nvcuda.dll"));
            } catch { return false; }
        }

        /// <summary>True if the CUDA whisper exe is downloaded.</summary>
        public static bool IsCudaExeReady()
        {
            return File.Exists(Path.Combine(WhisperDir, "cuda", "whisper-cli.exe"));
        }

        static volatile bool _cudaDownloading = false;
        /// <summary>Download CUDA exe in background, safe to call from anywhere.</summary>
        public static void DownloadCudaExeInBackground() { DownloadCudaExe(); }
        static void DownloadCudaExe()
        {
            if (_cudaDownloading) return;
            _cudaDownloading = true;
            try
            {
                string dir = WhisperDir;
                string cudaDir = Path.Combine(dir, "cuda");
                string dest = Path.Combine(cudaDir, "whisper-cli.exe");
                if (File.Exists(dest)) return;
                Directory.CreateDirectory(cudaDir);
                // Download from whisper.cpp releases — CUDA 12.4 build (includes cuBLAS DLLs)
                string url = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.3/whisper-cublas-12.4.0-bin-x64.zip";
                Logger.Info("DictationManager: downloading CUDA exe from " + url);
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                string tmpZip = Path.Combine(dir, "cuda_download.zip");
                using (var wc = new System.Net.WebClient())
                    wc.DownloadFile(url, tmpZip);
                // Extract — look for whisper exe inside the zip
                try
                {
                    string extractDir = Path.Combine(dir, "cuda_temp");
                    if (Directory.Exists(extractDir)) try { Directory.Delete(extractDir, true); } catch { }
                    // Use PowerShell to extract zip (available on all Windows 10/11)
                    var psiZ = new ProcessStartInfo {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"Expand-Archive -Path '" + tmpZip.Replace("'","''") + "' -DestinationPath '" + extractDir.Replace("'","''") + "' -Force\"",
                        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (var proc = Process.Start(psiZ)) { proc.WaitForExit(120000); }
                    // Find the largest exe — 28KB ones are deprecated wrappers
                    string bestExe = null; long bestSize = 0;
                    foreach (var f in Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length > bestSize) { bestSize = fi.Length; bestExe = f; }
                    }
                    if (bestExe != null && bestSize > 100000) // real binary is always >100KB
                    {
                        File.Copy(bestExe, dest, true);
                        // Also copy any required DLLs from the same directory
                        foreach (var dll in Directory.GetFiles(Path.GetDirectoryName(bestExe), "*.dll"))
                            try { File.Copy(dll, Path.Combine(cudaDir, Path.GetFileName(dll)), true); } catch { }
                        Logger.Info("DictationManager: CUDA exe extracted from " + bestExe + " (" + bestSize + " bytes)");
                    }
                    else { Logger.Info("DictationManager: no suitable exe found in CUDA zip."); }
                    try { Directory.Delete(extractDir, true); } catch { }
                }
                catch (Exception ex2) { Logger.Error("DictationManager: CUDA zip extraction failed.", ex2); }
                TryDelete(tmpZip);
                Logger.Info("DictationManager: CUDA exe download complete.");
            }
            catch (Exception ex) { Logger.Error("DictationManager: CUDA exe download failed.", ex); }
            finally { _cudaDownloading = false; }
        }

        /// <summary>Strip ambient sound descriptions like (birds chirping) or [BLANK_AUDIO] from transcription.</summary>
        static string CleanTranscription(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            // Remove anything in parentheses: (birds chirping), (music), etc.
            string cleaned = System.Text.RegularExpressions.Regex.Replace(raw, @"\([^)]*\)", "");
            // Remove anything in square brackets: [BLANK_AUDIO], [Music], etc.
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[[^\]]*\]", "");
            // Collapse multiple spaces and trim
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        static readonly object _whisperLock = new object();

        string TranscribeWhisper(byte[] wav)
        {
            lock (_whisperLock)
            {
            string whisperDir = WhisperDir;
            string whisperExe = GetWhisperExe();
            int    modelIdx   = Math.Max(0, Math.Min(WhisperModels.Length - 1, _audio.DictationWhisperModel));
            string modelFile  = Path.Combine(whisperDir, WhisperModels[modelIdx]);

            if (!File.Exists(whisperExe)) { Logger.Info("DictationManager: whisper-cli.exe not found."); return null; }
            if (!File.Exists(modelFile))
            {
                ThreadPool.QueueUserWorkItem(_ => DownloadWhisperModel(whisperDir, WhisperModels[modelIdx]));
                Logger.Info("DictationManager: model not found — downloading silently.");
                return null;
            }

            string tmpWav  = TempWavPath();
            // -of tells whisper exactly where to write; whisper appends ".txt" to this path
            string outBase = Path.Combine(Path.GetTempPath(), "aa_dict_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string tmpTxt  = outBase + ".txt";

            // CUDA exe is now validated in GetWhisperExe — always the real whisper-cli
            try
            {
                File.WriteAllBytes(tmpWav, wav);
                var psi = new ProcessStartInfo {
                    FileName = whisperExe,
                    Arguments = string.Format("-m \"{0}\" -f \"{1}\" -otxt -of \"{2}\" -l en", modelFile, tmpWav, outBase),
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = whisperDir,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var sbStdout = new StringBuilder();
                var sbStderr = new StringBuilder();
                using (var proc = Process.Start(psi))
                {
                    proc.OutputDataReceived += (s2, e2) => { if (e2.Data != null) sbStdout.AppendLine(e2.Data); };
                    proc.ErrorDataReceived  += (s2, e2) => { if (e2.Data != null) sbStderr.AppendLine(e2.Data); };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    bool exited = proc.WaitForExit(30000);
                    if (!exited) { try { proc.Kill(); } catch { } Logger.Info("DictationManager: Whisper timed out."); return null; }
                    proc.WaitForExit();
                    int exitCode = proc.ExitCode;
                    Logger.Info("DictationManager: whisper-cli exit code=" + exitCode);
                    if (sbStderr.Length > 0) Logger.Info("DictationManager: whisper stderr=" + sbStderr.ToString().Trim());
                }

                // Primary: read the output file whisper wrote
                for (int retry = 0; retry < 5; retry++)
                {
                    if (File.Exists(tmpTxt))
                    {
                        string t = File.ReadAllText(tmpTxt, Encoding.UTF8).Trim();
                        while (t.StartsWith("[")) { int close = t.IndexOf(']'); if (close >= 0) t = t.Substring(close + 1).Trim(); else break; }
                        if (t.Length > 0) return t;
                        break;
                    }
                    Thread.Sleep(50);
                }

                // Fallback: parse stdout
                string stdout = sbStdout.ToString();
                if (stdout.Length > 0)
                {
                    Logger.Info("DictationManager: no .txt file, trying stdout. stdout=" + stdout.Trim());
                    var lines = stdout.Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
                    var textLines = new System.Collections.Generic.List<string>();
                    foreach (var ln in lines)
                    {
                        string s = ln.Trim();
                        if (s.Length == 0) continue;
                        if (s.StartsWith("whisper_") || s.StartsWith("system_info") || s.StartsWith("main:")) continue;
                        if (s.StartsWith("[")) { int close = s.IndexOf(']'); if (close >= 0) s = s.Substring(close + 1).Trim(); }
                        if (s.Length > 0) textLines.Add(s);
                    }
                    string joined = string.Join(" ", textLines).Trim();
                    if (joined.Length > 0) return joined;
                }

                Logger.Info("DictationManager: Whisper produced no usable output. Args=" +
                    "-m \"" + modelFile + "\" -f \"" + tmpWav + "\" -otxt -of \"" + outBase + "\"");
                return null;
            }
            catch (Exception ex) { Logger.Error("DictationManager: Whisper transcription failed.", ex); return null; }
            finally { TryDelete(tmpWav); TryDelete(tmpTxt); }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static event Action<string> OnTextInjected;

        static void InjectUnicodeString(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            // Fire event for internal app testing zones (bypasses WinForms VK_PACKET swallowing)
            if (OnTextInjected != null) OnTextInjected(text);

            // Each character requires a KEYDOWN and a KEYUP event
            INPUT[] inputs = new INPUT[text.Length * 2];
            
            for (int i = 0; i < text.Length; i++)
            {
                ushort ch = (ushort)text[i];
                
                // Key down
                inputs[i * 2] = new INPUT { type = INPUT_KEYBOARD };
                inputs[i * 2].u.ki = new KEYBDINPUT {
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE
                };
                
                // Key up
                inputs[i * 2 + 1] = new INPUT { type = INPUT_KEYBOARD };
                inputs[i * 2 + 1].u.ki = new KEYBDINPUT {
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
                };
            }
            
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static bool IsWhisperModelInstalled(string modelName)
        {
            string whisperDir = WhisperDir;
            return File.Exists(Path.Combine(whisperDir, modelName));
        }

        static string TempWavPath()
        {
            return Path.Combine(Path.GetTempPath(), "aa_dictation_" + Guid.NewGuid().ToString("N") + ".wav");
        }

        static void TryDelete(string path)
        { try { if (path != null && File.Exists(path)) File.Delete(path); } catch { } }

        void StopPollTimer()
        {
            var t = _pollTimer; _pollTimer = null;
            if (t != null) { try { t.Stop(); t.Dispose(); } catch { } }
        }

        void RaiseStatus(string msg) { try { if (StatusChanged != null) StatusChanged(msg); } catch { } }

        static bool _modelDownloading = false;
        static void DownloadWhisperModel(string whisperDir, string modelFileName)
        {
            if (_modelDownloading) return;
            _modelDownloading = true;
            try
            {
                string dest = Path.Combine(whisperDir, modelFileName);
                if (File.Exists(dest)) return;
                Directory.CreateDirectory(whisperDir);
                string url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + modelFileName;
                Logger.Info("DictationManager: downloading model from " + url);
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                using (var wc = new System.Net.WebClient())
                { string tmp = dest + ".part"; wc.DownloadFile(url, tmp); File.Move(tmp, dest); }
                Logger.Info("DictationManager: model download complete — " + modelFileName);
            }
            catch (Exception ex) { Logger.Error("DictationManager: model download failed.", ex); }
            finally { _modelDownloading = false; }
        }

        /// <summary>Download a model with progress callback (percentage 0-100). Blocks until complete.</summary>
        public static void DownloadWhisperModelWithProgress(int modelIdx, Action<int> onProgress)
        {
            if (_modelDownloading) return;
            modelIdx = Math.Max(0, Math.Min(WhisperModels.Length - 1, modelIdx));
            string modelFileName = WhisperModels[modelIdx];
            _modelDownloading = true;
            try
            {
                string dir = WhisperDir;
                string dest = Path.Combine(dir, modelFileName);
                if (File.Exists(dest)) { if (onProgress != null) onProgress(100); return; }
                Directory.CreateDirectory(dir);
                string url = WhisperModelBaseUrl + modelFileName;
                Logger.Info("DictationManager: downloading model from " + url);
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                string tmp = dest + ".part";
                using (var wc = new System.Net.WebClient())
                {
                    wc.DownloadProgressChanged += (s, e) => { if (onProgress != null) try { onProgress(e.ProgressPercentage); } catch { } };
                    var task = wc.DownloadFileTaskAsync(new Uri(url), tmp);
                    while (!task.IsCompleted) { Thread.Sleep(100); }
                    if (task.IsFaulted && task.Exception != null) throw task.Exception.GetBaseException();
                }
                if (File.Exists(tmp)) File.Move(tmp, dest);
                Logger.Info("DictationManager: model download complete — " + modelFileName);
                if (onProgress != null) onProgress(100);
            }
            catch (Exception ex) { Logger.Error("DictationManager: model download failed.", ex); }
            finally { _modelDownloading = false; }
        }
    }
}
