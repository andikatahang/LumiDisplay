# Dark Glass UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-skin LumiDisplay's settings window, OSD, tray menu and logo/icon from light glass + blue to dark gray glass, and lay the settings window out in three columns.

**Architecture:** All colors flow from `Theme` (Theme.cs) and `Glass` (Glass.cs). Task 1 flips those tokens to dark and adds the few new ones; later tasks remove hard-coded light colors per surface and change the settings layout. The wallpaper-blur `GlassBackdrop` stays; only its washes change.

**Tech Stack:** C# / .NET 8 WinForms, GDI+. No test project exists; checks are `Debug.Assert` self-checks, `dotnet build`, and running the app and looking at it.

**Spec:** `docs/superpowers/specs/2026-11-07-dark-glass-ui-design.md`

## Global Constraints

- Purely visual: no behaviour, setting, hotkey or page-content changes.
- No new NuGet packages; WinForms + GDI+ only.
- No blue anywhere. Accent = `#ECECEE` with `#26272A` content on it.
- Settings window keeps the Windows title bar (`FixedSingle`), height 600.
- Settings window and OSD must still read as frosted glass over the wallpaper; the tray menu is an opaque dark panel.
- Must look right at 100%, 150% and 200% DPI.
- Build command (from repo root): `dotnet build src/LumiDisplay/LumiDisplay.csproj -c Debug`. Expected: `Build succeeded` with 0 errors.
- Run command: `dotnet run --project src/LumiDisplay -c Debug -- --settings`.

## Review Focus

1. **A very bright wallpaper** (white or near-white): the dark wash must still give readable text. Check: set a white wallpaper, open settings, body text and descriptions readable (Task 1 wash value; Task 1 contrast assert covers the solid fallback).
2. **No wallpaper / solid-color desktop** (backdrop null): `PaintBehind` falls back to `Theme.Window`/`Theme.Surface`; both must be dark. Check: set a solid black desktop, open settings and trigger the OSD.
3. **Leftover light colors** in a rarely seen state: the hotkey recorder while recording, an unavailable hotkey, the Stepper focused, a disabled tray item, a checked tray item, the muted OSD. Check each in Task 2/4/5's visual pass.
4. **Text on the new light accent**: primary button text, tray checkmark, active rail icon, toggle-on knob must be dark (`Theme.OnAccent`), not white. Covered by the Task 1 assert and Task 2 visual pass.
5. **High DPI**: three-column widths and the OSD pill radius must scale; nothing clipped at 200%. Check in Task 3 and Task 4.

---

### Task 1: Dark tokens

**Files:**
- Modify: `src/LumiDisplay/Theme.cs:78-136` (the `Theme` class)
- Modify: `src/LumiDisplay/Glass.cs:23-30, 105-126` (doc comment + wash/rim/hairline constants)
- Modify: `src/LumiDisplay/SettingsForm.cs:66-82` (`RefreshGlass` caption tint)
- Modify: `src/LumiDisplay/Program.cs` (self-check call)

**Interfaces:**
- Produces: new members `Theme.OnAccent` (Color), `Theme.Overlay(int alpha) -> Color` (white at alpha), `Theme.Shadow(int alpha) -> Color` (black at alpha), `Theme.ContrastRatio(Color fg, Color bg) -> double` (WCAG 2.x), `Theme.SelfCheck()`.
- Existing token names are kept; only values change.

- [ ] **Step 1: Set the token values in `Theme`**

| Token | Value |
|---|---|
| Window | `#2A2B2E` |
| Surface | `#333437` |
| SurfaceAlt | `#3B3C40` |
| Border | `#3F4044` |
| BorderStrong | `#55565B` |
| Text | `#ECECEE` |
| TextSecondary | `#C9CACE` |
| TextMuted | `#A3A4A9` |
| TextFaint | `#8E8F95` |
| Accent | `#ECECEE` |
| AccentHover | `#FFFFFF` |
| AccentPressed | `#C9CACE` |
| AccentSoft | `#3B3C40` |
| OnAccent (new) | `#26272A` |
| Danger | `#F87171` |
| Success | `#4ADE80` |

