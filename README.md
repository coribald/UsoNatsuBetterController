# UsoNatsuBetterController

A small BepInEx plugin that improves controller support for the Unity Mono version of UsoNatsu.

It keeps the game's existing Naninovel input where possible, remaps a few buttons to more common VN bindings, adds an English/Japanese language toggle on `R3`, seeds UI focus when menus open, and adds custom controller handling for screens that were mouse-only or only partly controller-aware.

## Mappings

- `A`: native confirm / advance
- `B`: native cancel
- `X`: native show / hide UI
- `Y`: opens backlog
- `LB`: toggles auto mode in normal gameplay / scrolls menus down
- `RB`: toggles skip in normal gameplay / scrolls menus up
- `Start`: opens pause menu
- `Select`: opens save/load menu
- `L3`: unused by this plugin
- `R3`: toggles language between English and Japanese

## Menu Behavior

- Title menu focus is automatically seeded so the left stick can navigate it reliably.
- Pause menu focus is automatically seeded so the left stick can navigate it.
- Save/load UI focus is automatically seeded when opened from `Select`.
- Backlog focus is automatically seeded when opened.
- `B` now properly closes the pause menu.

## Scroll Behavior

When backlog or save/load is open:

- `LB`: scroll down
- `RB`: scroll up
- `LT` / `RT`: also supported for scrolling if Unity exposes them on your controller setup

Outside backlog and save/load, `RB` toggles skip as normal.

## Build

1. Open a terminal in `UsoNatsuBetterController`.
2. Build with:

```powershell
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_HOME=(Get-Location).Path
dotnet build .\UsoNatsuBetterController.csproj -c Debug
```

The output DLL will be:

- `bin\Debug\UsoNatsuBetterController.dll`

## Install

1. Copy `bin\Debug\UsoNatsuBetterController.dll` into your game's `BepInEx\plugins\` folder.
2. Launch the game once to generate the config file.
3. Optional: adjust values in `BepInEx\config\cmarr.usonatsu.better-controller.cfg`.

## Notes

- This plugin targets the exported Unity Mono/Naninovel build the mod was developed against.
- Trigger axes and d-pads are inconsistent across Unity legacy input setups, so `LB` / `RB` scrolling is the primary path.

## How It Works

- Waits for Naninovel to initialize.
- Edits the live Naninovel input bindings to remap a few controller actions.
- Calls Naninovel's localization manager directly to switch locales, adding an English/Japanese toggle on `R3`.
- Finds visible managed UI screens and seeds the first interactable `Selectable` when needed.
- Drives `ScrollRect.verticalNormalizedPosition` for backlog and save/load so those screens can be used without a mouse wheel.
