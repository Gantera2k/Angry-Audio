// AudioSettings.cs — Unified state controller for all shared settings.
// ONE instance exists. Both OptionsForm and WelcomeForm read/write it.
// TrayApp subscribes to the Changed event and actuates immediately.
//
// This eliminates the _onToggle string message system for all shared controls.
// Any change to any property fires Changed, which TrayApp handles.
//
using System;
using System.Runtime.InteropServices;

namespace AngryAudio
{
    /// <summary>
    /// Identifies exactly which key slot is being captured.
    /// ONE enum, ONE state machine, ZERO boolean flags.
    /// </summary>
    public enum CaptureTarget
    {
        None,
        PttKey1, PttKey2,
        PtmKey1, PtmKey2,
        ToggleKey1, ToggleKey2,
        MmKey, MmKey2,
        DictationKey1, DictationKey2,
        DictationToggleKey, DictationToggleKey2
    }

    /// <summary>
    /// What changed — TrayApp uses this to decide what to actuate.
    /// </summary>
    [Flags]
    public enum SettingsChange
    {
        None         = 0,
        PttMode      = 1 << 0,   // PTT/PTM/Toggle enabled, keys changed — engine restart needed
        PttCosmetic  = 1 << 9,   // Overlay/sound toggles changed — NO engine restart, just refresh UI
        AfkMic       = 1 << 1,   // AFK mic mute enabled or seconds changed
        AfkSpeaker   = 1 << 2,   // AFK speaker mute enabled or seconds changed
        MicLock      = 1 << 3,   // Mic volume lock enabled or volume changed
        SpeakerLock  = 1 << 4,   // Speaker volume lock enabled or volume changed
        Startup      = 1 << 5,   // Start with Windows changed
        Notifications = 1 << 6,  // Notification toggles changed
        Overlay      = 1 << 7,   // Mic overlay master toggle changed
        AppVolume    = 1 << 8,   // Per-app volume rules changed
        Dictation    = 1 << 10,  // Dictation engine/key/mode changed
        VoiceActivity = 1 << 14, // Voice activity mode changed
        NightMode    = 1 << 11, // Night mode (loudness EQ) changed
        Equalizer    = 1 << 12, // EQ enhancement changed (bass boost, virtual surround)
        ApplyEnhancements = 1 << 13, // User clicked Apply — write registry and restart audio
    }

    /// <summary>
    /// Single source of truth for all audio settings.
    /// Both OptionsForm and WelcomeForm hold a reference to the SAME instance.
    /// Any mutation fires Changed so TrayApp can actuate immediately.
    /// </summary>
    public class AudioSettings
    {
        private readonly Settings _s;
        private bool _suppressEvents;

        /// <summary>Fires whenever any setting changes. TrayApp subscribes to this.</summary>
        public event Action<SettingsChange> Changed;

        /// <summary>Fires when key capture should start/stop (suspends/resumes PTT hook).</summary>
        public event Action<bool> CaptureStateChanged; // true = capturing, false = done

        /// <summary>The underlying Settings object for persistence and fields not managed here.</summary>
        public Settings Raw { get { return _s; } }

        public AudioSettings(Settings settings)
        {
            _s = settings;
        }

        // --- Fire helpers ---

        private void Fire(SettingsChange what)
        {
            if (!_suppressEvents && Changed != null)
            {
                try { Changed(what); } catch (Exception ex) { Logger.Error("AudioSettings.Changed handler failed.", ex); }
            }
        }

        /// <summary>Suppress Changed events during bulk loads (e.g. LoadSettings into UI).</summary>
        public void BeginUpdate() { _suppressEvents = true; }
        public void EndUpdate() { _suppressEvents = false; }

        /// <summary>Save to disk.</summary>
        public void Save() { _s.Save(); }

        // =====================================================================
        //  PTT / PTM / TOGGLE
        // =====================================================================