Update the class doc comment to "dark gray glass palette with a light gray accent".

- [ ] **Step 2: Set the `Glass` constants**

`WindowWash = ARGB(158, 40, 41, 44)`, `SurfaceWash = ARGB(13, 255, 255, 255)`, `RimTop = ARGB(22, 255, 255, 255)`, `RimBottom = ARGB(6, 255, 255, 255)`, `Hairline = ARGB(23, 255, 255, 255)`, `Divider = ARGB(15, 255, 255, 255)`. Also change the blur fallback gradient at Glass.cs:68 (`178,205,236` → `214,226,242`) to `#3A3C40` → `#2A2B2E`, and the scrollbar thumb at Glass.cs:517 to `Theme.Overlay(_hover || _dragging ? 90 : 50)`. Update the class doc comment ("washed toward dark gray").

- [ ] **Step 3: Write `Theme.SelfCheck()` with these asserts, and call it from `Program.Main` right after `UiFonts.Load()`**

```csharp
Debug.Assert(ContrastRatio(Text, Surface) >= 7.0);
Debug.Assert(ContrastRatio(TextSecondary, Surface) >= 4.5);
Debug.Assert(ContrastRatio(TextMuted, Surface) >= 4.5);
Debug.Assert(ContrastRatio(OnAccent, Accent) >= 7.0);
Debug.Assert(ContrastRatio(Text, Window) >= 7.0);
Debug.Assert(Math.Abs(ContrastRatio(Color.White, Color.Black) - 21.0) < 0.01);
```

- [ ] **Step 4: Dark caption in `RefreshGlass`**

Border color becomes `Theme.Border`. Also set `DWMWA_USE_IMMERSIVE_DARK_MODE` (20) to 1 so the caption buttons are light. It goes in the same try block.

- [ ] **Step 5: Build and run Debug; the app starts with no assert dialog**

Run the build and run commands. Expected: no assert failure; the settings window is dark (other surfaces may still show light leftovers until Tasks 2–6).

- [ ] **Step 6: Commit**

```bash
git add src/LumiDisplay/Theme.cs src/LumiDisplay/Glass.cs src/LumiDisplay/SettingsForm.cs src/LumiDisplay/Program.cs
git commit -m "feat(ui): dark gray glass tokens"
```

---

### Task 2: Themed controls without light leftovers

**Files:**
- Modify: `src/LumiDisplay/ThemedControls.cs` (every hard-coded `Color.FromArgb` listed below)

**Interfaces:**
- Consumes: `Theme.Overlay`, `Theme.Shadow`, `Theme.OnAccent`, `Glass.Hairline` from Task 1.

- [ ] **Step 1: Replace the hard-coded colors using these rules**

- Dark ink at alpha `FromArgb(a, 15, 23, 42)` used for **shadows** → `Theme.Shadow(a * 2)` (cap 255). Used for **tracks, hover, borders, clear-button backgrounds** → `Theme.Overlay(a)`.
- White fills at alpha `FromArgb(a, 255, 255, 255)` used as **field/tile/key-cap/selected-nav fills** → `Theme.Overlay(a / 9)` (≈14–25), with hover one step brighter. White **sheens** on buttons → remove (spec: flat light pill).
- Content drawn on `Theme.Accent` (primary button text, toggle-on knob, any white glyph on accent) → `Theme.OnAccent`.
- Recording fill in `ShortcutRecorder` (`235, 238,243,252`) → `Theme.Overlay(30)`.

- [ ] **Step 2: `Card` becomes outline-only**

`Card` keeps `IGlassSurface`, but it no longer paints a glass surface. It paints `Glass.PaintBehind` and then one rounded `Glass.Hairline` outline. Row dividers stay `Glass.Divider` (spec: cards are outline + dividers, no third layer).

- [ ] **Step 3: Build, run, visual pass**

Open every settings page. Then record a hotkey, hover and click a Stepper, drag a SliderBar, and toggle a switch. Expected: no white or blue fills anywhere. Primary "Save changes" is a light pill with dark text.

- [ ] **Step 4: Commit**

```bash
git add src/LumiDisplay/ThemedControls.cs
git commit -m "feat(ui): dark themed controls"
```

