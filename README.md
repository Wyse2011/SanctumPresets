# Sanctum Presets

[![Latest release](https://img.shields.io/github/v/release/Wyse2011/SanctumPresets?label=release)](https://github.com/Wyse2011/SanctumPresets/releases/latest)
[![Build](https://github.com/Wyse2011/SanctumPresets/actions/workflows/build.yml/badge.svg)](https://github.com/Wyse2011/SanctumPresets/actions/workflows/build.yml)
![Dalamud API 15](https://img.shields.io/badge/Dalamud%20API-15-blue)

**Remote control for the Sanctum VFX mod.** Pick the effect for each slot, give each slot its own color and speed, save the look as a named preset, and switch between looks with one click or a chat command. Keep notes on effects that need fixing and ideas for new looks, then export them to a text file.

---

## Features

- **Searchable slot dropdowns.** Type "fog" or "kk" to find an effect among hundreds instead of scrolling.
- **Named presets.** Save the current setup, then apply, update, rename, reorder or delete it.
- **Customize each slot.** Give each slot its own color (color wheel, strength, brightness), a **Chroma** full-color cycle with a typed cycle time, and playback speed (25% to 400%), live. The mod's files are never changed.
- **Sounds.** Add your own `.mp3`, `.ogg` or `.wav` files and play them on a slot, looping or once, with volume. Players near you hear them, and Lightless shares them.
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
  | 5 (sound) | Alexandrian ring | Left hand |

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

## Customizing a slot (color and speed)

Each slot has a **Customize** button next to **+ Note**. Click it to open that slot's adjuster. Once a slot is colored, the button takes on its color; if its speed is changed, the button shows it (for example **Customize 2x**).

**Color**

| Control | What it does |
|---|---|
| **Color this slot** | Turns the color on or off for this slot. Picking a color turns it on automatically. |
| **Color wheel / swatches** | Pick any color. Changes show up on you as you drag. |
| **Recolor** | Shifts every colored part of the effect to your color. White-hot parts stay white. Best for most effects. |
| **Tint** | Multiplies the whole effect by your color, like a colored filter. |
| **Strength** | 0% = original colors, 100% = fully your color. |
| **Brightness** | Makes the effect dimmer or brighter (20% to 300%). |

Pick **White** to drain the color out of an effect (ghostly fire, silver mist).

**Chroma**

| Control | What it does |
|---|---|
| **Chroma** | The effect cycles smoothly through every color (red, orange, yellow, green, cyan, blue, violet, magenta, back to red), forever. |
| **Seconds per cycle** | Type how long one trip around the color wheel takes, from 0.5 seconds to about 36 minutes, then press Enter. Quick buttons: 2s, 5s, 10s, 30s, 60s. |
| **Chroma strength** | 0% = original colors, 100% = fully the cycling colors. |

The color cycle is written into the effect itself, so the game animates it on its own and everyone who sees you through Lightless sees it cycle too, with nothing extra being sent while it runs. Each viewer's cycle starts when their game loads your effect, so two people may see different colors at the same moment. Chroma replaces the slot's static color while it's on; Brightness and Speed still apply, and the typed cycle time stays exact whatever the speed.

**Speed**

| Control | What it does |
|---|---|
| **Speed slider** | 25% (four times slower) to 400% (four times faster). Ctrl+click to type an exact value. |
| **0.5x / 0.75x / Normal / 1.5x / 2x / 3x** | Quick settings. |

Speed changes the whole effect consistently: animations, fades, texture scrolling, how long particles live, how often they spawn, how fast they move, spin and fall.

**Buttons**

| Button | What it does |
|---|---|
| **Same on all slots** | Gives every slot this color and speed. |
| **Copy / Paste** | Copy a slot's color and speed and paste them on another slot. |
| **Original colors** | Puts this slot's colors back to normal (keeps its speed). |
| **Reset slot** | Original colors, no chroma, normal speed. |

Customizing belongs to the **slot**: pick a different effect in that slot and it gets the same color and speed. **Reset all customizing** (under the slots) puts every slot back to normal. Presets save every slot's color and speed. Presets saved in 1.2 (one color for everything) still work.

How it works: the plugin makes customized copies of the effects you're using in a `_SanctumColors` folder inside the Sanctum VFX mod folder, and asks Penumbra to use them for your character. The mod's own files are never changed; the plugin only adds and clears that one folder. Customizing comes back automatically after restarting the game or reimporting the mod.

Some effects get their color mostly from their textures. Those still take the tint, but may not change as completely; the status line under the slots says which ones.

## Sounds

Sounds have their own slot: **5 - Sound (Left Ring)**, on the **Alexandrian ring worn on your left hand**. Slots 1 to 4 stay visual.

1. Open the **Sounds** tab and click **Add audio file...** (`.mp3`, `.ogg` or `.wav`). Rename it if you like.
2. Click the **Sound** button on slot 5 and pick your sound.
   - **Loop** on: repeats while the slot is on (mono). Off: plays once each time the effect starts (stereo).
   - **Volume**: 0% to 150% (above 100% can distort loud audio).
3. The first time, the sound is converted in the background (a few seconds); it starts playing as soon as it's ready.

Where things are kept:

- **Your audio files** stay in the plugin's own folder (**Open folder** in the Sounds tab), so updating or reimporting the mod never deletes them.
- **Converted game sounds** go in their own `_SanctumAudio` folder inside the Sanctum VFX mod folder, apart from the recolored effects in `_SanctumColors`. Being inside Penumbra's folder is what lets Lightless share them. If you reimport the mod, they're rebuilt automatically.

Sounds are converted to the game's own format using the game's own sound files as templates (read from your install, nothing from the game is shipped), so they play like any in-game sound effect: people near you hear them at their own **Sound Effects** volume, quieter with distance. Lightless shares them like effects, unless a pair has turned off **Sounds** for you.

Size: about 1.3 MB per minute looping (mono), 2.6 MB per minute playing once (stereo); every pair downloads it once. Only share sounds you have the right to share.

## Presets

| Button | What it does |
|---|---|
| **Apply** | Sets all slots with their colors and speeds, turns the mod on if it was off, and redraws you |
| **Update** | Replaces the preset with your current setup, colors and speeds |
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
| Colors or speed don't change | Click the slot's **Customize** button and check **Color this slot** / the speed slider. The status line under the slots shows what happened. |
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

- MP3 decoding: [NLayer](https://github.com/naudio/NLayer) (MIT). Ogg Vorbis decoding: [NVorbis](https://github.com/NVorbis/NVorbis) (MIT). Their source and license files are in `SanctumPresets/ThirdParty/`.

- Plugin code: [MIT License](LICENSE).
- **The Sanctum VFX mod is not part of this repository.** It includes skyboxes by KK, effects by the House VFX author, and effects from the game. Credit and permissions belong to their original authors.
- Built on [Dalamud](https://github.com/goatcorp/Dalamud) and [Penumbra](https://github.com/xivdev/Penumbra).
