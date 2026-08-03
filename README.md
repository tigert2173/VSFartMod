# VSFartMod

BepInEx plugin for **Virtual Succubus** that adds gas / smell / watersports kink content: timed tasks, Content Select toggles, SFX, VFX, and session helpers (fart mask HUD, Skunk Girl tail, No Blackout Smothering).

Built for **VS48R1** with **BepInEx Unity Mono 6**.

---

## Features

### Content Select kinks

Toggles under **Content Select → Kinks** (cloned/wired at runtime):

| Toggle | What it enables |
|--------|-----------------|
| **Farting** | Timed fart tasks + hip gas VFX/SFX when task text mentions farts |
| **Fart Torture** | Denser bursts while fart task text is active |
| **Burping** | Burp tasks + mouth morph + burp SFX/VFX |
| **Scat** | Scat tasks + soft splat FX/SFX |
| **Skunk Girl** | Striped procedural tail in-session; musk spray on skunk task text |
| **Panty Sharting** | Shart-flavored fart bursts when shart task text is active |
| **Stinky Socks** | Sock-smell tasks + smell FX (does not inherit fart FX from gas blurbs) |
| **Stinky Armpits** | Armpit-smell tasks + smell FX |
| **Her Watersports** | Her-on-you pee tasks + stream FX (separate from vanilla **Watersports**) |
| **Watersports** (vanilla) | Wired so self/you piss stays separate from her FX |
| **No Blackout Smothering** | Disables the fullscreen `SkinOverlay` during FaceSit / ButtFaceSit and lightly pulls her body off the near lens |
| **Low / Medium / High / Constant / Random Gas** | Fart pacing presets (one active at a time; also F11 in-session) |

### Timed tasks & dialogue

Harmony injects modded CSVs when PlayMaker reads them:

- `Tasklist - Timed New` — fart / burp / pee / scat / skunk / dutch oven / fart mask / panty shart / stinky socks & armpits tasks (plus vanilla rows)
- `ListOfToggles`, `TogggleInfo`, `MenuExplanations` — kink descriptions / menu copy
- `InteractionStrings` — dialogue lines for the new content

Primary tasklists are **not** overwritten (different schema).

### Effects systems

- **FartEffects** — gas clouds, hip bursts, dutch-oven cloud, mask-tube stream; shuffle-bag fart WAVs; gassiness + frequency scaling; Keep Camera Clear opacity caps
- **FartMaskVisuals** — first-person fart-mask HUD + rubber tube hips → lens on mask/tube tasks
- **BurpEffects** — burp particles + real burp WAVs + VRM mouth morph (`Fcl_MTH_A`)
- **PissEffects** — her watersports stream FX; optional vanilla watersports gate
- **ScatEffects** — soft scat splat FX/SFX
- **SkunkGirlEffects** — black/white striped tail + musk spray mist
- **SmellTortureEffects** — socks / armpits smell FX
- **FaceSitPoseInstaller** — No Blackout Smothering (`SkinOverlay` off + `SuccuPosition` pull)
- **Keep Camera Clear** — mist/spray stay translucent (cfg default on; no Content Select row)

### Misc

- Optional custom **logo** (`NewLogo.png`) on loader / main menu
- Asset bundle `vsfartmod` for textures / TextAssets when present; sounds and CSVs also load from disk under `VSFartSounds`

---

## Install

All files players need are in **`VSFartMod\Install\`**:

```
Install/
  INSTALL.txt
  BepInEx/plugins/VSFartMod.dll
  StreamingAssets/
    vsfartmod
    vsfartmod.bundle
    VSFartSounds/          (WAVs, CSVs, NewLogo.png)
```

1. Install **BepInEx Unity Mono 6** into the game root (once).
2. Copy `Install\BepInEx\plugins\VSFartMod.dll` → `<Game>\BepInEx\plugins\`
3. Copy everything in `Install\StreamingAssets\` → `<Game>\Virtual Succubus_Data\StreamingAssets\`

**Windows note:** Do **not** rename `VSFartSounds` to `VSFartMod` — it collides with the `vsfartmod` bundle filename (case-insensitive).

Missing optional WAVs (Piss/Scat/Skunk/Smell) fall back to procedural audio.

After rebuilding the plugin:

```powershell
dotnet build -c Release
Copy-Item bin\Release\netstandard2.1\VSFartMod.dll Install\BepInEx\plugins\VSFartMod.dll -Force
```

---

## In-session hotkeys

| Key | Action |
|-----|--------|
| **F4** | Force smell FX burst |
| **F5** | Force scat FX |
| **F6** | Force pee FX |
| **F7** | Force burp FX |
| **F8** / **F9** | Force fart burst |
| **F10** | Force fart mask + tube (~12s) |
| **F11** | Cycle gassiness: Low → Medium → High → Constant → Random |
| **[** / **]** | Lower / raise fart frequency scale |
| **F12** | Force skunk spray |

Enable the matching Content Select kink for normal task-driven FX; hotkeys are for testing.

---

## Config (`BepInEx\config\*.cfg`)

Section **`Farting`**:

| Key | Default | Notes |
|-----|---------|--------|
| `Gassiness` | Medium | Pace preset (also Content Select gas rows / F11) |
| `FrequencyScale` | 1 | 0.25–4; `[` `]` in-session |
| `KeepCameraClear` | true | Cap mist/fog alpha |
| `MaxCloudOpacity` | 0.2 | Soft alpha cap when KeepCameraClear is on |
| `UseCustomSmotherPoses` | true | **No Blackout Smothering** (SkinOverlay off; no body move by default) |
| `SmotherBodyPull` | 0 | Optional body pull (0–0.35). Default **0** = overlay only |
| `FaceSitLower` | 0.035 | Slight camera drop on FaceSit/ButtFaceSit (aim at anus vs top of crack) |
| `FaceSitPullBack` / `FaceSitRaise` | 0 | Optional; leave 0 unless you want more nudge |
| `FaceSitCameraPullback` | false | **Deprecated — leave false** |

---

## Build plugin

```powershell
cd VSFartMod
dotnet build -c Release
Copy-Item bin\Release\netstandard2.1\VSFartMod.dll Install\BepInEx\plugins\VSFartMod.dll -Force
# optional: also push live into your game
# Copy-Item Install\BepInEx\plugins\VSFartMod.dll "<game>\BepInEx\plugins\" -Force
```

---

## Build Unity assets (optional, 2022.3.8f1)

Open the Unity project used for the bundle, then:

1. **VSFartMod → Regenerate Fart Sounds** (optional)
2. **VSFartMod → Build AssetBundles**
3. Copy `Assets\AssetBundles\vsfartmod` → game `StreamingAssets\vsfartmod`

TextAssets and WAVs under `VSFartSounds\` are enough for most installs even without a fresh bundle.

---

## Sound notes

- **Farts:** prefer `VSFartSounds\Farts\Fart_*.wav` (shuffle bag, anti-repeat). Bundle clips are used if present.
- **Burps:** `VSFartSounds\Burps\Burp_*.wav` (e.g. BigSoundBank CC0 recordings).
- Other folders follow the same `Prefix_*.wav` pattern; see any `README.txt` inside those folders.

---

## Requirements

- Virtual Succubus (tested on VS48R1 PC Full)
- BepInEx Unity Mono 6
- Harmony (shipped with BepInEx)