---

### Task 3: Three-column settings window

**Files:**
- Modify: `src/LumiDisplay/SettingsForm.cs` (`SidebarWidth` const, `InitializeComponent` size, `BuildSidebar`, `AddPage`, header/footer X positions)
- Modify: `src/LumiDisplay/ThemedControls.cs` (`NavItem`, new `RailButton`)

**Interfaces:**
- Consumes: tokens from Task 1; outline `Card` from Task 2.
- Produces: `NavItem(string text, string description)` (glyph dropped, height 56, selected = `Theme.Overlay(23)` rounded fill); `RailButton : ThemedControl` with `Glyph Glyph` and `bool Selected` (40×40 circle; selected = `Theme.Accent` fill with `Theme.OnAccent` glyph, else `Theme.Overlay(15)` fill with `Theme.TextMuted` glyph).

- [ ] **Step 1: Replace `SidebarWidth = 224` with `RailWidth = 72` and `ListWidth = 240`; set `ClientSize = new Size(RailWidth + ListWidth + ContentWidth, 600)`; header, pages and footer move to X = `RailWidth + ListWidth`**

- [ ] **Step 2: Build the rail**

The rail is a `GlassPanel { IsSurface = true }`, inset 12, full height. It holds `Logo.Draw` at 36px, centered, top 16. Below that sits one `RailButton(Glyph.Gear) { Selected = true }`, centered at top 72. Nothing else goes in the rail.

- [ ] **Step 3: Build the list column**

The list is a `GlassPanel { IsSurface = true }` at X = `RailWidth`, inset 12, width `ListWidth - 12`. It has the title "Settings" (`Theme.SemiboldFont(12F)`). Under the title come the 4 `NavItem`s, each with a description:

| Page | Description |
|---|---|
| General | Startup and media keys |
| Stepping & OSD | How far each key press moves |
| Hotkeys | Global keyboard shortcuts |
| Monitors & Test | Pick a display, try it live |

The existing monitor-status/version block moves from the old sidebar to the bottom of this column, unchanged apart from colors. Remove the "SETTINGS" caps label.

- [ ] **Step 4: Content column as one rounded surface**

Wrap header, pages and footer in one `GlassPanel { IsSurface = true }`, inset 12, so they sit on the surface layer. Their positions become relative to that panel.

- [ ] **Step 5: Build, run, check**

Expected:
- Three columns are visible.
- Clicking each list item, Ctrl+Tab and Ctrl+1..4 switch pages and move the highlight.
- Scrolling a long page (Hotkeys) has no seams.
- At 150% and 200% DPI (Windows display scale) nothing is clipped.

- [ ] **Step 6: Commit**

```bash
git add src/LumiDisplay/SettingsForm.cs src/LumiDisplay/ThemedControls.cs
git commit -m "feat(settings): three-column dark glass layout"
```

---

### Task 4: Dark gray OSD pill

**Files:**
- Modify: `src/LumiDisplay/OsdOverlay.cs:100-120` (constants), the card/track/knob painting (≈255–365)

**Interfaces:**
- Consumes: `Theme.Overlay`, `Theme.Shadow`, `Theme.SemiboldFont` from Task 1.

- [ ] **Step 1: Set the constants**

- Palette: `FillTop = #A9AAAF`, `FillBottom = #8B8C92`, `Knob = ARGB(140, 230, 231, 234)`, `IconColor = #C9CACE`, `MutedFill = #5E5F64`.
- `CardWidth` grows to 360 so the value fits. `TrackInsetRight` grows by the width of the value label (≈40).

- [ ] **Step 2: Paint changes**

- The card radius becomes `CardHeight * s / 2` (pill).
- The wash becomes `ARGB(150, 40, 41, 44)`, and the no-backdrop fallback becomes `ARGB(240, 44, 45, 48)`.
- Drop the white sheen.
- The outer line becomes `Theme.Shadow(60)`. The rim gradient becomes `Theme.Overlay(56)` → `Theme.Overlay(12)`.
- The track becomes `Theme.Overlay(26)`, with no inset line.
- The knob rim becomes `Theme.Overlay(140)`. The knob shadow becomes `Theme.Shadow(80)`.

