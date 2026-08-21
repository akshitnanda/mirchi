# Mirchi

[![Build](https://github.com/akshitnanda/mirchi/actions/workflows/build.yml/badge.svg)](https://github.com/akshitnanda/mirchi/actions/workflows/build.yml)

A tiny, playful, privacy-first desktop charm inspired by the Indian nimbu-mirchi tradition. Mirchi is drawn entirely with native vector graphics and stays local to your computer.

## Platform status

- **Windows:** build-verified and smoke-tested. No .NET SDK or package install is required on standard 64-bit Windows installations with .NET Framework 4.x.
- **Experimental macOS 13+:** native AppKit source is included in [`mac`](mac/). Automated macOS compilation is configured, but interactive testing on a Mac is still outstanding.

## Quick start on Windows

1. Download or clone this repository.
2. Double-click `run.cmd`.
3. Right-click the charm and choose **Quit** when you are done.

The first run builds `Mirchi.exe` beside the source. Later you can launch the `.exe` directly. Run `build.cmd` whenever you want to rebuild it.

The build uses the 64-bit .NET Framework C# compiler included with Windows:

```bat
build.cmd
```

Contributors with a .NET SDK can use the project build instead:

```powershell
dotnet build Mirchi.csproj --configuration Release
```

For macOS build instructions, see [`mac/README.md`](mac/README.md).

Mirchi currently ships as source rather than a signed installer. It does not add itself to startup or install files elsewhere. To uninstall it, quit the app and delete the folder.

## Automated builds

GitHub Actions builds both platform implementations on every pull request and every push to `main`. Successful runs provide an unsigned Windows executable and an unsigned macOS app bundle as workflow artifacts. These artifacts are for testing; Mirchi does not currently publish signed releases.

## Play

- Drag the top loop to hang it anywhere on the screen.
- Drag and release the charm to flick it.
- Pull the lower charm to stretch it, then release for a snap.
- Roll the mouse wheel over the charm to cycle designs.
- Double-click for a quick wiggle.
- Right-click for the design picker, Wiggle, Reset, or Quit.

## Designs

- Nimbu Mirchi — India
- Nazar — Türkiye
- Hamsa — North Africa and West Asia
- Omamori — Japan
- Cornicello — Italy
- Lucky Knot and Coin — China

## Privacy

Mirchi is local and deliberately has no networking, storage, telemetry, clipboard, camera, microphone, shell, startup, or background-service code. It only uses native drawing, pointer input, a timer for the pendulum, and the primary screen size for its initial placement. It does not save its position or settings.

## License

Mirchi is available under the [MIT License](LICENSE).
