# Angry Audio

**Take back control of your microphone and speakers.**

Angry Audio is a lightweight Windows desktop utility that gives you real, system-wide control over your audio. Push-to-talk across every app, volume lock so nothing changes without your permission, and AFK protection that mutes you when you step away.

No subscriptions. No telemetry. No ads. Just privacy.

---

## What It Does

### Push-to-Talk / Push-to-Mute / Push-to-Toggle
Set a single hotkey that controls **every microphone** on your system — not just one app. Your headset, webcam mic, USB mic — all of them, muted at the OS level until you say otherwise.

- **Push-to-Talk** — Silent until you hold the key. Just like Discord and most games.
- **Push-to-Mute** — Mic stays open. Hold the key to mute for coughs and sneezes.
- **Push-to-Toggle** — Tap once to unmute, tap again to mute. No holding needed.

### Volume Lock
Apps love to silently change your mic and speaker volume. Zoom turns you down. Discord resets to 50%. Some game blasts your speakers. Angry Audio locks your levels and snaps them back the instant anything changes.

### AFK Protection
Walk away from your desk? Your mic and speakers automatically mute after a timeout you set. When you come back, volume fades in smoothly — no jarring blast of sound.

### Mic Status Overlay
A small on-screen indicator shows whether your mic is hot or muted. Supports shimmer effects for push-to-talk, push-to-mute, and toggle modes so you always know your mic state at a glance.

---

## Installation

1. Download `Angry_Audio_Setup.exe` from the [latest release](https://github.com/Gantera2k/Angry-Audio/releases).
2. Run the installer. It will ask for administrator privileges to install to Program Files.
3. Follow the setup wizard — pick your mode, set your hotkey, and you're done.

Angry Audio is code-signed by **Andrew Ganter** through Microsoft Azure Trusted Signing. No "Unknown Publisher" warnings.

---

## System Requirements

- Windows 10 or later
- .NET Framework 4.7.2+ (included with Windows 10)
- ~5 MB disk space

---

## Features at a Glance

| Feature | Description |
|---|---|
| System-wide PTT | Controls ALL microphones, not just one app |
| 3 hotkey modes | Push-to-Talk, Push-to-Mute, Push-to-Toggle |
| Up to 3 hotkeys | Bind multiple keys for different setups |
| Volume Lock | Mic and speaker volume enforcement |
| AFK Protection | Auto-mute mic and speakers when idle |
| Mic Overlay | On-screen mic status indicator |
| Startup Launch | Runs silently on Windows boot |
| Correction Toasts | Get notified when an app tries to change your volume |
| Dark Theme | Modern dark UI with animated starfield background |
| Auto-Update | Checks for updates from GitHub releases |
| Code Signed | Verified publisher, no security warnings |

---

## How It Works

Angry Audio runs in your system tray as a small kitty icon. Double-click the kitty to open settings anytime.

**Push-to-Talk** uses a low-level keyboard hook to detect your hotkey globally — it works in any app, any game, even on the desktop. When PTT is active, all microphones are set to 0% volume at the Windows mixer level. Holding your hotkey restores them to your configured level.

**Volume Lock** polls the Windows audio endpoint at regular intervals. If any application changes your mic or speaker volume, Angry Audio immediately corrects it back to your locked level and optionally shows a toast notification telling you which app tried to change it.

---

## Building from Source

Angry Audio is written in C# using WinForms. It can be compiled with either Mono's `mcs` compiler or Microsoft's `csc.exe`.

### Quick Build (Windows with .NET SDK)
```
BUILD.bat
```

### Manual Build (Mono)
```bash
mcs -target:winexe -out:"Angry Audio.exe" \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -win32icon:"Angry Audio.ico" \
  AppVersion.cs Audio.cs Controls.cs CorrectionToast.cs \
  DarkTheme.cs DarkMessage.cs StarRenderer.cs StarBackground.cs \
  Dpi.cs FadeOverlay.cs InstanceDialog.cs Logger.cs Mascot.cs \
  MicStatusOverlay.cs OptionsForm.cs Program.cs PushToTalk.cs \
  Settings.cs ToastStack.cs TrayApp.cs UpdateDialog.cs WelcomeForm.cs
```

### Building the Installer
```bash
mcs -target:winexe -out:"Angry_Audio_Setup.exe" \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -win32icon:"Angry Audio.ico" \
  -resource:"Angry Audio.exe",app.exe \
  -resource:"Angry Audio.ico",app.ico \
  -resource:version.txt,version.txt \
  Installer.cs Mascot.cs AppVersion.cs DarkTheme.cs DarkMessage.cs \
  StarRenderer.cs StarBackground.cs Dpi.cs Logger.cs
```

---

## Privacy

Angry Audio does not collect, transmit, or store any personal data. There is no telemetry, no analytics, no network calls except checking for updates from this GitHub repository. Your audio settings stay on your machine.

---

## License

© 2026 Andrew Ganter. All Rights Reserved.

This software and its source code are the exclusive intellectual property of Andrew Ganter. Unauthorized copying, modification, distribution, or reverse engineering is strictly prohibited without prior written consent.

---

*Your privacy, your rules.*
