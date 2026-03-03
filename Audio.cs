using System;
using System.Runtime.InteropServices;

namespace AngryAudio
{
    // --- COM Interface Definitions ---

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(int access, out IntPtr properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint channelCount);
        int SetMasterVolumeLevel(float levelDB, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDB);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDB, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDB);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        int VolumeStepUp(ref Guid eventContext);
        int VolumeStepDown(ref Guid eventContext);
        int QueryHardwareSupport(out uint hardwareSupportMask);
        int GetVolumeRange(out float minDB, out float maxDB, out float incrementDB);
    }

    internal enum EDataFlow
    {
        eRender = 0,   // Speakers / output
        eCapture = 1,  // Microphone / input
        eAll = 2
    }

    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        int GetCount(out int numDevices);
        int Item(int deviceIndex, out IMMDevice device);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorClass { }

    // --- Per-App Audio Session Interfaces ---

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);
        int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr simpleVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        int GetCount(out int sessionCount);
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl methods
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2 methods
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceId);
        int GetProcessId(out uint processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid eventContext);
        int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>
    /// Represents an active audio session (an app producing sound).
    /// </summary>
    public class AudioSession
    {
        public uint ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string DisplayName { get; set; }
        public float Volume { get; set; }     // 0-100
        public bool Muted { get; set; }
    }

    // --- Public Audio Helper ---

    public static class Audio
    {
        private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        private static Guid _eventContext = Guid.Empty;

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, 
            [Out] System.Text.StringBuilder lpBaseName, int nSize);

        // --- Per-App Audio Sessions ---

        /// <summary>
        /// Get all active audio sessions (apps currently registered with the audio system).
        /// </summary>
        /// <summary>Diagnostic: total raw sessions from last scan.</summary>
        public static int LastScanTotalSessions;
        /// <summary>Diagnostic: sessions skipped (system, expired, errors) from last scan.</summary>
        public static int LastScanSkippedSessions;
        /// <summary>Diagnostic: detailed breakdown of why each session was skipped.</summary>
        public static string LastScanDiagnostics = "";

        public static System.Collections.Generic.List<AudioSession> GetAudioSessions()
        {
            var sessions = new System.Collections.Generic.List<AudioSession>();
            LastScanTotalSessions = 0;
            LastScanSkippedSessions = 0;
            LastScanDiagnostics = "";
            int skipNull = 0, skipQI = 0, skipPid = 0, skipSystem = 0, skipProc = 0, skipEx = 0;
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 sessionMgr = null;
            IAudioSessionEnumerator sessionEnum = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) { LastScanDiagnostics = "Failed to get default audio device (0x" + hr.ToString("X8") + ")"; return sessions; }

                object iface;
                hr = device.Activate(IID_IAudioSessionManager2, 23 /* CLSCTX_ALL */, IntPtr.Zero, out iface);
                if (hr != 0 || iface == null) { LastScanDiagnostics = "Failed to activate session manager (0x" + hr.ToString("X8") + ")"; return sessions; }
                sessionMgr = (IAudioSessionManager2)iface;

                hr = sessionMgr.GetSessionEnumerator(out sessionEnum);
                if (hr != 0 || sessionEnum == null) { LastScanDiagnostics = "Failed to get session enumerator (0x" + hr.ToString("X8") + ")"; return sessions; }

                int count;
                sessionEnum.GetCount(out count);
                LastScanTotalSessions = count;
                Logger.Debug("Audio session scan: found " + count + " total sessions.");

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctrl = null;

                    try
                    {
                        sessionEnum.GetSession(i, out ctrl);
                        if (ctrl == null) { Logger.Debug("Session " + i + ": null control"); skipNull++; continue; }

                        // Direct cast — .NET COM interop performs QI through the proper COM channel
                        IAudioSessionControl2 ctrl2 = ctrl as IAudioSessionControl2;
                        if (ctrl2 == null)
                        {
                            Logger.Debug("Session " + i + ": cast to IAudioSessionControl2 failed"); skipQI++;
                            continue;
                        }

                        uint pid;
                        int hrPid = ctrl2.GetProcessId(out pid);
                        if (hrPid != 0) { Logger.Debug("Session " + i + ": GetProcessId returned 0x" + hrPid.ToString("X8")); skipPid++; continue; }
                        if (pid == 0) { Logger.Debug("Session " + i + ": system sounds (pid=0)"); skipSystem++; continue; }

                        string procName = GetProcessName(pid);
                        if (string.IsNullOrEmpty(procName)) { Logger.Debug("Session " + i + ": pid " + pid + " — process not found"); skipProc++; continue; }

                        string displayName = null;
                        try { ctrl2.GetDisplayName(out displayName); } catch { }
                        if (string.IsNullOrEmpty(displayName)) displayName = procName;

                        // Direct cast for ISimpleAudioVolume
                        float vol = 0f;
                        bool muted = false;
                        try
                        {
                            var simpleVol = ctrl as ISimpleAudioVolume;
                            if (simpleVol != null)
                            {
                                simpleVol.GetMasterVolume(out vol);
                                simpleVol.GetMute(out muted);
                            }
                        }
                        catch (Exception vex) { Logger.Debug("Session " + i + ": volume read failed: " + vex.Message); }

                        Logger.Debug("Session " + i + ": " + procName + " (pid " + pid + ") vol=" + (int)(vol * 100f) + "% muted=" + muted);

                        sessions.Add(new AudioSession
                        {
                            ProcessId = pid,
                            ProcessName = procName,
                            DisplayName = displayName,
                            Volume = vol * 100f,
                            Muted = muted
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error reading audio session " + i, ex);
                        skipEx++;
                    }
                }
                LastScanSkippedSessions = LastScanTotalSessions - sessions.Count;
                // Build detailed diagnostics
                var diag = new System.Text.StringBuilder();
                if (skipNull > 0) diag.AppendLine("  Null controls: " + skipNull);
                if (skipQI > 0) diag.AppendLine("  QI failed: " + skipQI);
                if (skipPid > 0) diag.AppendLine("  GetProcessId failed: " + skipPid);
                if (skipSystem > 0) diag.AppendLine("  System sounds (pid=0): " + skipSystem);
                if (skipProc > 0) diag.AppendLine("  Process not found: " + skipProc);
                if (skipEx > 0) diag.AppendLine("  Exceptions: " + skipEx);
                LastScanDiagnostics = diag.ToString();
                Logger.Debug("Audio session scan complete: " + sessions.Count + " usable, " + LastScanSkippedSessions + " skipped.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to enumerate audio sessions.", ex);
            }
            finally
            {
                if (sessionEnum != null && Marshal.IsComObject(sessionEnum))
                    Marshal.ReleaseComObject(sessionEnum);
                if (sessionMgr != null && Marshal.IsComObject(sessionMgr))
                    Marshal.ReleaseComObject(sessionMgr);
                if (device != null && Marshal.IsComObject(device))
                    Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }

            return sessions;
        }

        /// <summary>
        /// Check if any app currently has an active capture session on ANY microphone.
        /// Returns list of process names using any mic, or empty list.
        /// </summary>
        public static System.Collections.Generic.List<string> GetActiveMicCaptureSessions()
        {
            var apps = new System.Collections.Generic.List<string>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;
            const int DEVICE_STATE_ACTIVE = 0x00000001;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out devices);
                if (hr != 0 || devices == null) return apps;

                int devCount;
                devices.GetCount(out devCount);

                for (int d = 0; d < devCount; d++)
                {
                    IMMDevice device = null;
                    IAudioSessionManager2 sessionMgr = null;
                    IAudioSessionEnumerator sessionEnum = null;

                    try
                    {
                        hr = devices.Item(d, out device);
                        if (hr != 0 || device == null) continue;

                        object iface;
                        hr = device.Activate(IID_IAudioSessionManager2, 23, IntPtr.Zero, out iface);
                        if (hr != 0 || iface == null) continue;
                        sessionMgr = (IAudioSessionManager2)iface;

                        hr = sessionMgr.GetSessionEnumerator(out sessionEnum);
                        if (hr != 0 || sessionEnum == null) continue;

                        int count;
                        sessionEnum.GetCount(out count);

                        for (int i = 0; i < count; i++)
                        {
                            IAudioSessionControl ctrl = null;
                            try
                            {
                                sessionEnum.GetSession(i, out ctrl);
                                if (ctrl == null) continue;

                                int state;
                                ctrl.GetState(out state);

                                IAudioSessionControl2 ctrl2 = ctrl as IAudioSessionControl2;
                                if (ctrl2 == null) continue;

                                uint pid;
                                if (ctrl2.GetProcessId(out pid) != 0 || pid == 0) continue;

                                string procName = GetProcessName(pid);

                                if (state != 1) continue;

                                if (!string.IsNullOrEmpty(procName) && !apps.Contains(procName))
                                    apps.Add(procName);
                            }
                            catch { }
                            finally
                            {
                                if (ctrl != null && Marshal.IsComObject(ctrl))
                                    Marshal.ReleaseComObject(ctrl);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        if (sessionEnum != null && Marshal.IsComObject(sessionEnum))
                            Marshal.ReleaseComObject(sessionEnum);
                        if (sessionMgr != null && Marshal.IsComObject(sessionMgr))
                            Marshal.ReleaseComObject(sessionMgr);
                        if (device != null && Marshal.IsComObject(device))
                            Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetActiveMicCaptureSessions failed", ex);
            }
            finally
            {
                if (devices != null && Marshal.IsComObject(devices))
                    Marshal.ReleaseComObject(devices);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }

            return apps;
        }

        /// <summary>
        /// Get all active CAPTURE sessions — apps currently using the microphone.
        /// Returns process names of apps with active mic capture sessions.
        /// </summary>
        public static System.Collections.Generic.List<AudioSession> GetMicCaptureSessions()
        {
            var sessions = new System.Collections.Generic.List<AudioSession>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;
            const int DEVICE_STATE_ACTIVE = 0x00000001;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out devices);
                if (hr != 0 || devices == null) return sessions;

                int devCount;
                devices.GetCount(out devCount);

                for (int d = 0; d < devCount; d++)
                {
                    IMMDevice device = null;
                    IAudioSessionManager2 sessionMgr = null;
                    IAudioSessionEnumerator sessionEnum = null;

                    try
                    {
                        hr = devices.Item(d, out device);
                        if (hr != 0 || device == null) continue;

                        object iface;
                        hr = device.Activate(IID_IAudioSessionManager2, 23, IntPtr.Zero, out iface);
                        if (hr != 0 || iface == null) continue;
                        sessionMgr = (IAudioSessionManager2)iface;

                        hr = sessionMgr.GetSessionEnumerator(out sessionEnum);
                        if (hr != 0 || sessionEnum == null) continue;

                        int count;
                        sessionEnum.GetCount(out count);

                        for (int i = 0; i < count; i++)
                        {
                            IAudioSessionControl ctrl = null;
                            IAudioSessionControl2 ctrl2 = null;
                            try
                            {
                                hr = sessionEnum.GetSession(i, out ctrl);
                                if (hr != 0 || ctrl == null) continue;

                                ctrl2 = ctrl as IAudioSessionControl2;
                                if (ctrl2 == null) continue;

                                uint pid;
                                ctrl2.GetProcessId(out pid);
                                if (pid == 0) continue; // system session

                                int state;
                                ctrl.GetState(out state);
                                if (state != 1) continue; // 1 = AudioSessionStateActive

                                string procName = GetProcessName(pid);
                                if (string.IsNullOrEmpty(procName) || procName == "Unknown") continue;

                                // Avoid duplicates
                                bool exists = false;
                                foreach (var s in sessions)
                                    if (s.ProcessName == procName) { exists = true; break; }
                                if (exists) continue;

                                sessions.Add(new AudioSession { ProcessId = pid, ProcessName = procName, DisplayName = procName });
                            }
                            finally
                            {
                                if (ctrl2 != null && ctrl2 != ctrl && Marshal.IsComObject(ctrl2)) Marshal.ReleaseComObject(ctrl2);
                                if (ctrl != null && Marshal.IsComObject(ctrl)) Marshal.ReleaseComObject(ctrl);
                            }
                        }
                    }
                    finally
                    {
                        if (sessionEnum != null && Marshal.IsComObject(sessionEnum)) Marshal.ReleaseComObject(sessionEnum);
                        if (sessionMgr != null && Marshal.IsComObject(sessionMgr)) Marshal.ReleaseComObject(sessionMgr);
                        if (device != null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (Exception ex) { Logger.Error("GetMicCaptureSessions failed", ex); }
            finally
            {
                if (devices != null && Marshal.IsComObject(devices)) Marshal.ReleaseComObject(devices);
                if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
            }

            return sessions;
        }

        /// <summary>
        /// Set volume for a specific process by name (e.g., "chrome", "discord").
        public static int SetAppVolume(string processName, float percent)
        {
            int affected = 0;
            string target = processName.ToLowerInvariant().Replace(".exe", "");
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 sessionMgr = null;
            IAudioSessionEnumerator sessionEnum = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) return 0;

                object iface;
                hr = device.Activate(IID_IAudioSessionManager2, 23 /* CLSCTX_ALL */, IntPtr.Zero, out iface);
                if (hr != 0 || iface == null) return 0;
                sessionMgr = (IAudioSessionManager2)iface;

                hr = sessionMgr.GetSessionEnumerator(out sessionEnum);
                if (hr != 0 || sessionEnum == null) return 0;

                int count;
                sessionEnum.GetCount(out count);
                float scalar = Math.Max(0f, Math.Min(1f, percent / 100f));

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctrl = null;

                    try
                    {
                        sessionEnum.GetSession(i, out ctrl);
                        if (ctrl == null) continue;

                        IAudioSessionControl2 ctrl2 = null;
                        ctrl2 = ctrl as IAudioSessionControl2; if (ctrl2 == null) continue;

                        uint pid;
                        ctrl2.GetProcessId(out pid);
                        if (pid == 0) continue;

                        string procName = GetProcessName(pid);
                        if (string.IsNullOrEmpty(procName)) continue;

                        if (procName.ToLowerInvariant().Replace(".exe", "") == target)
                        {
                            try
                            {
                                var simpleVol = ctrl as ISimpleAudioVolume;
                                if (simpleVol != null)
                                {
                                    simpleVol.SetMasterVolume(scalar, ref _eventContext);
                                    affected++;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to set app volume for " + processName, ex);
            }
            finally
            {
                if (sessionEnum != null && Marshal.IsComObject(sessionEnum))
                    Marshal.ReleaseComObject(sessionEnum);
                if (sessionMgr != null && Marshal.IsComObject(sessionMgr))
                    Marshal.ReleaseComObject(sessionMgr);
                if (device != null && Marshal.IsComObject(device))
                    Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }

            return affected;
        }

        /// <summary>
        /// Get current volume for a specific process by name. Returns -1 if not found.
        /// </summary>
        public static float GetAppVolume(string processName)
        {
            string target = processName.ToLowerInvariant().Replace(".exe", "");
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 sessionMgr = null;
            IAudioSessionEnumerator sessionEnum = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) return -1f;

                object iface;
                hr = device.Activate(IID_IAudioSessionManager2, 23 /* CLSCTX_ALL */, IntPtr.Zero, out iface);
                if (hr != 0 || iface == null) return -1f;
                sessionMgr = (IAudioSessionManager2)iface;

                hr = sessionMgr.GetSessionEnumerator(out sessionEnum);
                if (hr != 0 || sessionEnum == null) return -1f;

                int count;
                sessionEnum.GetCount(out count);

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctrl = null;

                    try
                    {
                        sessionEnum.GetSession(i, out ctrl);
                        if (ctrl == null) continue;

                        IAudioSessionControl2 ctrl2 = null;
                        ctrl2 = ctrl as IAudioSessionControl2; if (ctrl2 == null) continue;

                        uint pid;
                        ctrl2.GetProcessId(out pid);
                        if (pid == 0) continue;

                        string procName = GetProcessName(pid);
                        if (string.IsNullOrEmpty(procName)) continue;

                        if (procName.ToLowerInvariant().Replace(".exe", "") == target)
                        {
                            try
                            {
                                var simpleVol = ctrl as ISimpleAudioVolume;
                                if (simpleVol != null)
                                {
                                    float vol;
                                    simpleVol.GetMasterVolume(out vol);
                                    return vol * 100f;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get app volume for " + processName, ex);
            }
            finally
            {
                if (sessionEnum != null && Marshal.IsComObject(sessionEnum))
                    Marshal.ReleaseComObject(sessionEnum);
                if (sessionMgr != null && Marshal.IsComObject(sessionMgr))
                    Marshal.ReleaseComObject(sessionMgr);
                if (device != null && Marshal.IsComObject(device))
                    Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }

            return -1f;
        }

        private static string GetProcessName(uint pid)
        {
            try
            {
                using (var proc = System.Diagnostics.Process.GetProcessById((int)pid))
                    return proc.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        // --- Microphone (Capture) ---

        public static float GetMicVolume()
        {
            return GetAllMicsVolume();
        }

        public static bool SetMicVolume(float percent)
        {
            return SetAllMicsVolume(percent);
        }

        /// <summary>
        /// Returns the highest volume level across ALL active capture devices.
        /// If any mic is louder than expected, enforcement catches it.
        /// </summary>
        private static float GetAllMicsVolume()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;
            float maxVol = -1f;
            const int DEVICE_STATE_ACTIVE = 0x00000001;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out devices);
                if (hr != 0 || devices == null) return -1f;

                int count;
                devices.GetCount(out count);

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    IAudioEndpointVolume vol = null;
                    try
                    {
                        hr = devices.Item(i, out device);
                        if (hr != 0 || device == null) continue;

                        object iface;
                        hr = device.Activate(IID_IAudioEndpointVolume, 1, IntPtr.Zero, out iface);
                        if (hr != 0 || iface == null) continue;

                        vol = (IAudioEndpointVolume)iface;
                        float level;
                        vol.GetMasterVolumeLevelScalar(out level);
                        float pct = level * 100f;
                        if (pct > maxVol) maxVol = pct;
                    }
                    catch { }
                    finally
                    {
                        if (vol != null && Marshal.IsComObject(vol))
                            Marshal.ReleaseComObject(vol);
                        if (device != null && Marshal.IsComObject(device))
                            Marshal.ReleaseComObject(device);
                    }
                }

                return maxVol;
            }
            catch { return -1f; }
            finally
            {
                if (devices != null && Marshal.IsComObject(devices))
                    Marshal.ReleaseComObject(devices);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        /// <summary>
        /// Sets volume on ALL active capture devices.
        /// </summary>
        private static bool SetAllMicsVolume(float percent)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;
            bool anySuccess = false;
            const int DEVICE_STATE_ACTIVE = 0x00000001;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out devices);
                if (hr != 0 || devices == null) return false;

                int count;
                devices.GetCount(out count);
                float scalar = Math.Max(0f, Math.Min(1f, percent / 100f));

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    IAudioEndpointVolume vol = null;
                    try
                    {
                        hr = devices.Item(i, out device);
                        if (hr != 0 || device == null) continue;

                        object iface;
                        hr = device.Activate(IID_IAudioEndpointVolume, 1, IntPtr.Zero, out iface);
                        if (hr != 0 || iface == null) continue;

                        vol = (IAudioEndpointVolume)iface;
                        hr = vol.SetMasterVolumeLevelScalar(scalar, ref _eventContext);
                        if (hr == 0) anySuccess = true;
                    }
                    catch { }
                    finally
                    {
                        if (vol != null && Marshal.IsComObject(vol))
                            Marshal.ReleaseComObject(vol);
                        if (device != null && Marshal.IsComObject(device))
                            Marshal.ReleaseComObject(device);
                    }
                }

                return anySuccess;
            }
            catch { return false; }
            finally
            {
                if (devices != null && Marshal.IsComObject(devices))
                    Marshal.ReleaseComObject(devices);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        public static bool GetMicMute()
        {
            return GetAllMicsMuted();
        }

        /// <summary>
        /// Returns true if the default mic's mute FLAG is set, regardless of volume level.
        /// Use this for enforcement — we need to know if the flag is set even if volume is non-zero.
        /// GetMicMute() checks both mute AND volume, which causes false negatives after SetMicVolume.
        /// </summary>
        public static bool IsMicMuteFlagSet()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioEndpointVolume vol = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out device);
                if (hr != 0 || device == null) return false;
                object iface;
                hr = device.Activate(IID_IAudioEndpointVolume, 1, IntPtr.Zero, out iface);
                if (hr != 0 || iface == null) return false;
                vol = (IAudioEndpointVolume)iface;
                bool muted;
                vol.GetMute(out muted);
                return muted;
            }
            catch { return false; }
            finally
            {
                if (vol != null && Marshal.IsComObject(vol)) Marshal.ReleaseComObject(vol);
                if (device != null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
            }
        }

        /// <summary>
        /// Returns true only if ALL active capture devices are muted AND at zero volume.
        /// If any mic is unmuted or has volume, returns false.
        /// </summary>
        private static bool GetAllMicsMuted()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;
            const int DEVICE_STATE_ACTIVE = 0x00000001;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out devices);
                if (hr != 0 || devices == null) return false;

                int count;
                devices.GetCount(out count);
                if (count == 0) return false;

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    IAudioEndpointVolume vol = null;
                    try
                    {
                        hr = devices.Item(i, out device);
                        if (hr != 0 || device == null) continue;

                        object iface;
                        hr = device.Activate(IID_IAudioEndpointVolume, 1, IntPtr.Zero, out iface);
                        if (hr != 0 || iface == null) continue;

                        vol = (IAudioEndpointVolume)iface;
                        bool muted;
                        vol.GetMute(out muted);
                        if (!muted) return false; // Any unmuted mic = not protected

                        // Also check volume — mute flag can be bypassed in exclusive mode
                        float level;
                        vol.GetMasterVolumeLevelScalar(out level);
                        if (level > 0.01f) return false; // Volume not zeroed = not fully protected
                    }
                    catch { }
                    finally
                    {
                        if (vol != null && Marshal.IsComObject(vol))
                            Marshal.ReleaseComObject(vol);
                        if (device != null && Marshal.IsComObject(device))
                            Marshal.ReleaseComObject(device);
                    }
                }

                return true; // All mics are muted AND at zero volume
            }
            catch { return false; }
            finally
            {
                if (devices != null && Marshal.IsComObject(devices))
                    Marshal.ReleaseComObject(devices);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        public static bool SetMicMute(bool mute)
        {
            return SetAllMicsMute(mute);
        }

        // Cache of device volumes before we zero them for mute. Key = device ID.
        private static System.Collections.Generic.Dictionary<string, float> _preMuteVolumes 
            = new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>
        /// Mute or unmute ALL active capture (microphone) devices, not just the default.
        /// This ensures apps using non-default mics (Steam, Discord, etc.) are also silenced.
        /// When muting, saves each device's volume then slams to 0 as defense-in-depth.
        /// When unmuting, restores each device's saved volume.
        /// </summary>
        public static bool SetAllMicsMute(bool mute)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection devices = null;
            bool anySuccess = false;
            const int DEVICE_STATE_ACTIVE = 0x00000001;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out devices);
                if (hr != 0 || devices == null) return false;

                int count;
                devices.GetCount(out count);

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    IAudioEndpointVolume vol = null;
                    try
                    {
                        hr = devices.Item(i, out device);
                        if (hr != 0 || device == null) continue;

                        string deviceId = null;
                        device.GetId(out deviceId);

                        object iface;
                        hr = device.Activate(IID_IAudioEndpointVolume, 1, IntPtr.Zero, out iface);
                        if (hr != 0 || iface == null) continue;

                        vol = (IAudioEndpointVolume)iface;

                        if (mute)
                        {
                            // Save current volume before zeroing
                            float currentLevel;
                            vol.GetMasterVolumeLevelScalar(out currentLevel);
                            if (!string.IsNullOrEmpty(deviceId) && currentLevel > 0.01f)
                            {
                                _preMuteVolumes[deviceId] = currentLevel;
                            }

                            hr = vol.SetMute(true, ref _eventContext);
                            if (hr == 0) anySuccess = true;

                            // Defense-in-depth: also slam volume to zero
                            vol.SetMasterVolumeLevelScalar(0f, ref _eventContext);
                        }
                        else
                        {
                            hr = vol.SetMute(false, ref _eventContext);
                            if (hr == 0) anySuccess = true;

                            // Restore saved volume
                            float restoreLevel = 1.0f; // Default to 100% if no saved value
                            if (!string.IsNullOrEmpty(deviceId) && _preMuteVolumes.ContainsKey(deviceId))
                            {
                                restoreLevel = _preMuteVolumes[deviceId];
                                _preMuteVolumes.Remove(deviceId);
                            }
                            vol.SetMasterVolumeLevelScalar(restoreLevel, ref _eventContext);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Failed to set mute on capture device " + i, ex);
                    }
                    finally
                    {
                        if (vol != null && Marshal.IsComObject(vol))
                            Marshal.ReleaseComObject(vol);
                        if (device != null && Marshal.IsComObject(device))
                            Marshal.ReleaseComObject(device);
                    }
                }

                return anySuccess;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to enumerate capture devices for mute-all", ex);
                return false;
            }
            finally
            {
                if (devices != null && Marshal.IsComObject(devices))
                    Marshal.ReleaseComObject(devices);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        public static string GetMicDeviceId()
        {
            return GetDeviceId(EDataFlow.eCapture);
        }

        // --- Speakers (Render) ---

        public static float GetSpeakerVolume()
        {
            return GetVolume(EDataFlow.eRender);
        }

        public static bool SetSpeakerVolume(float percent)
        {
            return SetVolume(EDataFlow.eRender, percent);
        }

        public static bool GetSpeakerMute()
        {
            return GetMute(EDataFlow.eRender);
        }

        public static bool SetSpeakerMute(bool mute)
        {
            return SetMuteState(EDataFlow.eRender, mute);
        }

        public static string GetSpeakerDeviceId()
        {
            return GetDeviceId(EDataFlow.eRender);
        }

        // --- Device Names ---

        public static string GetMicName()
        {
            return GetDeviceName(EDataFlow.eCapture);
        }

        public static string GetSpeakerName()
        {
            return GetDeviceName(EDataFlow.eRender);
        }

        private static string GetDeviceName(EDataFlow flow)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IntPtr pStore = IntPtr.Zero;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) return "Not detected";

                hr = device.OpenPropertyStore(0 /* STGM_READ */, out pStore);
                if (hr != 0 || pStore == IntPtr.Zero) return "Unknown";

                // PKEY_Device_FriendlyName = {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
                var pkey = new _PROPERTYKEY();
                pkey.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
                pkey.pid = 14;

                var propVariant = new byte[24]; // PROPVARIANT is 16-24 bytes
                hr = PropVariantGetValue(pStore, ref pkey, propVariant);
                if (hr != 0) return "Unknown";

                // VT_LPWSTR = 31; data pointer at offset 8
                int vt = BitConverter.ToInt16(propVariant, 0);
                if (vt == 31) // VT_LPWSTR
                {
                    IntPtr ptr;
                    if (IntPtr.Size == 8)
                        ptr = new IntPtr(BitConverter.ToInt64(propVariant, 8));
                    else
                        ptr = new IntPtr(BitConverter.ToInt32(propVariant, 8));
                    if (ptr != IntPtr.Zero)
                    {
                        string name = Marshal.PtrToStringUni(ptr);
                        Marshal.FreeCoTaskMem(ptr);
                        return name ?? "Unknown";
                    }
                }
                return "Unknown";
            }
            catch (Exception ex)
            {
                Logger.Debug("GetDeviceName failed: " + ex.Message);
                return "Not detected";
            }
            finally
            {
                if (pStore != IntPtr.Zero) try { Marshal.Release(pStore); } catch { }
                if (device != null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct _PROPERTYKEY { public Guid fmtid; public int pid; }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(byte[] pvar);

        private static int PropVariantGetValue(IntPtr pStore, ref _PROPERTYKEY key, byte[] propVar)
        {
            // IPropertyStore::GetValue is vtable index 5 (after QI/AddRef/Release/GetCount/GetAt)
            IntPtr vtable = Marshal.ReadIntPtr(pStore);
            IntPtr fnGetValue = Marshal.ReadIntPtr(vtable, IntPtr.Size * 5);

            // Pin the propvar and key for the call
            var gch = System.Runtime.InteropServices.GCHandle.Alloc(propVar, System.Runtime.InteropServices.GCHandleType.Pinned);
            var gck = System.Runtime.InteropServices.GCHandle.Alloc(key, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                if (IntPtr.Size == 8)
                {
                    var del = (GetValueDelegate64)Marshal.GetDelegateForFunctionPointer(fnGetValue, typeof(GetValueDelegate64));
                    return del(pStore, gck.AddrOfPinnedObject(), gch.AddrOfPinnedObject());
                }
                else
                {
                    var del = (GetValueDelegate32)Marshal.GetDelegateForFunctionPointer(fnGetValue, typeof(GetValueDelegate32));
                    return del(pStore, gck.AddrOfPinnedObject(), gch.AddrOfPinnedObject());
                }
            }
            finally
            {
                gck.Free();
                gch.Free();
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetValueDelegate64(IntPtr pThis, IntPtr pKey, IntPtr pPropVar);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetValueDelegate32(IntPtr pThis, IntPtr pKey, IntPtr pPropVar);

        // --- Internal Implementation ---

        private static IAudioEndpointVolume GetEndpointVolume(EDataFlow flow)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.eMultimedia, out device);
                if (hr != 0 || device == null)
                    return null;

                object iface;
                hr = device.Activate(IID_IAudioEndpointVolume, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out iface);
                if (hr != 0 || iface == null)
                    return null;

                return (IAudioEndpointVolume)iface;
            }
            finally
            {
                if (device != null && Marshal.IsComObject(device))
                    Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        private static float GetVolume(EDataFlow flow)
        {
            IAudioEndpointVolume vol = null;
            try
            {
                vol = GetEndpointVolume(flow);
                if (vol == null) return -1f;

                float level;
                vol.GetMasterVolumeLevelScalar(out level);
                return level * 100f;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get volume for " + flow, ex);
                return -1f;
            }
            finally
            {
                if (vol != null && Marshal.IsComObject(vol))
                    Marshal.ReleaseComObject(vol);
            }
        }

        private static bool SetVolume(EDataFlow flow, float percent)
        {
            IAudioEndpointVolume vol = null;
            try
            {
                vol = GetEndpointVolume(flow);
                if (vol == null) return false;

                float scalar = Math.Max(0f, Math.Min(1f, percent / 100f));
                int hr = vol.SetMasterVolumeLevelScalar(scalar, ref _eventContext);
                return hr == 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to set volume for " + flow, ex);
                return false;
            }
            finally
            {
                if (vol != null && Marshal.IsComObject(vol))
                    Marshal.ReleaseComObject(vol);
            }
        }

        private static bool GetMute(EDataFlow flow)
        {
            IAudioEndpointVolume vol = null;
            try
            {
                vol = GetEndpointVolume(flow);
                if (vol == null) return false;

                bool mute;
                vol.GetMute(out mute);
                return mute;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get mute state for " + flow, ex);
                return false;
            }
            finally
            {
                if (vol != null && Marshal.IsComObject(vol))
                    Marshal.ReleaseComObject(vol);
            }
        }

        private static bool SetMuteState(EDataFlow flow, bool mute)
        {
            IAudioEndpointVolume vol = null;
            try
            {
                vol = GetEndpointVolume(flow);
                if (vol == null) return false;

                int hr = vol.SetMute(mute, ref _eventContext);
                return hr == 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to set mute state for " + flow, ex);
                return false;
            }
            finally
            {
                if (vol != null && Marshal.IsComObject(vol))
                    Marshal.ReleaseComObject(vol);
            }
        }

        private static string GetDeviceId(EDataFlow flow)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
                int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.eMultimedia, out device);
                if (hr != 0 || device == null)
                    return null;

                string id;
                device.GetId(out id);
                return id;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get device ID for " + flow, ex);
                return null;
            }
            finally
            {
                if (device != null && Marshal.IsComObject(device))
                    Marshal.ReleaseComObject(device);
                if (enumerator != null && Marshal.IsComObject(enumerator))
                    Marshal.ReleaseComObject(enumerator);
            }
        }
    }
}
