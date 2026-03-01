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

        // Push-to-talk
        public bool PushToTalkEnabled { get; set; }
        public int PushToTalkKey { get; set; }
        public int PushToTalkKey2 { get; set; }
        public int PushToTalkKey3 { get; set; }
        public bool PttKey1ShowOverlay { get; set; }
        public bool PttKey2ShowOverlay { get; set; }
        public bool PttKey3ShowOverlay { get; set; }
        public bool PushToTalkConsumeKey { get; set; }
        public bool PushToToggleEnabled { get; set; }
        public bool PushToMuteEnabled { get; set; }
        public bool MicOverlayEnabled { get; set; }

        // Per-app volume enforcement
        // Key = process name (lowercase, no .exe), Value = target volume percent
        public Dictionary<string, int> AppVolumeRules { get; set; }
        public bool AppVolumeEnforceEnabled { get; set; }
        public int AppVolumeEnforceIntervalSec { get; set; }

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
            AfkMicMuteEnabled = true;
            AfkMicMuteSec = 10;
            AfkSpeakerMuteEnabled = false;
            AfkSpeakerMuteSec = 10;
            StartWithWindows = true;
            FirstRunComplete = false;
            NotifyOnCorrection = true;
            NotifyOnDeviceChange = true;
            PushToTalkEnabled = false;
            PushToTalkKey = 0;  // No default hotkey — user must set one
            PushToTalkKey2 = 0;  // disabled by default
            PushToTalkKey3 = 0;
            PttKey1ShowOverlay = true;
            PttKey2ShowOverlay = true;
            PttKey3ShowOverlay = true;
            PushToTalkConsumeKey = false;
            PushToToggleEnabled = false;
            PushToMuteEnabled = false;
            MicOverlayEnabled = true;
            AppVolumeRules = new Dictionary<string, int>();
            AppVolumeEnforceEnabled = false;
            AppVolumeEnforceIntervalSec = 5;
        }

        public void Validate()
        {
            MicVolumePercent = Clamp(MicVolumePercent, 0, 100);
            MicEnforceIntervalSec = Clamp(MicEnforceIntervalSec, 1, 3600);
            SpeakerVolumePercent = Clamp(SpeakerVolumePercent, 0, 100);
            SpeakerEnforceIntervalSec = Clamp(SpeakerEnforceIntervalSec, 1, 3600);
            AfkMicMuteSec = Clamp(AfkMicMuteSec, 1, 3600);
            AfkSpeakerMuteSec = Clamp(AfkSpeakerMuteSec, 1, 3600);
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
                if (values.ContainsKey("afkMicMuteSec")) settings.AfkMicMuteSec = ParseInt(values["afkMicMuteSec"], 10);
                if (values.ContainsKey("afkSpeakerMuteEnabled")) settings.AfkSpeakerMuteEnabled = ParseBool(values["afkSpeakerMuteEnabled"]);
                if (values.ContainsKey("afkSpeakerMuteSec")) settings.AfkSpeakerMuteSec = ParseInt(values["afkSpeakerMuteSec"], 10);
                if (values.ContainsKey("startWithWindows")) settings.StartWithWindows = ParseBool(values["startWithWindows"]);
                if (values.ContainsKey("firstRunComplete")) settings.FirstRunComplete = ParseBool(values["firstRunComplete"]);
                if (values.ContainsKey("notifyOnCorrection")) settings.NotifyOnCorrection = ParseBool(values["notifyOnCorrection"]);
                if (values.ContainsKey("notifyOnDeviceChange")) settings.NotifyOnDeviceChange = ParseBool(values["notifyOnDeviceChange"]);
                if (values.ContainsKey("pushToTalkEnabled")) settings.PushToTalkEnabled = ParseBool(values["pushToTalkEnabled"]);
                if (values.ContainsKey("pushToTalkKey")) settings.PushToTalkKey = ParseInt(values["pushToTalkKey"], 0x14);
                if (values.ContainsKey("pushToTalkKey2")) settings.PushToTalkKey2 = ParseInt(values["pushToTalkKey2"], 0);
                if (values.ContainsKey("pushToTalkKey3")) settings.PushToTalkKey3 = ParseInt(values["pushToTalkKey3"], 0);
                if (values.ContainsKey("pttKey1ShowOverlay")) settings.PttKey1ShowOverlay = ParseBool(values["pttKey1ShowOverlay"]);
                if (values.ContainsKey("pttKey2ShowOverlay")) settings.PttKey2ShowOverlay = ParseBool(values["pttKey2ShowOverlay"]);
                if (values.ContainsKey("pttKey3ShowOverlay")) settings.PttKey3ShowOverlay = ParseBool(values["pttKey3ShowOverlay"]);
                if (values.ContainsKey("pushToTalkConsumeKey")) settings.PushToTalkConsumeKey = ParseBool(values["pushToTalkConsumeKey"]);
                if (values.ContainsKey("pushToToggleEnabled")) settings.PushToToggleEnabled = ParseBool(values["pushToToggleEnabled"]);
                if (values.ContainsKey("pushToMuteEnabled")) settings.PushToMuteEnabled = ParseBool(values["pushToMuteEnabled"]);
                if (values.ContainsKey("micOverlayEnabled")) settings.MicOverlayEnabled = ParseBool(values["micOverlayEnabled"]);
                if (values.ContainsKey("appVolumeEnforceEnabled")) settings.AppVolumeEnforceEnabled = ParseBool(values["appVolumeEnforceEnabled"]);
                if (values.ContainsKey("appVolumeEnforceIntervalSec")) settings.AppVolumeEnforceIntervalSec = ParseInt(values["appVolumeEnforceIntervalSec"], 5);
                if (values.ContainsKey("appVolumeRules")) settings.AppVolumeRules = ParseAppRules(values["appVolumeRules"]);

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
                sb.AppendLine(JsonField("pushToTalkEnabled", PushToTalkEnabled) + ",");
                sb.AppendLine(JsonField("pushToTalkKey", PushToTalkKey) + ",");
                sb.AppendLine(JsonField("pushToTalkKey2", PushToTalkKey2) + ",");
                sb.AppendLine(JsonField("pushToTalkKey3", PushToTalkKey3) + ",");
                sb.AppendLine(JsonField("pttKey1ShowOverlay", PttKey1ShowOverlay) + ",");
                sb.AppendLine(JsonField("pttKey2ShowOverlay", PttKey2ShowOverlay) + ",");
                sb.AppendLine(JsonField("pttKey3ShowOverlay", PttKey3ShowOverlay) + ",");
                sb.AppendLine(JsonField("pushToTalkConsumeKey", PushToTalkConsumeKey) + ",");
                sb.AppendLine(JsonField("pushToToggleEnabled", PushToToggleEnabled) + ",");
                sb.AppendLine(JsonField("pushToMuteEnabled", PushToMuteEnabled) + ",");
                sb.AppendLine(JsonField("micOverlayEnabled", MicOverlayEnabled) + ",");
                sb.AppendLine(JsonField("appVolumeEnforceEnabled", AppVolumeEnforceEnabled) + ",");
                sb.AppendLine(JsonField("appVolumeEnforceIntervalSec", AppVolumeEnforceIntervalSec) + ",");
                sb.AppendLine(JsonFieldStr("appVolumeRules", SerializeAppRules(AppVolumeRules)));
                sb.AppendLine("}");

                File.WriteAllText(SettingsFilePath, sb.ToString(), Encoding.UTF8);
                Logger.Info("Settings saved to " + SettingsFilePath);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save settings.", ex);
            }
        }

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
            if (int.TryParse(value?.Trim(), out result))
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