        public bool PttEnabled
        {
            get { return _s.PushToTalkEnabled; }
            set { if (_s.PushToTalkEnabled != value) { _s.PushToTalkEnabled = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public int PttKey
        {
            get { return _s.PushToTalkKey; }
            set { if (_s.PushToTalkKey != value) { _s.PushToTalkKey = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public int PttKey2
        {
            get { return _s.PushToTalkKey2; }
            set { if (_s.PushToTalkKey2 != value) { _s.PushToTalkKey2 = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public bool PttKey1ShowOverlay
        {
            get { return _s.PttKey1ShowOverlay; }
            set { if (_s.PttKey1ShowOverlay != value) { _s.PttKey1ShowOverlay = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool PttKey2ShowOverlay
        {
            get { return _s.PttKey2ShowOverlay; }
            set { if (_s.PttKey2ShowOverlay != value) { _s.PttKey2ShowOverlay = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool PttSoundFeedback
        {
            get { return _s.PttSoundFeedback; }
            set { if (_s.PttSoundFeedback != value) { _s.PttSoundFeedback = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }

        // --- Push-to-Mute ---
        public bool PtmEnabled
        {
            get { return _s.PushToMuteEnabled; }
            set { if (_s.PushToMuteEnabled != value) { _s.PushToMuteEnabled = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public int PtmKey
        {
            get { return _s.PushToMuteKey; }
            set { if (_s.PushToMuteKey != value) { _s.PushToMuteKey = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public int PtmKey2
        {
            get { return _s.PushToMuteKey2; }
            set { if (_s.PushToMuteKey2 != value) { _s.PushToMuteKey2 = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public bool PtmShowOverlay
        {
            get { return _s.PtmShowOverlay; }
            set { if (_s.PtmShowOverlay != value) { _s.PtmShowOverlay = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }
        public int MicOverlayX
        {
            get { return _s.MicOverlayX; }
            set { if (_s.MicOverlayX != value) { _s.MicOverlayX = value; _s.Save(); } }
        }
        public int MicOverlayY
        {
            get { return _s.MicOverlayY; }
            set { if (_s.MicOverlayY != value) { _s.MicOverlayY = value; _s.Save(); } }
        }

        public bool PtmSoundFeedback
        {
            get { return _s.PtmSoundFeedback; }
            set { if (_s.PtmSoundFeedback != value) { _s.PtmSoundFeedback = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }

        // --- Push-to-Toggle ---
        public bool PtToggleEnabled
        {
            get { return _s.PushToToggleEnabled; }
            set { if (_s.PushToToggleEnabled != value) { _s.PushToToggleEnabled = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public bool PttSuppressEnabled
        {
            get { return _s.PttSuppressEnabled; }
            set { if (_s.PttSuppressEnabled != value) { _s.PttSuppressEnabled = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public bool PtmSuppressEnabled
        {
            get { return _s.PtmSuppressEnabled; }
            set { if (_s.PtmSuppressEnabled != value) { _s.PtmSuppressEnabled = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public bool PtToggleSuppressEnabled
        {
            get { return _s.PtToggleSuppressEnabled; }
            set { if (_s.PtToggleSuppressEnabled != value) { _s.PtToggleSuppressEnabled = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public int PtToggleKey
        {
            get { return _s.PushToToggleKey; }
            set { if (_s.PushToToggleKey != value) { _s.PushToToggleKey = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public int PtToggleKey2
        {
            get { return _s.PushToToggleKey2; }
            set { if (_s.PushToToggleKey2 != value) { _s.PushToToggleKey2 = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public bool PtToggleShowOverlay
        {
            get { return _s.PtToggleShowOverlay; }
            set { if (_s.PtToggleShowOverlay != value) { _s.PtToggleShowOverlay = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool PtToggleSoundFeedback
        {
            get { return _s.PtToggleSoundFeedback; }
            set { if (_s.PtToggleSoundFeedback != value) { _s.PtToggleSoundFeedback = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }

        // --- Multi-Mode Key ---
        public int MmKey
        {
            get { return _s.MultiModeKey; }
            set { if (_s.MultiModeKey != value) { _s.MultiModeKey = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }
        public int MmKey2
        {
            get { return _s.MultiModeKey2; }
            set { if (_s.MultiModeKey2 != value) { _s.MultiModeKey2 = value; _s.Save(); Fire(SettingsChange.PttMode); } }
        }

        // --- Sound type (shared across all modes) ---
        public int SoundFeedbackType
        {
            get { return _s.SoundFeedbackType; }
            set { if (_s.SoundFeedbackType != value) { _s.SoundFeedbackType = value; _s.Save(); } }
        }
        public int PttSoundType
        {
            get { return _s.PttSoundType; }
            set { if (_s.PttSoundType != value) { _s.PttSoundType = value; _s.Save(); } }
        }
        public int PtmSoundType
        {
            get { return _s.PtmSoundType; }
            set { if (_s.PtmSoundType != value) { _s.PtmSoundType = value; _s.Save(); } }
        }
        public int PtToggleSoundType
        {
            get { return _s.PtToggleSoundType; }
            set { if (_s.PtToggleSoundType != value) { _s.PtToggleSoundType = value; _s.Save(); } }
        }
        public int DictSoundType
        {
            get { return _s.DictSoundType; }
            set { if (_s.DictSoundType != value) { _s.DictSoundType = value; _s.Save(); } }
        }

        // --- Per-segment sound volume (0–100) ---
        public int PttSoundVolume
        {
            get { return _s.PttSoundVolume; }
            set { if (_s.PttSoundVolume != value) { _s.PttSoundVolume = value; _s.Save(); } }
        }
        public int PtmSoundVolume
        {
            get { return _s.PtmSoundVolume; }
            set { if (_s.PtmSoundVolume != value) { _s.PtmSoundVolume = value; _s.Save(); } }
        }
        public int PtToggleSoundVolume
        {
            get { return _s.PtToggleSoundVolume; }
            set { if (_s.PtToggleSoundVolume != value) { _s.PtToggleSoundVolume = value; _s.Save(); } }
        }
        public int DictSoundVolume
        {
            get { return _s.DictSoundVolume; }
            set { if (_s.DictSoundVolume != value) { _s.DictSoundVolume = value; _s.Save(); } }
        }

        // =====================================================================
        //  BATCH PTT OPERATIONS — set key + enable in one shot, one event
        // =====================================================================

        /// <summary>Set PTT key and enable in one atomic operation. Fires ONE Changed event.</summary>
        public void SetPttKeyAndEnable(int vk)
        {
            _suppressEvents = true;
            _s.PushToTalkKey = vk;
            _s.PushToTalkEnabled = true;
            _s.Save();
            _suppressEvents = false;
            Fire(SettingsChange.PttMode);
        }

        /// <summary>Set PTM key and enable in one atomic operation.</summary>
        public void SetPtmKeyAndEnable(int vk)
        {
            _suppressEvents = true;
            _s.PushToMuteKey = vk;
            _s.PushToMuteEnabled = true;
            _s.Save();
            _suppressEvents = false;
            Fire(SettingsChange.PttMode);
        }

        /// <summary>Set Toggle key and enable in one atomic operation.</summary>
        public void SetPtToggleKeyAndEnable(int vk)
        {
            _suppressEvents = true;
            _s.PushToToggleKey = vk;
            _s.PushToToggleEnabled = true;
            _s.Save();
            _suppressEvents = false;
            Fire(SettingsChange.PttMode);
        }

        /// <summary>Disable a PTT mode and clear its keys. Fires ONE Changed event.</summary>
        public void DisablePttMode()
        {
            _suppressEvents = true;
            _s.PushToTalkEnabled = false;
            _s.PushToTalkKey = 0; _s.PushToTalkKey2 = 0;
            _s.Save();
            _suppressEvents = false;
            Fire(SettingsChange.PttMode);
        }

        public void DisablePtmMode()
        {
            _suppressEvents = true;
            _s.PushToMuteEnabled = false;
            _s.PushToMuteKey = 0; _s.PushToMuteKey2 = 0;
            _s.Save();
            _suppressEvents = false;
            Fire(SettingsChange.PttMode);
        }

        public void DisablePtToggleMode()
        {
            _suppressEvents = true;
            _s.PushToToggleEnabled = false;
            _s.PushToToggleKey = 0; _s.PushToToggleKey2 = 0;
            _s.Save();
            _suppressEvents = false;
            // BULLETPROOF: ALWAYS unmute the mic when Toggle is disabled.
            // Toggle mode starts with mic muted — if Toggle is disabled for ANY reason
            // (user unchecked it, key stolen by another segment, key removed, etc.)
            // the mic MUST be unmuted or the user's mic stays dead permanently.
            try { Audio.SetMicMute(false); } catch { }
            Fire(SettingsChange.PttMode);
        }

        /// <summary>Clear the multi-mode keys.</summary>
        public void ClearMmKey()
        {
            _suppressEvents = true;
            _s.MultiModeKey = 0;
            _s.MultiModeKey2 = 0;
            _s.Save();
            _suppressEvents = false;
            Fire(SettingsChange.PttMode);
        }

        /// <summary>True if any PTT mode is enabled with a valid key.</summary>
        public bool AnyModeActive
        {
            get {
                return (PttEnabled && PttKey > 0) ||
                       (PtmEnabled && PtmKey > 0) ||
                       (PtToggleEnabled && PtToggleKey > 0);
            }
        }

        // =====================================================================
        //  AFK PROTECTION
        // =====================================================================

        public bool AfkMicEnabled
        {
            get { return _s.AfkMicMuteEnabled; }
            set { if (_s.AfkMicMuteEnabled != value) { _s.AfkMicMuteEnabled = value; _s.Save(); Fire(SettingsChange.AfkMic); } }
        }
        public int AfkMicSec
        {
            get { return _s.AfkMicMuteSec; }
            set { if (_s.AfkMicMuteSec != value) { _s.AfkMicMuteSec = value; _s.Save(); Fire(SettingsChange.AfkMic); } }
        }
        public bool AfkSpeakerEnabled
        {
            get { return _s.AfkSpeakerMuteEnabled; }
            set { if (_s.AfkSpeakerMuteEnabled != value) { _s.AfkSpeakerMuteEnabled = value; _s.Save(); Fire(SettingsChange.AfkSpeaker); } }
        }
        public int AfkSpeakerSec
        {
            get { return _s.AfkSpeakerMuteSec; }
            set { if (_s.AfkSpeakerMuteSec != value) { _s.AfkSpeakerMuteSec = value; _s.Save(); Fire(SettingsChange.AfkSpeaker); } }
        }

        // =====================================================================
        //  VOLUME LOCK
        // =====================================================================

        public bool MicLockEnabled
        {
            get { return _s.MicEnforceEnabled; }
            set { if (_s.MicEnforceEnabled != value) { _s.MicEnforceEnabled = value; _s.Save(); Fire(SettingsChange.MicLock); } }
        }
        public int MicLockVolume
        {
            get { return _s.MicVolumePercent; }
            set { if (_s.MicVolumePercent != value) { _s.MicVolumePercent = value; _s.Save(); Fire(SettingsChange.MicLock); } }
        }
        public bool SpeakerLockEnabled
        {
            get { return _s.SpeakerEnforceEnabled; }
            set { if (_s.SpeakerEnforceEnabled != value) { _s.SpeakerEnforceEnabled = value; _s.Save(); Fire(SettingsChange.SpeakerLock); } }
        }
        public int SpeakerLockVolume
        {
            get { return _s.SpeakerVolumePercent; }
            set { if (_s.SpeakerVolumePercent != value) { _s.SpeakerVolumePercent = value; _s.Save(); Fire(SettingsChange.SpeakerLock); } }
        }

        // =====================================================================
        //  GENERAL
        // =====================================================================

        public bool StartWithWindows
        {
            get { return _s.StartWithWindows; }
            set { if (_s.StartWithWindows != value) { _s.StartWithWindows = value; _s.ApplyStartupSetting(); _s.Save(); Fire(SettingsChange.Startup); } }
        }
        public bool NotifyOnCorrection
        {
            get { return _s.NotifyOnCorrection; }
            set { if (_s.NotifyOnCorrection != value) { _s.NotifyOnCorrection = value; _s.Save(); Fire(SettingsChange.Notifications); } }
        }
        public bool NotifyOnDeviceChange
        {
            get { return _s.NotifyOnDeviceChange; }
            set { if (_s.NotifyOnDeviceChange != value) { _s.NotifyOnDeviceChange = value; _s.Save(); Fire(SettingsChange.Notifications); } }
        }
        public bool RestrictSoundOutput
        {
            get { return _s.RestrictSoundOutput; }
            set { if (_s.RestrictSoundOutput != value) { _s.RestrictSoundOutput = value; _s.Save(); Fire(SettingsChange.Notifications); } }
        }
        public bool MicOverlayEnabled
        {
            get { return _s.MicOverlayEnabled; }
            set { if (_s.MicOverlayEnabled != value) { _s.MicOverlayEnabled = value; _s.Save(); Fire(SettingsChange.Overlay); } }
        }
        public bool AppVolumeEnabled
        {
            get { return _s.AppVolumeEnforceEnabled; }
            set { if (_s.AppVolumeEnforceEnabled != value) { _s.AppVolumeEnforceEnabled = value; _s.Save(); Fire(SettingsChange.AppVolume); } }
        }

        // === DICTATION ===
        public bool DictationEnabled
        {
            get { return _s.DictationEnabled; }
            set { if (_s.DictationEnabled != value) { _s.DictationEnabled = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public bool DictationToggleEnabled
        {
            get { return _s.DictationToggleEnabled; }
            set { if (_s.DictationToggleEnabled != value) { _s.DictationToggleEnabled = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public bool DictPthSuppressEnabled
        {
            get { return _s.DictPthSuppressEnabled; }
            set { if (_s.DictPthSuppressEnabled != value) { _s.DictPthSuppressEnabled = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public bool DictPttSuppressEnabled
        {
            get { return _s.DictPttSuppressEnabled; }
            set { if (_s.DictPttSuppressEnabled != value) { _s.DictPttSuppressEnabled = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public int DictationKey2
        {
            get { return _s.DictationKey2; }
            set { if (_s.DictationKey2 != value) { _s.DictationKey2 = value; _s.Save(); } }
        }
        public int DictationToggleKey2
        {
            get { return _s.DictationToggleKey2; }
            set { if (_s.DictationToggleKey2 != value) { _s.DictationToggleKey2 = value; _s.Save(); } }
        }
        public int DictationEngine
        {
            get { return _s.DictationEngine; }
            set { if (_s.DictationEngine != value) { _s.DictationEngine = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public int DictationMode
        {
            get { return _s.DictationMode; }
            set { if (_s.DictationMode != value) { _s.DictationMode = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public int DictationKey
        {
            get { return _s.DictationKey; }
            set { if (_s.DictationKey != value) { _s.DictationKey = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public int DictationToggleKey
        {
            get { return _s.DictationToggleKey; }
            set { if (_s.DictationToggleKey != value) { _s.DictationToggleKey = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public int DictationWhisperModel
        {
            get { return _s.DictationWhisperModel; }
            set { if (_s.DictationWhisperModel != value) { _s.DictationWhisperModel = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public bool DictSoundFeedback
        {
            get { return _s.DictSoundFeedback; }
            set { if (_s.DictSoundFeedback != value) { _s.DictSoundFeedback = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public bool DictShowOverlay
        {
            get { return _s.DictShowOverlay; }
            set { if (_s.DictShowOverlay != value) { _s.DictShowOverlay = value; _s.Save(); Fire(SettingsChange.Dictation); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool DictDuckingEnabled
        {
            get { return _s.DictDuckingEnabled; }
            set { if (_s.DictDuckingEnabled != value) { _s.DictDuckingEnabled = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public int DictDuckingVolume
        {
            get { return _s.DictDuckingVolume; }
            set { if (_s.DictDuckingVolume != value) { _s.DictDuckingVolume = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }

        // Per-segment duck flags
        public bool PttDuckEnabled
        {
            get { return _s.PttDuckEnabled; }
            set { if (_s.PttDuckEnabled != value) { _s.PttDuckEnabled = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool PtmDuckEnabled
        {
            get { return _s.PtmDuckEnabled; }
            set { if (_s.PtmDuckEnabled != value) { _s.PtmDuckEnabled = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool PtToggleDuckEnabled
        {
            get { return _s.PtToggleDuckEnabled; }
            set { if (_s.PtToggleDuckEnabled != value) { _s.PtToggleDuckEnabled = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool DictPthDuckEnabled
        {
            get { return _s.DictPthDuckEnabled; }
            set { if (_s.DictPthDuckEnabled != value) { _s.DictPthDuckEnabled = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public bool DictPttDuckEnabled
        {
            get { return _s.DictPttDuckEnabled; }
            set { if (_s.DictPttDuckEnabled != value) { _s.DictPttDuckEnabled = value; _s.Save(); Fire(SettingsChange.Dictation); } }
        }
        public int PttDuckingVolume
        {
            get { return _s.PttDuckingVolume; }
            set { if (_s.PttDuckingVolume != value) { _s.PttDuckingVolume = value; _s.Save(); Fire(SettingsChange.PttCosmetic); } }
        }

        // =====================================================================
        //  VOICE ACTIVITY
        // =====================================================================

        public bool VoiceActivityEnabled
        {
            get { return _s.VoiceActivityEnabled; }
            set { if (_s.VoiceActivityEnabled != value) { _s.VoiceActivityEnabled = value; _s.Save(); Fire(SettingsChange.VoiceActivity); } }
        }
        public int VoiceActivityThreshold
        {
            get { return _s.VoiceActivityThreshold; }
            set { if (_s.VoiceActivityThreshold != value) { _s.VoiceActivityThreshold = value; _s.Save(); Fire(SettingsChange.VoiceActivity); } }
        }
        public int VoiceActivityHoldoverMs
        {
            get { return _s.VoiceActivityHoldoverMs; }
            set { if (_s.VoiceActivityHoldoverMs != value) { _s.VoiceActivityHoldoverMs = value; _s.Save(); Fire(SettingsChange.VoiceActivity); } }
        }
        public bool VoiceActivityShowOverlay
        {
            get { return _s.VoiceActivityShowOverlay; }
            set { if (_s.VoiceActivityShowOverlay != value) { _s.VoiceActivityShowOverlay = value; _s.Save(); Fire(SettingsChange.VoiceActivity); Fire(SettingsChange.PttCosmetic); } }
        }
        public bool VoiceActivitySoundFeedback
        {
            get { return _s.VoiceActivitySoundFeedback; }
            set { if (_s.VoiceActivitySoundFeedback != value) { _s.VoiceActivitySoundFeedback = value; _s.Save(); Fire(SettingsChange.VoiceActivity); } }
        }

        /// <summary>Disable Voice Activity mode (called when PTT/PTM/Toggle is enabled for mutual exclusivity).</summary>
        public void DisableVoiceActivity()
        {
            if (_s.VoiceActivityEnabled) { _s.VoiceActivityEnabled = false; _s.Save(); Fire(SettingsChange.VoiceActivity); }
        }

        // =====================================================================
        //  NIGHT MODE (LOUDNESS EQUALIZATION)
        // =====================================================================

        public bool NightModeEnabled
        {
            get { return _s.NightModeEnabled; }
            set { if (_s.NightModeEnabled != value) { _s.NightModeEnabled = value; _s.Save(); Fire(SettingsChange.NightMode); } }
        }
        public int NightModeReleaseTime
        {
            get { return _s.NightModeReleaseTime; }
            set { int v = Math.Max(2, Math.Min(7, value)); if (_s.NightModeReleaseTime != v) { _s.NightModeReleaseTime = v; _s.Save(); if (_s.NightModeEnabled) Fire(SettingsChange.NightMode); } }
        }

        // =====================================================================
        //  EQUALIZER ENHANCEMENTS
        // =====================================================================



        public string EqBands
        {
            get { return _s.EqBands ?? "0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5"; }
            set { if (_s.EqBands != value) { _s.EqBands = value; _s.Save(); } }
        }

                /// <summary>User explicitly clicked Apply — push all enhancement settings to registry and restart audio.</summary>
        public void ApplyEnhancements() { Fire(SettingsChange.ApplyEnhancements); }

        // =====================================================================
        //  KEY CAPTURE — shared implementation
        // =====================================================================

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private System.Threading.Timer _captureTimer;
        private bool[] _prevKeyState = new bool[256];
        private Action<int> _captureCallback;
        private volatile bool _capturing;
        private CaptureTarget _currentTarget = CaptureTarget.None;
        private System.Threading.SynchronizationContext _uiContext;

        /// <summary>True when a key capture is in progress.</summary>
        public bool IsCapturing { get { return _capturing; } }

        /// <summary>Which key slot is currently being captured.</summary>
        public CaptureTarget CurrentTarget { get { return _currentTarget; } }

        /// <summary>Capture the SynchronizationContext so we can marshal back to UI thread.</summary>
        public void InitCaptureContext() {
            _uiContext = System.Threading.SynchronizationContext.Current as System.Threading.SynchronizationContext;
        }

        /// <summary>
        /// Start capturing the next key press for a specific target slot.
        /// Calls back with the virtual key code (0 = cancelled/Escape).
        /// </summary>
        public void StartCapture(CaptureTarget target, Action<int> callback)
        {
            if (_capturing) return;
            _capturing = true;
            _currentTarget = target;
            _captureCallback = callback;

            // Notify TrayApp to suspend PTT hook
            if (CaptureStateChanged != null) CaptureStateChanged(true);

            // Snapshot current key state
            for (int i = 0; i < 256; i++)
                _prevKeyState[i] = (GetAsyncKeyState(i) & 0x8000) != 0;

            // Use System.Threading.Timer — runs on threadpool, immune to UI thread starvation.
            // WinForms Timer fires via WM_TIMER (lowest priority message) and gets starved
            // by continuous WM_PAINT from star animations and card repaints.
            if (_captureTimer != null) { try { _captureTimer.Dispose(); } catch { } }
            // Run at a gentler 40ms interval (25Hz) to prevent CPU spikes and micro-stutters
            // when reading 255 keys via GetAsyncKeyState
            _captureTimer = new System.Threading.Timer(CaptureTimerTick, null, 0, 40);
        }

        /// <summary>Clear a key from the previous-key-state snapshot so a conflict bounce
        /// can immediately re-capture the same key without requiring a release first.</summary>
        public void ClearPrevKeyState(int vk) {
            if (vk > 0 && vk < _prevKeyState.Length) _prevKeyState[vk] = false;
        }

        /// <summary>Cancel an in-progress capture.</summary>
        public void CancelCapture()
        {
            if (!_capturing) return;
            _capturing = false;
            _currentTarget = CaptureTarget.None;
            if (_captureTimer != null) { try { _captureTimer.Dispose(); } catch { } _captureTimer = null; }
            if (CaptureStateChanged != null) CaptureStateChanged(false);
            if (_captureCallback != null) { try { _captureCallback(0); } catch { } }
            _captureCallback = null;
        }

        private void CaptureTimerTick(object state)
        {
            if (!_capturing) { var t = _captureTimer; _captureTimer = null; if (t != null) try { t.Dispose(); } catch { } return; }

            for (int vk = 1; vk < 256; vk++)
            {
                if (vk >= 1 && vk <= 3) continue; // skip LMB, RMB, Cancel — unusable as hotkeys. X1(5), X2(6), MMB(4) are fine — PTT detects them via GetAsyncKeyState polling
                bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                bool wasDown = _prevKeyState[vk];
                _prevKeyState[vk] = down;
                if (down && !wasDown)
                {
                    _capturing = false;
                    _currentTarget = CaptureTarget.None;
                    var t = _captureTimer; _captureTimer = null; if (t != null) try { t.Dispose(); } catch { }

                    // Resolve L/R modifiers
                    int resolvedVk = vk;
                    if (resolvedVk == 0x10) resolvedVk = (GetAsyncKeyState(0xA1) & 0x8000) != 0 ? 0xA1 : 0xA0;
                    if (resolvedVk == 0x11) resolvedVk = (GetAsyncKeyState(0xA3) & 0x8000) != 0 ? 0xA3 : 0xA2;
                    if (resolvedVk == 0x12) resolvedVk = (GetAsyncKeyState(0xA5) & 0x8000) != 0 ? 0xA5 : 0xA4;

                    // Escape = cancel
                    if (resolvedVk == 0x1B) resolvedVk = 0;

                    Logger.Info("AudioSettings: captured vk=0x" + resolvedVk.ToString("X2") + " (" + PushToTalk.GetKeyName(resolvedVk) + ")");

                    // Marshal callback to UI thread
                    int capturedVk = resolvedVk;
                    Action uiAction = () => {
                        // Run callback FIRST — it may modify keys or start a new capture (conflict bounce).
                        // CaptureStateChanged must fire AFTER so TrayApp doesn't resume the PTT hook
                        // while the captured key is still registered as a hotkey.
                        var cb = _captureCallback;
                        _captureCallback = null;
                        if (cb != null) { try { cb(capturedVk); } catch { } }

                        // Only resume hook if the callback didn't start a new capture
                        if (!_capturing) {
                            if (CaptureStateChanged != null) CaptureStateChanged(false);
                        }
                    };

                    // Post to UI thread
                    if (_uiContext != null)
                        _uiContext.Post(_ => uiAction(), null);
                    else
                        uiAction(); // fallback: run inline

                    return;
                }
            }
        }

        public bool IsKeyInUse(int vk, int excludeMode)
        {
            if (vk <= 0) return false;
            // The MM key is an explicit exception; it can overlap with any other key.
            if (excludeMode == 3) return false;

            // excludeMode: 0=PTT, 1=PTM, 2=Toggle, 3=MmKey(bypassed), 4=Dictation
            if (excludeMode != 0 && (vk == PttKey || vk == PttKey2)) return true;
            if (excludeMode != 1 && (vk == PtmKey || vk == PtmKey2)) return true;
            if (excludeMode != 2 && (vk == PtToggleKey || vk == PtToggleKey2)) return true;
            if (excludeMode != 4 && (vk == DictationKey || vk == DictationKey2 || vk == DictationToggleKey || vk == DictationToggleKey2)) return true;
            return false;
        }

        /// <summary>Check if a key duplicates another slot within the same mode (e.g. PTT key1 == PTT key2).</summary>
        public bool IsDuplicateInMode(int vk, CaptureTarget target)
        {
            if (vk <= 0) return false;
            switch (target)
            {
                case CaptureTarget.PttKey1: return vk == PttKey2;
                case CaptureTarget.PttKey2: return vk == PttKey;
                case CaptureTarget.PtmKey1: return vk == PtmKey2;
                case CaptureTarget.PtmKey2: return vk == PtmKey;
                case CaptureTarget.ToggleKey1: return vk == PtToggleKey2;
                case CaptureTarget.ToggleKey2: return vk == PtToggleKey;
                case CaptureTarget.MmKey: return vk == MmKey2;
                case CaptureTarget.MmKey2: return vk == MmKey;
                case CaptureTarget.DictationKey1: return vk == DictationKey2 || vk == DictationToggleKey || vk == DictationToggleKey2;
                case CaptureTarget.DictationKey2: return vk == DictationKey || vk == DictationToggleKey || vk == DictationToggleKey2;
                case CaptureTarget.DictationToggleKey: return vk == DictationToggleKey2 || vk == DictationKey || vk == DictationKey2;
                case CaptureTarget.DictationToggleKey2: return vk == DictationToggleKey || vk == DictationKey || vk == DictationKey2;
            }
            return false;
        }

        /// <summary>Get the exclude mode index (0=PTT,1=PTM,2=Toggle,3=MM) for a capture target.</summary>
        public static int ExcludeModeFor(CaptureTarget t)
        {
            if (t >= CaptureTarget.PttKey1 && t <= CaptureTarget.PttKey2) return 0;
            if (t >= CaptureTarget.PtmKey1 && t <= CaptureTarget.PtmKey2) return 1;
            if (t >= CaptureTarget.ToggleKey1 && t <= CaptureTarget.ToggleKey2) return 2;
            if (t == CaptureTarget.MmKey || t == CaptureTarget.MmKey2) return 3;
            if (t == CaptureTarget.DictationKey1 || t == CaptureTarget.DictationKey2 ||
                t == CaptureTarget.DictationToggleKey || t == CaptureTarget.DictationToggleKey2) return 4;
            return -1; // unknown
        }

        /// <summary>Set the key for a specific target and save.</summary>
        public void SetKeyForTarget(CaptureTarget target, int vk)
        {
            switch (target)
            {
                case CaptureTarget.PttKey1: PttKey = vk; break;
                case CaptureTarget.PttKey2: PttKey2 = vk; break;
                case CaptureTarget.PtmKey1: PtmKey = vk; break;
                case CaptureTarget.PtmKey2: PtmKey2 = vk; break;
                case CaptureTarget.ToggleKey1: PtToggleKey = vk; break;
                case CaptureTarget.ToggleKey2: PtToggleKey2 = vk; break;
                case CaptureTarget.MmKey: MmKey = vk; break;
                case CaptureTarget.MmKey2: MmKey2 = vk; break;
                case CaptureTarget.DictationKey1: DictationKey = vk; break;
                case CaptureTarget.DictationKey2: DictationKey2 = vk; break;
                case CaptureTarget.DictationToggleKey: DictationToggleKey = vk; break;
                case CaptureTarget.DictationToggleKey2: DictationToggleKey2 = vk; break;
            }
        }

        /// <summary>Get the key for a specific target.</summary>
        public int GetKeyForTarget(CaptureTarget target)
        {
            switch (target)
            {
                case CaptureTarget.PttKey1: return PttKey;
                case CaptureTarget.PttKey2: return PttKey2;
                case CaptureTarget.PtmKey1: return PtmKey;
                case CaptureTarget.PtmKey2: return PtmKey2;
                case CaptureTarget.ToggleKey1: return PtToggleKey;
                case CaptureTarget.ToggleKey2: return PtToggleKey2;
                case CaptureTarget.MmKey: return MmKey;
                case CaptureTarget.MmKey2: return MmKey2;
                case CaptureTarget.DictationKey1: return DictationKey;
                case CaptureTarget.DictationKey2: return DictationKey2;
                case CaptureTarget.DictationToggleKey: return DictationToggleKey;
                case CaptureTarget.DictationToggleKey2: return DictationToggleKey2;
            }
            return 0;
        }
    }
}
