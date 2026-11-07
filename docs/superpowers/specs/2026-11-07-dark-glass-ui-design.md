# Dark glass UI — design

## Intent

LumiDisplay's settings window, volume/brightness OSD and tray menu should feel like one calm,
elegant piece of dark gray frosted glass. Today they use a light liquid-glass look (white wash,
dark blue accent, blue OSD fill). The user wants the same family of materials, but dark gray, with
the settings window laid out like their reference (a messaging app: slim round-icon rail, a list
column, a content column, all inside one large rounded glass frame) and the OSD shaped like their
reference slider (glass pill, rounded track, round glass knob at the end of the fill) with a
**gray** fill instead of blue.

Approved mockups: `.superpowers/brainstorm/1557-1794051350/content/settings-layout.html` (option B)
and `osd-tray.html` (option A + tray menu). They are not checked in (`.superpowers/` is ignored);
the values below are the source of truth.

## Goals

- Settings window, OSD and tray menu share one dark gray glass language.
- Settings window uses a three-column layout (user's choice B).
- OSD uses dark gray glass (user's choice A) with a gray fill.
- Tray menu becomes a dark panel with rounded corners and soft hover (user said yes).
- Logo and app icon move to the gray palette.

## Non-goals

- No behaviour, setting, hotkey or page content changes. Purely visual.
- No new pages (there is no About page; none is added).
- No light/dark theme switch or user option: the app becomes dark only.
- No new dependencies; stays WinForms + GDI+.

## Anti-goals

- Blue accent anywhere (the accent becomes light gray / near-white).
- A flat opaque dark gray in the settings window or OSD: they must still read as frosted glass
  over the wallpaper. (The tray menu is the one exception, see below.)
- Losing the existing wallpaper-blur backdrop or DPI scaling.
- Low-contrast text: body text must stay clearly readable on the dark glass.

## Palette (replaces the light values in `Theme` and `Glass`)

| Role | Value |
|---|---|
| Window wash over blurred wallpaper | `#28292C` at ~62% alpha |
| Panel / card wash (on top of window) | white at ~5% alpha |
| Selected list item, hover | white at ~9% alpha |
| Hairline / divider | white at ~6–9% alpha |
| Rim highlight (top edge of glass) | white at ~8% (window), ~22% (OSD) |
| Text | `#ECECEE` |
| Text secondary / descriptions | `#8E8F95`–`#A3A4A9` |
| Accent (primary button, toggle on, selected rail icon) | `#ECECEE` fill with `#26272A` content |
| Toggle off track | white at ~14% alpha, knob `#C9CACE` |
| OSD fill | vertical gradient `#A9AAAF` → `#8B8C92` |
| OSD track | white at ~10% alpha |
| OSD knob | light glass: `#E6E7EA` ~55% alpha, 1px white ~55% rim, soft drop shadow |

Danger and success keep their meaning; builder may brighten them for the dark background.

## Settings window

- **Keeps the normal Windows title bar** (user's decision): `FixedSingle` border, caption tinted
  dark via DWM like today's `TopColor` tinting. The three columns are rounded glass panels inset in
  the client area; there is no borderless 26px window frame.
- **Left rail** (~56px): logo at top, one round icon button for Settings shown active (solid light
  circle, dark glyph), like the black circle in the reference but inverted. *My call:* nothing else
  in the rail, since there is no About page.
- **Middle column** (~210px, rounded glass panel): title "Settings", then the 4 existing pages as
  list items, each with its name and a one-line description (e.g. General — "Startup and media keys").
  Selected item gets the soft white highlight. *My call:* the existing monitor-status/version block
  moves to the bottom of this column.
- **Right column** (rounded glass panel): page title, existing cards and rows unchanged in
  content, restyled dark. *My call:* keep the two-layer backdrop (Base + Surface); cards inside the
  column are drawn as a hairline outline with row dividers, not a third glass layer. "Save changes" footer button becomes a light pill.
- Window gets wider to fit the extra column; height stays 600. Exact widths are the builder's.
- Keyboard page switching (Ctrl+Tab, Ctrl+1..4) keeps working.

## OSD

- Glass pill (fully rounded), dark gray wash over the blurred wallpaper, rim highlight on top.
- Small speaker glyph left, larger right, value in % at the end (brightness: sun glyphs).
- Rounded track; gray gradient fill; round light-glass knob sitting at the end of the fill, never
  leaving the track. Muted state keeps a dimmer fill.

## Tray menu

- **Solid dark panel, no real blur** (user's decision): `#28292C`-ish opaque fill with a subtle
  lighter rim, rounded corners (~18px) if WinForms allows cleanly, "LumiDisplay" header, existing items and
  submenus unchanged, rounded soft hover highlight, subtle separators.

## Logo and app icon

Changes everywhere (user's decision): the in-app mark (rail, tray menu header, tray icon fallback)
**and** `lumidisplay.ico` (exe, taskbar, tray) move from white tile + blue mark to the gray
palette. `Logo` gets its own colors instead of reading `Theme.Accent*`, so repointing the accent to
near-white can't turn it white-on-white. Regenerate the .ico with `IconExporter`. The exact gray
treatment of the mark (dark tile + light mark, or the reverse) is the builder's, as long as it reads
at 16px on both light and dark taskbars.

## Constraints

- Keep `GlassBackdrop` (blurred wallpaper) — only the wash colors change from light to dark.
- Everything flows from `Theme` / `Glass` constants; controls in `ThemedControls.cs`,
  `SettingsForm.cs`, `TrayApplicationContext.cs`, `OsdOverlay.cs` currently use them.
- Must look right at 100–200% DPI.
- Update the screenshots in `docs/images/` and README only if the builder can regenerate them; otherwise note it.

## Left to the builder

Exact pixel sizes, column widths, shadow values, glyph tweaks, how the tray menu's rounded
corners are achieved in WinForms, and contrast fine-tuning within the palette above. Standard
Windows message boxes and dropdown lists stay native.
