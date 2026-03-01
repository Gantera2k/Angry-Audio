using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AngryAudio
{
    static class Program
    {
        private static Mutex _mutex;
        private const string MutexName = "Global\\Angry_Audio_Mutex";
        private const string KillEventName = "Global\\Angry_Audio_Kill_Event";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // Parse command-line arguments
            bool argSettings = false;
            bool argPause = false;
            bool argKill = false;

            foreach (string arg in args)
            {
                string a = arg.ToLowerInvariant().Trim();
                if (a == "--settings") argSettings = true;
                else if (a == "--pause") argPause = true;
                else if (a == "--kill") argKill = true;
            }

            // Handle --kill: signal the running instance to shut down
            if (argKill)
            {
                try
                {
                    EventWaitHandle killEvent;
                    if (EventWaitHandle.TryOpenExisting(KillEventName, out killEvent))
                    {
                        killEvent.Set();
                        killEvent.Dispose();
                    }
                }
                catch { }
                return;
            }

            // Initialize UI framework once before any forms
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Logger.Initialize();
            Dpi.Init();

            // Single instance enforcement — auto-close previous instance
            bool createdNew;
            try
            {
                _mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
                _mutex = new Mutex(true, MutexName, out createdNew);
            }

            if (!createdNew)
            {
                // Try brief wait first — handles race conditions and stale mutexes
                try { if (_mutex.WaitOne(500, false)) createdNew = true; }
                catch (AbandonedMutexException) { createdNew = true; }
            }

            if (!createdNew)
            {
                // Silently kill the previous instance and take over
                try
                {
                    var killEvent = EventWaitHandle.OpenExisting(KillEventName);
                    killEvent.Set();
                    killEvent.Dispose();
                }
                catch { }

                // Wait for old instance to release mutex
                try { if (!_mutex.WaitOne(5000, false)) return; }
                catch (AbandonedMutexException) { /* old instance crashed, we own it */ }
            }

            try
            {
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

                Logger.Info("Angry Audio starting up.");

                using (var trayApp = new TrayApp(argSettings, argPause))
                {
                    Application.Run();
                }

                Logger.Info("Angry Audio shutting down cleanly.");
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal error during startup.", ex);
                DarkMessage.Show(
                    "Angry Audio encountered a fatal error and needs to close.\n\n" + ex.Message,
                    "Angry Audio — Error");
            }
            finally
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Logger.Error("Unhandled UI thread exception.", e.Exception);
            // Don't let the process become a zombie
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            Environment.Exit(1);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null) Logger.Error("Unhandled domain exception.", ex);
            else Logger.Error("Unhandled domain exception (non-Exception object).");
            // Don't let the process become a zombie
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            Environment.Exit(1);
        }
    }
}
