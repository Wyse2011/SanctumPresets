# Sanctum Presets

A Dalamud plugin for the **Sanctum VFX 4-Slot** Penumbra mod.

- Pick an effect for each of the four slots with searchable dropdowns
- Save setups as **named presets** and switch looks with one click
- **Notes**: mark effects that need fixing, jot down ideas, link them to effects and presets, export everything to a `.txt`
- Share presets as text (`SANCTUM1:...`)

## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, add:
   ```
   https://raw.githubusercontent.com/YOUR-GITHUB-NAME/SanctumPresets/main/repo.json
   ```
   tick **Enabled**, then click **Save and Close**.
3. Type `/xlplugins`, search for **Sanctum Presets**, and install it.

You also need:
- **Penumbra** with the **Sanctum VFX 4-Slot** mod installed
- The accessories the mod uses: Alexandrian earrings and bracelets, Gordian necklace, Gordian ring (right hand)

## Use

| Command | What it does |
|---|---|
| `/sanctum` | Open the window |
| `/sanctum <preset name>` | Apply a preset |
| `/sanctum reset` | Set every slot to `---` |
| `/sanctum list` | List your presets |
| `/sanctum note <text>` | Save a quick note linked to your current effects |

## Building

Requires the .NET 10 SDK and XIVLauncher (run the game once with Dalamud).

```
dotnet build -c Release
```

Releases are built automatically: publish a GitHub Release with a tag like `v1.2.0`, and the
Release workflow builds the plugin, attaches `latest.zip`, and updates `repo.json`.
