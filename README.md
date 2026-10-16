# Sanctum Presets

[![Latest release](https://img.shields.io/github/v/release/Wyse2011/SanctumPresets?label=release)](https://github.com/Wyse2011/SanctumPresets/releases/latest)
[![Build](https://github.com/Wyse2011/SanctumPresets/actions/workflows/build.yml/badge.svg)](https://github.com/Wyse2011/SanctumPresets/actions/workflows/build.yml)
![Dalamud API 15](https://img.shields.io/badge/Dalamud%20API-15-blue)

**Remote control for the Sanctum VFX mod.** Pick the effect for each slot, save the look as a named preset, and switch between looks with one click or a chat command. Keep notes on effects that need fixing and ideas for new looks, then export them to a text file.

---

## Features

- **Searchable slot dropdowns.** Type "fog" or "kk" to find an effect among hundreds instead of scrolling.
- **Named presets.** Save the current setup, then apply, update, rename, reorder or delete it.
- **One-click reset.** Set every slot back to `---`.
- **Automatic redraw** after every change, so you see effects immediately.
- **Notes.** Mark effects that need fixing ("mist needs to be lowered"), record ideas, and link notes to specific effects, slots and presets. Slots with open notes show a yellow marker.
- **Export notes to `.txt`.** Sorted into fixes, ideas, general and done, followed by every preset and its slots.
- **Share presets** as a line of text you can paste in Discord.
- **Chat commands** that also work in macros.

## Requirements

- [XIVLauncher / Dalamud](https://goatcorp.github.io/)
- [Penumbra](https://github.com/xivdev/Penumbra)
- The **Sanctum VFX 4-Slot** mod installed in Penumbra (not included here)
- The accessories the mod uses, worn on your character:

  | Slot | Item | Worn on |
  |---|---|---|
  | 1 | Alexandrian earrings | Ears |
  | 2 | Gordian necklace | Neck |
  | 3 | Alexandrian bracelets | Wrists |
  | 4 | Gordian ring | Right hand |

## Installation

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this link, tick **Enabled**, and click **Save and Close**:

   ```
   https://raw.githubusercontent.com/Wyse2011/SanctumPresets/main/repo.json
   ```

3. Type `/xlplugins`, search for **Sanctum Presets**, and click **Install**.

Updates arrive through `/xlplugins` like any other plugin.

> **Used a test copy before?** Remove its path from **Dev Plugin Locations** in `/xlsettings` → Experimental first. Your presets and notes carry over.

## Quick start

1. Type `/sanctum`.
2. Choose an effect in each slot. Click a dropdown and type to search.
3. Type a name like `Molten Night` next to **Save as new preset** and click it.
4. Later, click **Apply** next to the preset, or type `/sanctum Molten Night`.

## Presets

| Button | What it does |
|---|---|
| **Apply** | Sets all slots, turns the mod on if it was off, and redraws you |
| **Update** | Replaces the preset with your current setup |
| **Note** | Writes a note linked to this preset |
| **Rename** | Renames it (notes stay linked) |
| **^** / **v** | Moves it up or down the list |
| **Copy** | Copies this preset as text for sharing |
| **X** | Deletes it (hold **Ctrl** while clicking) |

Hover over a preset's name to see its slots. **Reset all slots** sets every slot to `---`.

## Notes

Notes are for keeping track of what needs to change and what you'd like to try.

- **Types:** *Fix needed*, *Idea*, *General*. Tick **Done** when it's handled.
- **Links:** connect a note to effects (in one slot or any slot) and to presets.
- **Quick notes:**
  - **+ Note** next to a slot links that slot's current effect.
  - **Note** next to a preset links that preset.
  - `/sanctum note <text>` in chat links everything you're wearing.
- **Markers:** a yellow **! 2** next to a slot means its effect has open notes. Hover to read them.
- **Notes tab:** filter by type, search, show or hide done notes, edit and check them off.
- **Export:** **Export to .txt...** saves a report; **Copy as text** puts it on the clipboard.

## Sharing presets

- **Copy all presets to clipboard** (or **Copy** on one preset) gives you text starting with `SANCTUM1:`.
- Send that text to someone. They copy it and click **Import from clipboard**.
- Presets with the same name are replaced; new ones are added.

## Chat commands

| Command | What it does |
|---|---|
| `/sanctum` | Open or close the window |
| `/sanctum <preset name>` | Apply a preset (not case-sensitive) |
| `/sanctum reset` | Set every slot to `---` |
| `/sanctum list` | List your presets |
| `/sanctum note <text>` | Save a quick note linked to your current effects |

## Troubleshooting

| Problem | Fix |
|---|---|
| "Penumbra is not loaded or not ready yet." | Make sure Penumbra is installed and enabled, then reopen the window. |
| "Couldn't find a mod named like Sanctum VFX" | Open **Settings** at the bottom of the window and pick the mod. |
| Effects don't show up | Wear the accessory for that slot (see Requirements) and click **Redraw me**. |
| Two Sanctum mods installed | Keep only one enabled in Penumbra. The plugin prefers the 4-Slot version. |
| "... no longer exists" when applying | That effect was renamed or removed in a mod update. The slot falls back to `---`; pick a new effect and click **Update**. |

Errors show in red at the bottom of the window. For more detail, type `/xllog` and look for lines mentioning `SanctumPresets`.

Your presets and notes are saved in `%AppData%\XIVLauncher\pluginConfigs\SanctumPresets.json`.

## For developers

**Build** (needs the .NET 10 SDK, and XIVLauncher run once with Dalamud):

```
dotnet build -c Release
```

**Release:** publish a [GitHub Release](https://github.com/Wyse2011/SanctumPresets/releases/new) with a tag like `v1.2.0`. The **Release** workflow builds the plugin, attaches `latest.zip`, and updates `repo.json`.

The plugin talks to Penumbra only through its public IPC (API v5) and never edits mod files.

## Credits and license

- Plugin code: [MIT License](LICENSE).
- **The Sanctum VFX mod is not part of this repository.** It includes skyboxes by KK, effects by the House VFX author, and effects from the game. Credit and permissions belong to their original authors.
- Built on [Dalamud](https://github.com/goatcorp/Dalamud) and [Penumbra](https://github.com/xivdev/Penumbra).
