# Angry Audio — Complete Build Guide

**READ THIS ENTIRE FILE BEFORE TOUCHING ANYTHING.**

This guide exists because Claude has repeatedly forgotten how to compile this project, shipped half-builds, and wasted Drew's time. If you are Claude reading this: follow it exactly. No improvising.

---

## Project Overview

Angry Audio is a C# WinForms application compiled with the .NET Framework 4.0 `csc.exe` compiler (on Windows) or Mono's `mcs` compiler (on Linux/CI). It produces two executables:

1. **Angry Audio.exe** — the main application
2. **Angry_Audio_Setup.exe** — a self-extracting installer that embeds the exe + ico + version.txt as resources

---

## File Inventory

### Source Files (compiled into Angry Audio.exe)

Every `.cs` file below MUST be included. If you add a new `.cs` file, add it here AND to both build commands.

```
AppVersion.cs          — Version string, copyright, legal notice
Audio.cs               — Windows Core Audio API wrapper (mic/speaker control)
Controls.cs            — Custom UI controls (ToggleSwitch, PaddedNumericUpDown, etc.)
CorrectionToast.cs     — Toast notifications when volume is corrected
DarkMessage.cs         — Dark-themed message box replacement
DarkTheme.cs           — Shared color constants, painting helpers, orbiting star
Dpi.cs                 — DPI scaling helpers (Dpi.S, Dpi.Pt, Dpi.Size)
FadeOverlay.cs         — AFK fade-in/fade-out overlay
InstanceDialog.cs      — "Already running" dialog
Logger.cs              — File logger (%APPDATA%\Angry Audio\log.txt)
Mascot.cs              — Base64-embedded angry kitten mascot image
MicStatusOverlay.cs    — On-screen mic muted/open overlay with shimmer
OptionsForm.cs         — Main settings form (all panes)
Program.cs             — Entry point, mutex, DPI init
PushToTalk.cs          — Hotkey engine (polling + optional LL hook)
Settings.cs            — JSON settings load/save
StarBackground.cs      — Animated star background (shared across forms)
StarRenderer.cs        — Star field rendering + celestial events
ToastStack.cs          — Toast notification manager
TrayApp.cs             — System tray icon, main coordinator
UpdateDialog.cs        — Auto-update from GitHub releases
WelcomeForm.cs         — First-run wizard
```

### Source Files (compiled into Angry_Audio_Setup.exe)

The installer is a SEPARATE compilation that only includes these files:

```
Installer.cs           — Installer UI and extraction logic
Mascot.cs              — Shared mascot image
AppVersion.cs          — Shared version info
DarkTheme.cs           — Shared theme
DarkMessage.cs         — Shared message box
StarRenderer.cs        — Shared star rendering
StarBackground.cs      — Shared star background
Dpi.cs                 — Shared DPI helpers
Logger.cs              — Shared logger
```

### Non-Code Files

```
Angry Audio.ico        — Application icon (MUST be included in zip builds)
app.manifest           — Installer manifest (requireAdministrator)
version.txt            — Single line: version number (e.g. "63.6")
BUILD.bat              — Windows build script
BUILD_GUIDE.md         — This file
ARCHITECTURE.md        — Architecture documentation
README.md              — GitHub readme
.gitignore             — Git ignore rules
```

### NOT Compiled (ignore these)

```
new_events.cs          — Leftover snippet, not part of the build
```

---

## How to Compile

### References (required for both builds)

```
System.dll
System.Drawing.dll
System.Windows.Forms.dll
Microsoft.CSharp.dll
```

### Step 1: Build the Main Application

**Windows (csc.exe):**
```bat
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:"Angry Audio.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
  /win32icon:"Angry Audio.ico" ^
  AppVersion.cs Audio.cs Controls.cs CorrectionToast.cs ^
  DarkTheme.cs DarkMessage.cs StarRenderer.cs StarBackground.cs ^
  Dpi.cs FadeOverlay.cs InstanceDialog.cs Logger.cs Mascot.cs MicStatusOverlay.cs ^
  OptionsForm.cs Program.cs PushToTalk.cs Settings.cs ToastStack.cs TrayApp.cs ^
  UpdateDialog.cs WelcomeForm.cs
```

**Linux/CI (Mono mcs):**
```bash
mcs -out:"Angry Audio.exe" -target:winexe \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:Microsoft.CSharp.dll \
  AppVersion.cs Audio.cs Controls.cs CorrectionToast.cs \
  DarkTheme.cs DarkMessage.cs StarRenderer.cs StarBackground.cs \
  Dpi.cs FadeOverlay.cs InstanceDialog.cs Logger.cs Mascot.cs MicStatusOverlay.cs \
  OptionsForm.cs Program.cs PushToTalk.cs Settings.cs ToastStack.cs TrayApp.cs \
  UpdateDialog.cs WelcomeForm.cs
```

**Expected result:** 3 warnings (UpdateDialog unused fields). Zero errors.

### Step 2: Build the Installer

The installer embeds three resources: the exe, the icon, and the version file. **Step 1 must complete first** because the installer embeds `Angry Audio.exe` as a resource.

