# MikaNote Installer

## Build

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

This will:

1. Build `MikaNote.App` in `Release`
2. Stage the app files into `installer\publish`
3. Download the official `.NET 8 Desktop Runtime` installer for Windows x64
4. Stage that runtime installer into `installer\redist`
5. Look for `Inno Setup 6`
6. Build `installer\Output\MikaNote-Setup.exe` if Inno Setup is installed

For a self-contained package later, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 -SelfContained
```

This will try to publish `win-x64` self-contained output, which may require runtime packs to be available.

## What the default build includes

- Program files under `Program Files\MikaNote`
- Automatic check/install for `.NET 8 Desktop Runtime`
- Desktop shortcut option
- Startup option
- Desktop context menu option
- First-launch splash screen after setup
- Uninstall cleanup for startup/context menu/shortcuts
- Optional user-data removal prompt during uninstall

## Self-contained note

The default script uses a framework-dependent release build so it can be produced offline more reliably.
If you want the installer to carry the .NET runtime with it, use `-SelfContained` once the required runtime packs are available on the build machine.

## Installer options

- Start MikaNote when Windows starts
- Add desktop context menu
- Create a desktop shortcut
- Launch MikaNote after setup

## Uninstall behavior

The uninstaller asks whether to keep notes and settings in:

```text
Documents\MikaNote
```

Choose `Yes` to keep data, or `No` to remove it with the app.
