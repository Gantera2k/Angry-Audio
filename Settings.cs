using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace AngryAudio
{
    public class Settings
    {
        // Mic enforcement
        public bool MicEnforceEnabled { get; set; }
        public int MicVolumePercent { get; set; }
        public int MicEnforceIntervalSec { get; set; }

        // Speaker enforcement
        public bool SpeakerEnforceEnabled { get; set; }
        public int SpeakerVolumePercent { get; set; }
        public int SpeakerEnforceIntervalSec { get; set; }

        // AFK mute
        public bool AfkMicMuteEnabled { get; set; }
        public int AfkMicMuteSec { get; set; }
        public bool AfkSpeakerMuteEnabled { get; set; }
        public int AfkSpeakerMuteSec { get; set; }

        // General
        public bool StartWithWindows { get; set; }
        public bool FirstRunComplete { get; set; }
        public bool NotifyOnCorrection { get; set; }
        public bool NotifyOnDeviceChange { get; set; }
        public bool RestrictSoundOutput { get; set; }

        // Push-to-talk
        public bool PushToTalkEnabled { get; set; }
        public int PushToTalkKey { get; set; }
        public int PushToTalkKey2 { get; set; }
        public int PushToMuteKey { get; set; }
        public int PushToMuteKey2 { get; set; }
        public int PushToToggleKey { get; set; }
        public int PushToToggleKey2 { get; set; }
        public int MultiModeKey { get; set; }
        public int MultiModeKey2 { get; set; }
        public bool PttKey1ShowOverlay { get; set; }
        public bool PttKey2ShowOverlay { get; set; }
        public bool PtmShowOverlay { get; set; }
        public bool PtToggleShowOverlay { get; set; }
        public bool PttSuppressEnabled { get; set; }
        public bool PtmSuppressEnabled { get; set; }
        public bool PtToggleSuppressEnabled { get; set; }
        public bool PushToToggleEnabled { get; set; }
        public bool PushToMuteEnabled { get; set; }
        public bool MicOverlayEnabled { get; set; }
        public int MicOverlayX { get; set; }
        public int MicOverlayY { get; set; }
        public bool PttSoundFeedback { get; set; }
        public bool PtmSoundFeedback { get; set; }
        public bool PtToggleSoundFeedback { get; set; }
        public int SoundFeedbackType { get; set; } // 0=soft click, 1=double tap, 2=chirp, 3=radio, 4=chime, 5=subtle pop, 6=custom
        public int PttSoundType { get; set; }       // per-segment sound type overrides
        public int PtmSoundType { get; set; }
        public int PtToggleSoundType { get; set; }
        public int DictSoundType { get; set; }
        public string CustomSoundPath { get; set; }
        public int PttSoundVolume { get; set; }     // 0-100, per-segment sound effect volume
        public int PtmSoundVolume { get; set; }
        public int PtToggleSoundVolume { get; set; }
        public int DictSoundVolume { get; set; }

        // Per-app volume enforcement
        // Key = process name (lowercase, no .exe), Value = target volume percent
        public Dictionary<string, int> AppVolumeRules { get; set; }
        public bool AppVolumeEnforceEnabled { get; set; }
        public int AppVolumeEnforceIntervalSec { get; set; }
        public int LastActivePane { get; set; }
        public int LastWindowX { get; set; }
        public int LastWindowY { get; set; }
        public bool DictationEnabled { get; set; }       // push-to-hold active (Whisper only)
        public bool DictationToggleEnabled { get; set; }  // push-to-toggle active (both engines)
        public bool DictPthSuppressEnabled { get; set; }  // passes native key downstream
        public bool DictPttSuppressEnabled { get; set; }  // passes native key downstream
        public int DictationEngine { get; set; }
        public int DictationMode { get; set; }
        public int DictationKey { get; set; }        // push-to-hold primary
        public int DictationKey2 { get; set; }          // push-to-hold secondary
        public int DictationToggleKey { get; set; }       // push-to-toggle primary
        public int DictationToggleKey2 { get; set; }      // push-to-toggle secondary
        public int DictationWhisperModel { get; set; }
        public bool DictSoundFeedback { get; set; }
        public bool DictShowOverlay { get; set; }
        public bool DictDuckingEnabled { get; set; }   // Lower system volume during dictation
        public int  DictDuckingVolume { get; set; }    // Target volume percent during ducking (0-100)

        // Per-segment duck flags (rubber duck icon active)
        public bool PttDuckEnabled { get; set; }
        public bool PtmDuckEnabled { get; set; }
        public bool PtToggleDuckEnabled { get; set; }
        public bool DictPthDuckEnabled { get; set; }
        public bool DictPttDuckEnabled { get; set; }
        public int  PttDuckingVolume { get; set; }     // Target volume percent for PTT/PTM/Toggle ducking

        // Voice Activity
        public bool VoiceActivityEnabled { get; set; }
        public int VoiceActivityThreshold { get; set; }   // 0-100 (percentage of max peak)
        public int VoiceActivityHoldoverMs { get; set; }   // 200-5000ms
        public bool VoiceActivityShowOverlay { get; set; }
        public bool VoiceActivitySoundFeedback { get; set; }

        // Night Mode (Loudness Equalization)
        public bool NightModeEnabled { get; set; }
        public int NightModeReleaseTime { get; set; } // 2 (fast/movie) to 7 (slow/music)

        // Equalizer enhancements
        public string EqBands { get; set; }  // pipe-separated 10 floats 0.0-1.0 e.g. "0.5|0.5|..."

        // Display (color temperature + brightness + color filter)
        public bool DisplayEnabled { get; set; }
        public int DisplayTempK { get; set; }
        public int DisplayBrightness { get; set; }
        public bool DisplayGrayscale { get; set; }  // legacy — superseded by DisplayFilterType
        public bool DisplayInvert { get; set; }     // legacy — superseded by DisplayFilterType
        public int DisplayFilterType { get; set; }   // -1=off, 0=Grayscale, 1=Inverted, 2=GrayscaleInv, 3=Deuter, 4=Protan, 5=Tritan
        public int DisplayColorIntensity { get; set; }  // 0-100
        public int DisplayColorBoost { get; set; }      // 0-100
        public string DisplayPreset { get; set; }
        /// <summary>Pipe-separated per-monitor overrides: "\\\\.\\ DISPLAY1:4500:80|\\\\.\\ DISPLAY2:6500:100".
        /// Empty = all monitors use the global DisplayTempK / DisplayBrightness.</summary>
        public string DisplayMonitorSettings { get; set; }

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Angry Audio");

        private static readonly string SettingsFilePath = Path.Combine(AppDataDir, "settings.json");
        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "Angry Audio";

        public Settings()
        {
            ApplyDefaults();
        }

        public void ApplyDefaults()
        {
            MicEnforceEnabled = true;
            MicVolumePercent = 100;
            MicEnforceIntervalSec = 60;
            SpeakerEnforceEnabled = false;
            SpeakerVolumePercent = 100;
            SpeakerEnforceIntervalSec = 60;
            AfkMicMuteEnabled = false;
            AfkMicMuteSec = 60;
            AfkSpeakerMuteEnabled = false;
            AfkSpeakerMuteSec = 60;
            StartWithWindows = true;
            FirstRunComplete = false;
            NotifyOnCorrection = true;
            NotifyOnDeviceChange = true;
            RestrictSoundOutput = false;
            PushToTalkEnabled = false;
            PushToTalkKey = 0;  // No default hotkey — user must set one
            PushToTalkKey2 = 0;  // disabled by default
            PushToMuteKey = 0;
            PushToMuteKey2 = 0;
            PushToToggleKey = 0;
            PushToToggleKey2 = 0;
            MultiModeKey = 0;
            MultiModeKey2 = 0;
            PttKey1ShowOverlay = true;
            PttKey2ShowOverlay = true;
            PtmShowOverlay = true;
            PtToggleShowOverlay = true;
            PttSuppressEnabled = false;
            PtmSuppressEnabled = false;
            PtToggleSuppressEnabled = false;
            PushToToggleEnabled = false;
            PushToMuteEnabled = false;
            MicOverlayEnabled = true;
            MicOverlayX = -1;
            MicOverlayY = -1;
            PttSoundFeedback = false;
            PtmSoundFeedback = false;
            PtToggleSoundFeedback = false;
            SoundFeedbackType = 0;
            PttSoundType = 0;
            PtmSoundType = 0;
            PtToggleSoundType = 0;
            DictSoundType = 0;
            CustomSoundPath = "";
            PttSoundVolume = 50;
            PtmSoundVolume = 50;
            PtToggleSoundVolume = 50;
            DictSoundVolume = 50;
            AppVolumeRules = new Dictionary<string, int>();
            AppVolumeEnforceEnabled = false;
            AppVolumeEnforceIntervalSec = 5;
            LastWindowX = -1;
            LastWindowY = -1;
            DictationEnabled = false;
            DictationToggleEnabled = false;
            DictPthSuppressEnabled = false;
            DictPttSuppressEnabled = false;
            DictationKey2 = 0;
            DictationToggleKey2 = 0;
            DictationEngine = 1;
            DictationMode = 0;
            DictationKey = 0;
            DictationToggleKey = 0;
            DictationWhisperModel = 0;
            DictSoundFeedback = false;
            DictShowOverlay = true;
            DictDuckingEnabled = false;
            DictDuckingVolume = 20;
            PttDuckEnabled = false;
            PtmDuckEnabled = false;
            PtToggleDuckEnabled = false;
            DictPthDuckEnabled = false;
            DictPttDuckEnabled = false;
            PttDuckingVolume = 20;
            VoiceActivityEnabled = false;
            VoiceActivityThreshold = 15;
            VoiceActivityHoldoverMs = 2000;
            VoiceActivityShowOverlay = true;
            VoiceActivitySoundFeedback = false;
            NightModeEnabled = false;
            NightModeReleaseTime = 4;

            EqBands = "0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5";

            DisplayEnabled = false;
            DisplayTempK = 6500;
            DisplayBrightness = 100;
            DisplayGrayscale = false;
            DisplayInvert = false;
            DisplayFilterType = -1;
            DisplayColorIntensity = 100;
            DisplayColorBoost = 75;
            DisplayPreset = "Normal";
        }

        public void Validate()
        {
            MicVolumePercent = Clamp(MicVolumePercent, 0, 100);
            MicEnforceIntervalSec = Clamp(MicEnforceIntervalSec, 1, 3600);
            SpeakerVolumePercent = Clamp(SpeakerVolumePercent, 0, 100);
            SpeakerEnforceIntervalSec = Clamp(SpeakerEnforceIntervalSec, 1, 3600);
            AfkMicMuteSec = Clamp(AfkMicMuteSec, 1, 3600);
            AfkSpeakerMuteSec = Clamp(AfkSpeakerMuteSec, 1, 3600);
            VoiceActivityThreshold = Clamp(VoiceActivityThreshold, 0, 100);
            VoiceActivityHoldoverMs = Clamp(VoiceActivityHoldoverMs, 200, 5000);
            NightModeReleaseTime = Clamp(NightModeReleaseTime, 2, 7);
            PttSoundVolume = Clamp(PttSoundVolume, 0, 100);
            PtmSoundVolume = Clamp(PtmSoundVolume, 0, 100);
            PtToggleSoundVolume = Clamp(PtToggleSoundVolume, 0, 100);
            DictSoundVolume = Clamp(DictSoundVolume, 0, 100);
            DictDuckingVolume = Clamp(DictDuckingVolume, 0, 100);
            PttDuckingVolume = Clamp(PttDuckingVolume, 0, 100);
            DisplayTempK = Clamp(DisplayTempK, 1200, 6500);
            DisplayBrightness = Clamp(DisplayBrightness, 20, 100);
            DisplayColorIntensity = Clamp(DisplayColorIntensity, 0, 100);
            DisplayColorBoost = Clamp(DisplayColorBoost, 0, 100);
            DisplayFilterType = Clamp(DisplayFilterType, -1, 5);
            // Always clear per-monitor overrides on startup to avoid stale device-specific limits
            DisplayMonitorSettings = "";

            // Startup sanitization: clear hotkeys for disabled segments
            // Prevents stale keys from a previous session showing when toggles are off
            if (!PushToTalkEnabled) { PushToTalkKey = 0; PushToTalkKey2 = 0; }
            if (!PushToMuteEnabled) { PushToMuteKey = 0; PushToMuteKey2 = 0; }
            if (!PushToToggleEnabled) { PushToToggleKey = 0; PushToToggleKey2 = 0; MultiModeKey = 0; MultiModeKey2 = 0; }
            // MM keys require Toggle — clear if Toggle is off even when MM was saved
            if (!PushToToggleEnabled) { MultiModeKey = 0; MultiModeKey2 = 0; }
            if (!DictationEnabled) { DictationKey = 0; DictationKey2 = 0; }
            if (!DictationToggleEnabled) { DictationToggleKey = 0; DictationToggleKey2 = 0; }
        }

        // --- Persistence ---

        public static Settings Load()
        {
            var settings = new Settings();

            if (!File.Exists(SettingsFilePath))
            {
                Logger.Info("No settings file found. Using defaults.");
                return settings;
            }

            try
            {
                string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                var values = ParseJson(json);

                // Version check — if missing or < 2, reset to v2 defaults
                int ver = 0;
                if (values.ContainsKey("settingsVersion")) ver = ParseInt(values["settingsVersion"], 0);
                if (ver < 2)
                {
                    Logger.Info("Old settings version " + ver + " found. Resetting to v2 defaults.");
                    // Keep firstRunComplete so welcome doesn't show again
                    if (values.ContainsKey("firstRunComplete")) settings.FirstRunComplete = ParseBool(values["firstRunComplete"]);
                    settings.Save();
                    return settings;
                }

                if (values.ContainsKey("micEnforceEnabled")) settings.MicEnforceEnabled = ParseBool(values["micEnforceEnabled"]);
                if (values.ContainsKey("micVolumePercent")) settings.MicVolumePercent = ParseInt(values["micVolumePercent"], 100);
                if (values.ContainsKey("micEnforceIntervalSec")) settings.MicEnforceIntervalSec = ParseInt(values["micEnforceIntervalSec"], 60);
                if (values.ContainsKey("speakerEnforceEnabled")) settings.SpeakerEnforceEnabled = ParseBool(values["speakerEnforceEnabled"]);
                if (values.ContainsKey("speakerVolumePercent")) settings.SpeakerVolumePercent = ParseInt(values["speakerVolumePercent"], 100);
                if (values.ContainsKey("speakerEnforceIntervalSec")) settings.SpeakerEnforceIntervalSec = ParseInt(values["speakerEnforceIntervalSec"], 60);
                if (values.ContainsKey("afkMicMuteEnabled")) settings.AfkMicMuteEnabled = ParseBool(values["afkMicMuteEnabled"]);
                if (values.ContainsKey("afkMicMuteSec")) settings.AfkMicMuteSec = ParseInt(values["afkMicMuteSec"], 60);
                if (values.ContainsKey("afkSpeakerMuteEnabled")) settings.AfkSpeakerMuteEnabled = ParseBool(values["afkSpeakerMuteEnabled"]);
                if (values.ContainsKey("afkSpeakerMuteSec")) settings.AfkSpeakerMuteSec = ParseInt(values["afkSpeakerMuteSec"], 60);
                if (values.ContainsKey("startWithWindows")) settings.StartWithWindows = ParseBool(values["startWithWindows"]);
                if (values.ContainsKey("firstRunComplete")) settings.FirstRunComplete = ParseBool(values["firstRunComplete"]);
                if (values.ContainsKey("notifyOnCorrection")) settings.NotifyOnCorrection = ParseBool(values["notifyOnCorrection"]);
                if (values.ContainsKey("notifyOnDeviceChange")) settings.NotifyOnDeviceChange = ParseBool(values["notifyOnDeviceChange"]);
            if (values.ContainsKey("restrictSoundOutput")) settings.RestrictSoundOutput = ParseBool(values["restrictSoundOutput"]);
                if (values.ContainsKey("pushToTalkEnabled")) settings.PushToTalkEnabled = ParseBool(values["pushToTalkEnabled"]);
                if (values.ContainsKey("pushToTalkKey")) settings.PushToTalkKey = ParseInt(values["pushToTalkKey"], 0x14);
                if (values.ContainsKey("pushToTalkKey2")) settings.PushToTalkKey2 = ParseInt(values["pushToTalkKey2"], 0);
                if (values.ContainsKey("pttKey1ShowOverlay")) settings.PttKey1ShowOverlay = ParseBool(values["pttKey1ShowOverlay"]);
                if (values.ContainsKey("pttKey2ShowOverlay")) settings.PttKey2ShowOverlay = ParseBool(values["pttKey2ShowOverlay"]);
                if (values.ContainsKey("ptmShowOverlay")) settings.PtmShowOverlay = ParseBool(values["ptmShowOverlay"]);
                if (values.ContainsKey("ptToggleShowOverlay")) settings.PtToggleShowOverlay = ParseBool(values["ptToggleShowOverlay"]);
                if (values.ContainsKey("pushToTalkConsumeKey")) {
                    bool p = ParseBool(values["pushToTalkConsumeKey"]);
                    settings.PttSuppressEnabled = p;
                    settings.PtmSuppressEnabled = p;
                    settings.PtToggleSuppressEnabled = p;
                }
                if (values.ContainsKey("pttSuppressEnabled")) settings.PttSuppressEnabled = ParseBool(values["pttSuppressEnabled"]);
                if (values.ContainsKey("ptmSuppressEnabled")) settings.PtmSuppressEnabled = ParseBool(values["ptmSuppressEnabled"]);
                if (values.ContainsKey("ptToggleSuppressEnabled")) settings.PtToggleSuppressEnabled = ParseBool(values["ptToggleSuppressEnabled"]);
                if (values.ContainsKey("pushToToggleEnabled")) settings.PushToToggleEnabled = ParseBool(values["pushToToggleEnabled"]);
                if (values.ContainsKey("pushToMuteEnabled")) settings.PushToMuteEnabled = ParseBool(values["pushToMuteEnabled"]);
                if (values.ContainsKey("pushToMuteKey")) settings.PushToMuteKey = ParseInt(values["pushToMuteKey"], 0);
                if (values.ContainsKey("pushToMuteKey2")) settings.PushToMuteKey2 = ParseInt(values["pushToMuteKey2"], 0);
                if (values.ContainsKey("pushToToggleKey")) settings.PushToToggleKey = ParseInt(values["pushToToggleKey"], 0);
                if (values.ContainsKey("pushToToggleKey2")) settings.PushToToggleKey2 = ParseInt(values["pushToToggleKey2"], 0);
                if (values.ContainsKey("multiModeKey")) settings.MultiModeKey = ParseInt(values["multiModeKey"], 0);
                if (values.ContainsKey("multiModeKey2")) settings.MultiModeKey2 = ParseInt(values["multiModeKey2"], 0);
                if (values.ContainsKey("micOverlayEnabled")) settings.MicOverlayEnabled = ParseBool(values["micOverlayEnabled"]);
                if (values.ContainsKey("micOverlayX")) settings.MicOverlayX = ParseInt(values["micOverlayX"], -1);
                if (values.ContainsKey("micOverlayY")) settings.MicOverlayY = ParseInt(values["micOverlayY"], -1);
                if (values.ContainsKey("pttSoundFeedback")) settings.PttSoundFeedback = ParseBool(values["pttSoundFeedback"]);
                if (values.ContainsKey("ptmSoundFeedback")) settings.PtmSoundFeedback = ParseBool(values["ptmSoundFeedback"]);
                if (values.ContainsKey("ptToggleSoundFeedback")) settings.PtToggleSoundFeedback = ParseBool(values["ptToggleSoundFeedback"]);
                if (values.ContainsKey("soundFeedbackType")) settings.SoundFeedbackType = ParseInt(values["soundFeedbackType"], 0);
                // Per-segment sound type — defaults to global SoundFeedbackType for backward compat
                int globalType = settings.SoundFeedbackType;
                if (values.ContainsKey("pttSoundType")) settings.PttSoundType = ParseInt(values["pttSoundType"], globalType);
                else settings.PttSoundType = globalType;
                if (values.ContainsKey("ptmSoundType")) settings.PtmSoundType = ParseInt(values["ptmSoundType"], globalType);
                else settings.PtmSoundType = globalType;
                if (values.ContainsKey("ptToggleSoundType")) settings.PtToggleSoundType = ParseInt(values["ptToggleSoundType"], globalType);
                else settings.PtToggleSoundType = globalType;
                if (values.ContainsKey("dictSoundType")) settings.DictSoundType = ParseInt(values["dictSoundType"], globalType);
                else settings.DictSoundType = globalType;
                if (values.ContainsKey("customSoundPath")) settings.CustomSoundPath = values["customSoundPath"].Trim(new char[]{'"'});
                if (values.ContainsKey("pttSoundVolume")) settings.PttSoundVolume = ParseInt(values["pttSoundVolume"], 80);
                if (values.ContainsKey("ptmSoundVolume")) settings.PtmSoundVolume = ParseInt(values["ptmSoundVolume"], 80);
                if (values.ContainsKey("ptToggleSoundVolume")) settings.PtToggleSoundVolume = ParseInt(values["ptToggleSoundVolume"], 80);
                if (values.ContainsKey("dictSoundVolume")) settings.DictSoundVolume = ParseInt(values["dictSoundVolume"], 80);
                if (values.ContainsKey("appVolumeEnforceEnabled")) settings.AppVolumeEnforceEnabled = ParseBool(values["appVolumeEnforceEnabled"]);
                if (values.ContainsKey("appVolumeEnforceIntervalSec")) settings.AppVolumeEnforceIntervalSec = ParseInt(values["appVolumeEnforceIntervalSec"], 5);
                if (values.ContainsKey("appVolumeRules")) settings.AppVolumeRules = ParseAppRules(values["appVolumeRules"]);
                if (values.ContainsKey("lastWindowX")) settings.LastWindowX = ParseInt(values["lastWindowX"], -1);
                if (values.ContainsKey("lastWindowY")) settings.LastWindowY = ParseInt(values["lastWindowY"], -1);
                if (values.ContainsKey("lastActivePane")) settings.LastActivePane = ParseInt(values["lastActivePane"], 0);
                if (values.ContainsKey("voiceActivityEnabled")) settings.VoiceActivityEnabled = ParseBool(values["voiceActivityEnabled"]);
                if (values.ContainsKey("voiceActivityThreshold")) settings.VoiceActivityThreshold = ParseInt(values["voiceActivityThreshold"], 15);
                if (values.ContainsKey("voiceActivityHoldoverMs")) settings.VoiceActivityHoldoverMs = ParseInt(values["voiceActivityHoldoverMs"], 2000);
                if (values.ContainsKey("voiceActivityShowOverlay")) settings.VoiceActivityShowOverlay = ParseBool(values["voiceActivityShowOverlay"]);
                if (values.ContainsKey("voiceActivitySoundFeedback")) settings.VoiceActivitySoundFeedback = ParseBool(values["voiceActivitySoundFeedback"]);
                if (values.ContainsKey("dictationEnabled")) settings.DictationEnabled = ParseBool(values["dictationEnabled"]);
                if (values.ContainsKey("dictationToggleEnabled")) settings.DictationToggleEnabled = ParseBool(values["dictationToggleEnabled"]);
                
                // --- Migration for Dictation Suppression ---
                if (values.ContainsKey("dictationSuppressEnabled")) {
                    bool fallback = ParseBool(values["dictationSuppressEnabled"]);
                    settings.DictPthSuppressEnabled = fallback;
                    settings.DictPttSuppressEnabled = fallback;
                } else {
                    if (values.ContainsKey("dictPthSuppressEnabled")) settings.DictPthSuppressEnabled = ParseBool(values["dictPthSuppressEnabled"]);
                    else settings.DictPthSuppressEnabled = false;
                    
                    if (values.ContainsKey("dictPttSuppressEnabled")) settings.DictPttSuppressEnabled = ParseBool(values["dictPttSuppressEnabled"]);
                    else settings.DictPttSuppressEnabled = false;
                }
                
                if (values.ContainsKey("dictationKey2")) settings.DictationKey2 = ParseInt(values["dictationKey2"], 0);
                if (values.ContainsKey("dictationToggleKey2")) settings.DictationToggleKey2 = ParseInt(values["dictationToggleKey2"], 0);
                if (values.ContainsKey("dictationEngine")) settings.DictationEngine = ParseInt(values["dictationEngine"], 0);
                if (values.ContainsKey("dictationMode")) settings.DictationMode = ParseInt(values["dictationMode"], 0);
                if (values.ContainsKey("dictationKey")) settings.DictationKey = ParseInt(values["dictationKey"], 0);
                if (values.ContainsKey("dictationToggleKey")) settings.DictationToggleKey = ParseInt(values["dictationToggleKey"], 0);
                if (values.ContainsKey("dictationWhisperModel")) settings.DictationWhisperModel = ParseInt(values["dictationWhisperModel"], 0);
                if (values.ContainsKey("dictSoundFeedback")) settings.DictSoundFeedback = ParseBool(values["dictSoundFeedback"]);
                if (values.ContainsKey("dictShowOverlay")) settings.DictShowOverlay = ParseBool(values["dictShowOverlay"]);
                if (values.ContainsKey("dictDuckingEnabled")) settings.DictDuckingEnabled = ParseBool(values["dictDuckingEnabled"]);
                if (values.ContainsKey("dictDuckingVolume")) settings.DictDuckingVolume = ParseInt(values["dictDuckingVolume"], 20);
                if (values.ContainsKey("pttDuckEnabled")) settings.PttDuckEnabled = ParseBool(values["pttDuckEnabled"]);
                if (values.ContainsKey("ptmDuckEnabled")) settings.PtmDuckEnabled = ParseBool(values["ptmDuckEnabled"]);
                if (values.ContainsKey("ptToggleDuckEnabled")) settings.PtToggleDuckEnabled = ParseBool(values["ptToggleDuckEnabled"]);
                if (values.ContainsKey("dictPthDuckEnabled")) settings.DictPthDuckEnabled = ParseBool(values["dictPthDuckEnabled"]);
                if (values.ContainsKey("dictPttDuckEnabled")) settings.DictPttDuckEnabled = ParseBool(values["dictPttDuckEnabled"]);
                if (values.ContainsKey("pttDuckingVolume")) settings.PttDuckingVolume = ParseInt(values["pttDuckingVolume"], 20);
                if (values.ContainsKey("nightModeEnabled")) settings.NightModeEnabled = ParseBool(values["nightModeEnabled"]);
                if (values.ContainsKey("nightModeReleaseTime")) settings.NightModeReleaseTime = ParseInt(values["nightModeReleaseTime"], 4);
                if (values.ContainsKey("eqBands")) settings.EqBands = values["eqBands"].Trim(new char[]{'"'});
                if (values.ContainsKey("displayEnabled")) settings.DisplayEnabled = ParseBool(values["displayEnabled"]);
                if (values.ContainsKey("displayTempK")) settings.DisplayTempK = ParseInt(values["displayTempK"], 6500);
                if (values.ContainsKey("displayBrightness")) settings.DisplayBrightness = ParseInt(values["displayBrightness"], 100);
                if (values.ContainsKey("displayFilterType")) settings.DisplayFilterType = ParseInt(values["displayFilterType"], -1);
                if (values.ContainsKey("displayColorIntensity")) settings.DisplayColorIntensity = ParseInt(values["displayColorIntensity"], 100);
                if (values.ContainsKey("displayColorBoost")) settings.DisplayColorBoost = ParseInt(values["displayColorBoost"], 75);
                if (values.ContainsKey("displayGrayscale")) settings.DisplayGrayscale = ParseBool(values["displayGrayscale"]);
                if (values.ContainsKey("displayInvert")) settings.DisplayInvert = ParseBool(values["displayInvert"]);
                if (values.ContainsKey("displayPreset")) settings.DisplayPreset = values["displayPreset"].Trim(new char[]{'"'});
                if (values.ContainsKey("displayMonitorSettings")) settings.DisplayMonitorSettings = values["displayMonitorSettings"].Trim(new char[]{'"'});

                settings.Validate();
                Logger.Info("Settings loaded from " + SettingsFilePath);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings. Backing up corrupt file and resetting to defaults.", ex);
                settings = new Settings();
                BackupCorruptFile();
            }

            // Migration: old default was Tilde (0xC0) — spams characters in text fields.
            // Migrate to no hotkey (user must set one explicitly).
            if (settings.PushToTalkKey == 0xC0)
            {
                settings.PushToTalkKey = 0;
                try { settings.Save(); } catch { }
            }

            return settings;
        }

        public void Save()
        {
            Validate();

            try
            {
                if (!Directory.Exists(AppDataDir))
                    Directory.CreateDirectory(AppDataDir);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine(JsonField("settingsVersion", 2) + ",");
                sb.AppendLine(JsonField("micEnforceEnabled", MicEnforceEnabled) + ",");
                sb.AppendLine(JsonField("micVolumePercent", MicVolumePercent) + ",");
                sb.AppendLine(JsonField("micEnforceIntervalSec", MicEnforceIntervalSec) + ",");
                sb.AppendLine(JsonField("speakerEnforceEnabled", SpeakerEnforceEnabled) + ",");
                sb.AppendLine(JsonField("speakerVolumePercent", SpeakerVolumePercent) + ",");
                sb.AppendLine(JsonField("speakerEnforceIntervalSec", SpeakerEnforceIntervalSec) + ",");
                sb.AppendLine(JsonField("afkMicMuteEnabled", AfkMicMuteEnabled) + ",");
                sb.AppendLine(JsonField("afkMicMuteSec", AfkMicMuteSec) + ",");
                sb.AppendLine(JsonField("afkSpeakerMuteEnabled", AfkSpeakerMuteEnabled) + ",");
                sb.AppendLine(JsonField("afkSpeakerMuteSec", AfkSpeakerMuteSec) + ",");
                sb.AppendLine(JsonField("startWithWindows", StartWithWindows) + ",");
                sb.AppendLine(JsonField("firstRunComplete", FirstRunComplete) + ",");
                sb.AppendLine(JsonField("notifyOnCorrection", NotifyOnCorrection) + ",");
                sb.AppendLine(JsonField("notifyOnDeviceChange", NotifyOnDeviceChange) + ",");
            sb.AppendLine(JsonField("restrictSoundOutput", RestrictSoundOutput) + ",");
                sb.AppendLine(JsonField("pushToTalkEnabled", PushToTalkEnabled) + ",");
                sb.AppendLine(JsonField("pushToTalkKey", PushToTalkKey) + ",");
                sb.AppendLine(JsonField("pushToTalkKey2", PushToTalkKey2) + ",");
                sb.AppendLine(JsonField("pttKey1ShowOverlay", PttKey1ShowOverlay) + ",");
                sb.AppendLine(JsonField("pttKey2ShowOverlay", PttKey2ShowOverlay) + ",");
                sb.AppendLine(JsonField("ptmShowOverlay", PtmShowOverlay) + ",");
                sb.AppendLine(JsonField("ptToggleShowOverlay", PtToggleShowOverlay) + ",");
                sb.AppendLine(JsonField("pttSuppressEnabled", PttSuppressEnabled) + ",");
                sb.AppendLine(JsonField("ptmSuppressEnabled", PtmSuppressEnabled) + ",");
                sb.AppendLine(JsonField("ptToggleSuppressEnabled", PtToggleSuppressEnabled) + ",");
                sb.AppendLine(JsonField("pushToToggleEnabled", PushToToggleEnabled) + ",");
                sb.AppendLine(JsonField("pushToMuteEnabled", PushToMuteEnabled) + ",");
                sb.AppendLine(JsonField("pushToMuteKey", PushToMuteKey) + ",");
                sb.AppendLine(JsonField("pushToMuteKey2", PushToMuteKey2) + ",");
                sb.AppendLine(JsonField("pushToToggleKey", PushToToggleKey) + ",");
                sb.AppendLine(JsonField("pushToToggleKey2", PushToToggleKey2) + ",");
                sb.AppendLine(JsonField("multiModeKey", MultiModeKey) + ",");
                sb.AppendLine(JsonField("multiModeKey2", MultiModeKey2) + ",");
                sb.AppendLine(JsonField("micOverlayEnabled", MicOverlayEnabled) + ",");
                sb.AppendLine(JsonField("micOverlayX", MicOverlayX) + ",");
                sb.AppendLine(JsonField("micOverlayY", MicOverlayY) + ",");
                sb.AppendLine(JsonField("pttSoundFeedback", PttSoundFeedback) + ",");
                sb.AppendLine(JsonField("ptmSoundFeedback", PtmSoundFeedback) + ",");
                sb.AppendLine(JsonField("ptToggleSoundFeedback", PtToggleSoundFeedback) + ",");
                sb.AppendLine(JsonField("soundFeedbackType", SoundFeedbackType) + ",");
                sb.AppendLine(JsonField("pttSoundType", PttSoundType) + ",");
                sb.AppendLine(JsonField("ptmSoundType", PtmSoundType) + ",");
                sb.AppendLine(JsonField("ptToggleSoundType", PtToggleSoundType) + ",");
                sb.AppendLine(JsonField("dictSoundType", DictSoundType) + ",");
                sb.AppendLine(JsonFieldStr("customSoundPath", CustomSoundPath ?? "") + ",");
                sb.AppendLine(JsonField("pttSoundVolume", PttSoundVolume) + ",");
                sb.AppendLine(JsonField("ptmSoundVolume", PtmSoundVolume) + ",");
                sb.AppendLine(JsonField("ptToggleSoundVolume", PtToggleSoundVolume) + ",");
                sb.AppendLine(JsonField("dictSoundVolume", DictSoundVolume) + ",");
                sb.AppendLine(JsonField("appVolumeEnforceEnabled", AppVolumeEnforceEnabled) + ",");
                sb.AppendLine(JsonField("appVolumeEnforceIntervalSec", AppVolumeEnforceIntervalSec) + ",");
                sb.AppendLine(JsonField("lastWindowX", LastWindowX) + ",");
                sb.AppendLine(JsonField("lastWindowY", LastWindowY) + ",");
                sb.AppendLine(JsonField("lastActivePane", LastActivePane) + ",");
                sb.AppendLine(JsonField("voiceActivityEnabled", VoiceActivityEnabled) + ",");
                sb.AppendLine(JsonField("voiceActivityThreshold", VoiceActivityThreshold) + ",");
                sb.AppendLine(JsonField("voiceActivityHoldoverMs", VoiceActivityHoldoverMs) + ",");
                sb.AppendLine(JsonField("voiceActivityShowOverlay", VoiceActivityShowOverlay) + ",");
                sb.AppendLine(JsonField("voiceActivitySoundFeedback", VoiceActivitySoundFeedback) + ",");
                sb.AppendLine(JsonField("nightModeEnabled", NightModeEnabled) + ",");
                sb.AppendLine(JsonField("nightModeReleaseTime", NightModeReleaseTime) + ",");
                sb.AppendLine(JsonFieldStr("eqBands", EqBands ?? "0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5|0.5") + ",");
                sb.AppendLine(JsonField("dictationEnabled", DictationEnabled) + ",");
                sb.AppendLine(JsonField("dictationToggleEnabled", DictationToggleEnabled) + ",");
                sb.AppendLine(JsonField("dictPthSuppressEnabled", DictPthSuppressEnabled) + ",");
                sb.AppendLine(JsonField("dictPttSuppressEnabled", DictPttSuppressEnabled) + ",");
                sb.AppendLine(JsonField("dictationKey2", DictationKey2) + ",");
                sb.AppendLine(JsonField("dictationToggleKey2", DictationToggleKey2) + ",");
                sb.AppendLine(JsonField("dictationEngine", DictationEngine) + ",");
                sb.AppendLine(JsonField("dictationMode", DictationMode) + ",");
                sb.AppendLine(JsonField("dictationKey", DictationKey) + ",");
                sb.AppendLine(JsonField("dictationToggleKey", DictationToggleKey) + ",");
                sb.AppendLine(JsonField("dictationWhisperModel", DictationWhisperModel) + ",");
                sb.AppendLine(JsonField("dictSoundFeedback", DictSoundFeedback) + ",");
                sb.AppendLine(JsonField("dictShowOverlay", DictShowOverlay) + ",");
                sb.AppendLine(JsonField("dictDuckingEnabled", DictDuckingEnabled) + ",");
                sb.AppendLine(JsonField("dictDuckingVolume", DictDuckingVolume) + ",");
                sb.AppendLine(JsonField("pttDuckEnabled", PttDuckEnabled) + ",");
                sb.AppendLine(JsonField("ptmDuckEnabled", PtmDuckEnabled) + ",");
                sb.AppendLine(JsonField("ptToggleDuckEnabled", PtToggleDuckEnabled) + ",");
                sb.AppendLine(JsonField("dictPthDuckEnabled", DictPthDuckEnabled) + ",");
                sb.AppendLine(JsonField("dictPttDuckEnabled", DictPttDuckEnabled) + ",");
                sb.AppendLine(JsonField("pttDuckingVolume", PttDuckingVolume) + ",");
                sb.AppendLine(JsonField("displayEnabled", DisplayEnabled) + ",");
                sb.AppendLine(JsonField("displayTempK", DisplayTempK) + ",");
                sb.AppendLine(JsonField("displayBrightness", DisplayBrightness) + ",");
                sb.AppendLine(JsonField("displayFilterType", DisplayFilterType) + ",");
                sb.AppendLine(JsonField("displayColorIntensity", DisplayColorIntensity) + ",");
                sb.AppendLine(JsonField("displayColorBoost", DisplayColorBoost) + ",");
                sb.AppendLine(JsonField("displayGrayscale", DisplayGrayscale) + ",");
                sb.AppendLine(JsonField("displayInvert", DisplayInvert) + ",");
                sb.AppendLine(JsonFieldStr("displayPreset", DisplayPreset ?? "Normal") + ",");
                sb.AppendLine(JsonFieldStr("displayMonitorSettings", DisplayMonitorSettings ?? "") + ",");
                sb.AppendLine(JsonFieldStr("appVolumeRules", SerializeAppRules(AppVolumeRules)));
                sb.AppendLine("}");

                string jsonString = sb.ToString();
                System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        lock (_saveLock) {
                            File.WriteAllText(SettingsFilePath, jsonString, Encoding.UTF8);
                            Logger.Info("Settings saved to " + SettingsFilePath);
                        }
                    } catch (Exception ex) {
                        Logger.Error("Failed to save settings to disk.", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to build settings JSON.", ex);
            }
        }

        private static readonly object _saveLock = new object();

        // --- Windows Startup ---

        public void ApplyStartupSetting()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true))
                {
                    if (key == null) return;

                    if (StartWithWindows)
                    {
                        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        key.SetValue(StartupValueName, "\"" + exePath + "\"");
                        Logger.Info("Windows startup entry added.");
                    }
                    else
                    {
                        if (key.GetValue(StartupValueName) != null)
                        {
                            key.DeleteValue(StartupValueName, false);
                            Logger.Info("Windows startup entry removed.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update Windows startup setting.", ex);
            }
        }

        // --- Manual JSON Parser (no external dependencies) ---

        private static Dictionary<string, string> ParseJson(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Strip outer braces
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            // Split by comma-separated key:value pairs
            int i = 0;
            while (i < json.Length)
            {
                // Find key (quoted string)
                int keyStart = json.IndexOf('"', i);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find colon
                int colon = json.IndexOf(':', keyEnd + 1);
                if (colon < 0) break;

                // Find value (everything until next comma or end)
                int valueStart = colon + 1;
                int nextComma = json.IndexOf(',', valueStart);
                string value;
                if (nextComma >= 0)
                {
                    value = json.Substring(valueStart, nextComma - valueStart).Trim();
                    i = nextComma + 1;
                }
                else
                {
                    value = json.Substring(valueStart).Trim();
                    i = json.Length;
                }

                // Strip quotes from string values
                if (value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);

                result[key] = value;
            }

            return result;
        }

        private static bool ParseBool(string value)
        {
            return value != null && value.Trim().ToLowerInvariant() == "true";
        }

        private static int ParseInt(string value, int defaultValue)
        {
            int result;
            if (value != null && int.TryParse(value.Trim(), out result))
                return result;
            return defaultValue;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string JsonField(string key, bool value)
        {
            return string.Format("  \"{0}\": {1}", key, value ? "true" : "false");
        }

        private static string JsonField(string key, int value)
        {
            return string.Format("  \"{0}\": {1}", key, value);
        }

        private static string JsonFieldStr(string key, string value)
        {
            return string.Format("  \"{0}\": \"{1}\"", key, value.Replace("\"", "\\\""));
        }

        /// <summary>
        /// Serialize app rules to "chrome:80,discord:60" format.
        /// </summary>
        private static string SerializeAppRules(Dictionary<string, int> rules)
        {
            if (rules == null || rules.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kvp in rules)
                parts.Add(kvp.Key + ":" + kvp.Value);
            return string.Join(",", parts.ToArray());
        }

        /// <summary>
        /// Parse "chrome:80,discord:60" format to dictionary.
        /// </summary>
        private static Dictionary<string, int> ParseAppRules(string raw)
        {
            var rules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return rules;
            raw = raw.Trim().Trim(new char[] { '"' });
            if (string.IsNullOrEmpty(raw)) return rules;

            foreach (string part in raw.Split(new char[] { ',' }))
            {
                string trimmed = part.Trim();
                int colonIdx = trimmed.LastIndexOf(':');
                if (colonIdx <= 0) continue;

                // Preserve original case — OrdinalIgnoreCase comparer handles lookup
                string appName = trimmed.Substring(0, colonIdx).Trim();
                string volStr = trimmed.Substring(colonIdx + 1).Trim();
                int vol;
                if (int.TryParse(volStr, out vol))
                {
                    // Negative values encode "unlocked" state: -(volume+1)
                    // Positive values encode "locked" state: 0..100
                    if (vol >= 0)
                        vol = Math.Max(0, Math.Min(100, vol));
                    else
                        vol = Math.Max(-101, Math.Min(-1, vol));
                    rules[appName] = vol;
                }
            }
            return rules;
        }

        private static void BackupCorruptFile()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string backupPath = SettingsFilePath + ".corrupt";
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(SettingsFilePath, backupPath);
                    Logger.Warn("Corrupt settings file backed up to " + backupPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to backup corrupt settings file.", ex);
            }
        }

    }
}