- [ ] **Step 3: Value label**

Draw the current value as an integer (e.g. `68`), right of the large icon. Use `Theme.SemiboldFont(10F)` in `Theme.Text`, right-aligned to `card.Right - 16*s`. When muted, use `Theme.TextMuted`.

- [ ] **Step 4: Build, run, check**

Press the volume keys and the brightness hotkeys, then mute. Check at 0%, 50% and 100% on a bright and a dark wallpaper. Expected:
- dark pill
- gray fill
- the knob never leaves the track
- the value updates
- mute dims the fill

- [ ] **Step 5: Commit**

```bash
git add src/LumiDisplay/OsdOverlay.cs
git commit -m "feat(osd): dark gray glass pill with gray fill"
```

---

### Task 5: Dark tray menu

**Files:**
- Modify: `src/LumiDisplay/TrayApplicationContext.cs:360-384` (menu creation), `779-870` (`LumiDisplayColorTable`, `LumiDisplayMenuRenderer`)

**Interfaces:**
- Consumes: tokens from Task 1.

- [ ] **Step 1: Colors**

- Menu `BackColor` and dropdown background: `Theme.Surface`. Header label: `Theme.Text`.
- Hover: rounded `Theme.SurfaceAlt` fill (radius 8), with the text staying `Theme.Text`.
- Arrows: `Theme.TextMuted`. Separator: `Theme.Border`.
- Check box: `Theme.Accent` with an `Theme.OnAccent` checkmark.
- The color-table borders: `Theme.Border`.

- [ ] **Step 2: Rounded corners**

On Windows 11, set `DWMWA_WINDOW_CORNER_PREFERENCE` (33) = `DWMWCP_ROUND` (2) on the menu's and each submenu's handle when it opens (`Opened` event). On older Windows, leave the corners square (`shortcut: square corners on Windows 10, fine since the app targets Windows 11 look`).

- [ ] **Step 3: Build, run, check**

Right-click the tray icon and open each submenu (Monitors, Brightness, Volume). Hover over items and check one. Expected: dark rounded panels, no white or blue, readable disabled items ("No monitors detected").

- [ ] **Step 4: Commit**

```bash
git add src/LumiDisplay/TrayApplicationContext.cs
git commit -m "feat(tray): dark rounded menu"
```

---

### Task 6: Gray logo and app icon

**Files:**
- Modify: `src/LumiDisplay/Theme.cs:277-356` (`Logo`)
- Modify (regenerated): `src/LumiDisplay/lumidisplay.ico`
- Modify: `docs/images/*.png`, README.md / README.id.md only if screenshots are retaken (see Step 4)

**Interfaces:**
- Produces: `Logo` owns its colors (private constants), no `Theme.Accent*` reads.

- [ ] **Step 1: Give `Logo` its own palette**

The tile is a dark graphite gradient `#3A3B3F` → `#26272A`, with a `#55565B` hairline. The monitor and sun mark is `#ECECEE`, the screen glass `#4A4B50`, and the rays `#C9CACE`. At ≤24px, keep the border a little stronger (`#6E6F75`) so the tile reads on a dark taskbar.

- [ ] **Step 2: Regenerate the icon**

```bash
dotnet run --project src/LumiDisplay -c Debug -- --export-icon src/LumiDisplay/lumidisplay.ico
```
Expected: the file is rewritten (git shows it modified).

- [ ] **Step 3: Check**

Rebuild and run. Look at:
- the taskbar icon on the light and the dark Windows theme
- the tray icon at 100% and 150%
- the rail logo and the tray header

Expected: the mark is readable at 16px on both taskbars.

- [ ] **Step 4: Screenshots**

Retake `docs/images/settings-*.png`, `osd-*.png`, `tray-menu.png` at the same sizes if possible. If not, leave them and note "screenshots outdated" in the final summary.

- [ ] **Step 5: Commit**

```bash
git add src/LumiDisplay/Theme.cs src/LumiDisplay/lumidisplay.ico docs/images
git commit -m "feat(brand): gray logo and app icon"
```
