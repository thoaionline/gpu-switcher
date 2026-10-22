<div align="center">
  <img src="GpuSwitcher/Assets/GpuSwitcher-256.png" alt="GPU Switcher icon" width="80" height="80">
  <h1>GPU Switcher</h1>
  <p>Set Windows graphics preferences for running desktop apps in one place.</p>
</div>

GPU Switcher lists running applications, shows which GPU they used during a short sample, and can set their Windows graphics preference to **High performance** in one batch. It also has an optional mode that checks for newly running apps every minute.

## Features

- **See what is running.** Processes sharing an executable are grouped into one application row. Search by name or path, and filter to apps observed using another GPU.
- **Compare use with preference.** The table shows both sampled GPU activity and the preference saved in Windows. An idle app may have no activity to show.
- **Apply in bulk or selectively.** Set all eligible apps or choose individual rows. Windows and protected processes are excluded.
- **Keep new apps covered.** Turn on **Auto switch every minute** to scan immediately and then every 60 seconds while the window is open. Apps already set to High performance are skipped; an unchanged cycle makes no registry writes.
- **Undo the latest batch.** The previous registry values are saved before a batch and can be restored with **Undo last change**. Undo also turns automatic switching off.

## Requirements

- Windows 10 version 1803 or later, or Windows 11
- Two or more GPUs for switching to be useful
- .NET 10 SDK to build or run from source; a self-contained published build does not require a separate .NET installation

Preferences are saved for the current Windows user. Administrator access is not required.

## Run from source

Open the solution in Visual Studio with the **.NET desktop development** workload, or run this from the repository root in PowerShell:

```powershell
dotnet run --project .\GpuSwitcher\GpuSwitcher.csproj
```

The app scans on launch. Use **Set all eligible to High performance** for a one-time batch, or enable **Auto switch every minute** to keep checking while the app stays open. Restart affected applications for the new preference to take effect. Use **Undo last change** to restore the most recent batch; each later batch replaces that backup.

## Build a standalone executable

From the repository root on Windows:

```powershell
dotnet publish .\GpuSwitcher\GpuSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64
```

The resulting `GpuSwitcher.exe` is in `dist\win-x64`. The `dist` directory is ignored by Git; build outputs are not part of the source repository.

## How it works and what to expect

GPU Switcher identifies adapters through DXGI and samples the Windows **GPU Engine** performance counters. The activity column describes a brief observation, not a guarantee about which GPU an app will use later. The High performance choice asks Windows to prefer its high performance adapter; it does not pin an exact physical GPU when several candidates exist.

For each selected desktop executable, the app sets `GpuPreference=2;` under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, preserving other settings in that value. It rechecks the saved preference before writing, so an app already set to High performance is left untouched. This registry format is used by Windows Graphics settings but is not a documented public API; a future Windows release could change it.

Apps with inaccessible executable paths cannot be changed. Apps that explicitly choose a GPU through their own settings or graphics API may use a different device despite the Windows preference. Automatic switching runs only while the window is open and starts **off** each time the app launches.

An eligible app is a readable desktop `.exe` outside the Windows directory. GPU Switcher excludes itself and does not change Windows system processes or apps whose executable path is inaccessible.

## Contributing

Bug reports and pull requests are welcome. Please describe the Windows version and GPU setup when reporting detection or preference issues. Build the project and check that the window launches before submitting a code change.

## License

GPU Switcher is released under the [MIT License](LICENSE).