**Windows (csc.exe):**
```bat
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:"Angry_Audio_Setup.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
  /win32icon:"Angry Audio.ico" ^
  /win32manifest:app.manifest ^
  /resource:"Angry Audio.exe",app.exe ^
  /resource:"Angry Audio.ico",app.ico ^
  /resource:version.txt,version.txt ^
  Installer.cs Mascot.cs AppVersion.cs DarkTheme.cs DarkMessage.cs ^
  StarRenderer.cs StarBackground.cs Dpi.cs Logger.cs
```

**Linux/CI (Mono mcs):**
```bash
mcs -target:winexe -out:"Angry_Audio_Setup.exe" \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:Microsoft.CSharp.dll \
  -win32icon:"Angry Audio.ico" \
  -resource:"Angry Audio.exe",app.exe \
  -resource:"Angry Audio.ico",app.ico \
  -resource:version.txt,version.txt \
  Installer.cs Mascot.cs AppVersion.cs DarkTheme.cs DarkMessage.cs \
  StarRenderer.cs StarBackground.cs Dpi.cs Logger.cs
```

**Note:** The Linux build omits `-win32manifest:app.manifest` because Mono ignores it. The manifest only matters on Windows where it triggers the UAC elevation prompt.

**Expected result:** Zero warnings, zero errors.

---

## Version Bumping

**EVERY build that changes ANY code MUST bump the version in TWO places:**

1. `AppVersion.cs` — change `Version = "XX.X"`
2. `version.txt` — change to match (single line, no newline issues)

These MUST always match. The installer reads version.txt to check for updates. AppVersion.cs is displayed in the UI.

---

## Code Signing (Production Releases)

Angry Audio is signed through **Microsoft Azure Trusted Signing** ($9.99/month). The publisher displays as **Andrew Ganter**.

Signing happens on Drew's Windows machine after compilation, NOT in CI. The signing command uses:

- **SignTool.exe** (from Windows SDK)
- **Azure.CodeSigning.Dlib.dll** (from Microsoft.Trusted.Signing.Client NuGet)
- **metadata.json** (Azure config with endpoint, account, certificate profile)
- **Azure CLI login** (`az login` must be active)

Drew runs signing manually. Claude does NOT sign builds.

---

## Packaging for Distribution

### DEV zip (for Claude → Drew handoff):

Include all source + ico:
```bash
zip Angry_Audio_vXX_X_DEV.zip *.cs *.ico *.bat *.manifest *.txt *.md .gitignore
```

**ALWAYS include `Angry Audio.ico` in the zip. No exceptions.**

### Release (for GitHub):

Only distribute `Angry_Audio_Setup.exe` (after signing on Drew's machine).

---

## Git Workflow

- Repository: `https://github.com/Gantera2k/Angry-Audio.git`
- Branch: `main`
- Commit message format: `vXX.X - Brief description of changes`
- Force push is OK for amending during active development sessions
- **ALWAYS verify changes survived after git operations** (rebase has eaten changes before)

---

## Common Mistakes (Claude, Read This)

1. **Half-builds:** ALWAYS verify the compiled exe contains your changes. If you did a git checkout or rebase, re-check EVERY modified file before compiling.

2. **Missing files in build command:** If you add a new .cs file, it must go in the compile command. The compiler doesn't auto-discover files.

3. **Forgetting the icon in zip builds:** `Angry Audio.ico` MUST be in every zip. The app embeds it as base64 in Mascot.cs for runtime, but the .ico file is needed for compilation (`/win32icon`).

4. **version.txt mismatch:** If AppVersion.cs says 63.6 but version.txt says 63.3, the auto-updater breaks. Always update both.

5. **Installer built before exe:** The installer embeds `Angry Audio.exe` as a resource. If you build the installer first, it embeds a stale exe. Always build exe first, then installer.

6. **Git rebase clobbering changes:** After ANY git operation (rebase, checkout, merge), verify your changes with `grep` before compiling. Don't trust that the operation preserved your work.

---

## DPI Architecture Note

All pixel coordinates in the UI are specified at **96 DPI base** and scaled through `Dpi.S()`, `Dpi.Pt()`, and `Dpi.Size()`. Drew runs at 150% scaling (144 DPI). When writing layout code, pass BASE values (as if designing for 100% scaling) and let the Dpi helpers scale them. Do not pass pre-scaled values.

---

## Quick Reference

```
# Full build (Linux/CI):
mcs -out:"Angry Audio.exe" -target:winexe -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:Microsoft.CSharp.dll AppVersion.cs Audio.cs Controls.cs CorrectionToast.cs DarkTheme.cs DarkMessage.cs StarRenderer.cs StarBackground.cs Dpi.cs FadeOverlay.cs InstanceDialog.cs Logger.cs Mascot.cs MicStatusOverlay.cs OptionsForm.cs Program.cs PushToTalk.cs Settings.cs ToastStack.cs TrayApp.cs UpdateDialog.cs WelcomeForm.cs

# Full installer (Linux/CI):
mcs -target:winexe -out:"Angry_Audio_Setup.exe" -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:Microsoft.CSharp.dll -win32icon:"Angry Audio.ico" -resource:"Angry Audio.exe",app.exe -resource:"Angry Audio.ico",app.ico -resource:version.txt,version.txt Installer.cs Mascot.cs AppVersion.cs DarkTheme.cs DarkMessage.cs StarRenderer.cs StarBackground.cs Dpi.cs Logger.cs

# Package:
zip Angry_Audio_vXX_X_DEV.zip *.cs *.ico *.bat *.manifest *.txt *.md .gitignore
```
