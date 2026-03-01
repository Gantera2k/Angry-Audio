# Angry Audio — Code Architecture

## File Map

### Theme & Visuals (single source of truth for all visual elements)
| File | Lines | What It Does |
|------|-------|-------------|
| `DarkTheme.cs` | ~90 | Color palette, fonts, GlassTint, RoundRect utility. **Change colors HERE.** |
| `StarRenderer.cs` | ~2180 | All painting methods (stars, shooting stars, celestial events, orbiting stars, shields). `partial class DarkTheme`. |
| `StarBackground.cs` | ~750 | `StarBackground` class (THE one star system), `ShootingStar`, `CelestialEvents`. All forms use this. |
| `DarkMessage.cs` | ~130 | Dark-themed MessageBox replacement. |

### Shared Controls (used by multiple forms)
| File | Lines | What It Does |
|------|-------|-------------|
| `Controls.cs` | ~530 | `ToggleSwitch`, `SlickSlider`, `BufferedPanel`, `ScrollPanel`, `SplashForm`, `PaddedNumericUpDown`. **Change how toggles/sliders look HERE.** |

### Forms (UI surfaces)
| File | Lines | What It Does |
|------|-------|-------------|
| `OptionsForm.cs` | ~1720 | Main settings window. All star rendering via `_stars.Paint()`. |
| `WelcomeForm.cs` | ~740 | First-run wizard. Same star pattern as Options. |
| `Installer.cs` | ~1180 | Setup wizard (single-surface painting, uses `_stars.PaintGlassTint()`). |

### Core Logic
| File | Lines | What It Does |
|------|-------|-------------|
| `TrayApp.cs` | ~2130 | **Orchestrator** — tray icon, enforcement loop, form management. |
| `Audio.cs` | ~1200 | COM interop for mic/speaker volume control. |
| `PushToTalk.cs` | ~410 | Keyboard hook + PTT/PTM/PTToggle state machine. |
| `Settings.cs` | ~420 | JSON persistence for all user preferences. |

### Supporting
| File | Lines | What It Does |
|------|-------|-------------|
| `CorrectionToast.cs` | ~750 | Toast notifications (correction, info, mic warning). |
| `MicStatusOverlay.cs` | ~720 | Floating mic status indicator. |
| `FadeOverlay.cs` | ~200 | Full-screen fade effect. |
| `UpdateDialog.cs` | ~440 | Auto-update download dialog. |
| `ToastStack.cs` | ~180 | Toast positioning manager. |
| `InstanceDialog.cs` | ~130 | "Already running" dialog. |
| `Mascot.cs` | ~150 | Angry kitten mascot renderer. |
| `Logger.cs` | ~130 | File logging. |
| `Dpi.cs` | ~100 | DPI scaling helpers. |
| `Program.cs` | ~140 | Entry point + single-instance check. |
| `AppVersion.cs` | ~17 | Version string. |

## How Stars Work (ONE system)

```
StarBackground (_stars)        ← Every form creates one
    ├── Paint(g, w, h, ox, oy) ← Draw stars at offset (child panels)
    ├── PaintGlassTint(g, w, h, path) ← Frosted glass card (installer)
    ├── PaintChildBg(g, w, h, ox, oy, cw, ch) ← Control background
    ├── Tick()                  ← Advance twinkle animation
    ├── Shooting (ShootingStar) ← Managed internally
    └── Celestial (CelestialEvents) ← Managed internally
```

**Every form** uses the exact same pattern:
- `PaintUnifiedStars(g, c)` → calls `_stars.Paint()` with FormOffset
- `PaintCardBg(g, child)` → calls `_stars.PaintChildBg()` with FormOffset

**Glass tint** is `DarkTheme.GlassTint` — defined ONCE, used everywhere.

## Where to Fix Things

| Problem | File to Edit |
|---------|-------------|
| Colors wrong | `DarkTheme.cs` |
| Stars invisible/broken | `StarBackground.cs` (cache/paint logic) |
| Star visual appearance | `StarRenderer.cs` (PaintCardStars) |
| Toggle/slider looks wrong | `Controls.cs` |
| Card glass tint wrong | `DarkTheme.cs` (GlassTint constant) |
| Hotkey not working | `PushToTalk.cs` (hook) + `OptionsForm.cs` (capture) |
| Volume not enforcing | `TrayApp.cs` (enforcement loop) + `Audio.cs` (COM) |
| Toast looks wrong | `CorrectionToast.cs` |
| Installer visual bug | `Installer.cs` (uses same StarBackground) |
| Welcome wizard bug | `WelcomeForm.cs` (uses same StarBackground) |
