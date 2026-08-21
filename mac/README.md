# Mirchi for macOS

Native AppKit version of the Mirchi overlay for macOS 13 or newer. It contains the same six charm designs, flexible-chain swing, elastic bottom pull, snap-back, repositionable hook, and local-only privacy model as the Windows version.

## Build

On a Mac with Xcode Command Line Tools installed:

```zsh
cd mirchi/mac
chmod +x build-app.sh run.sh
./build-app.sh
open Mirchi.app
```

Or run `./run.sh` to build on first launch.

The build creates `Mirchi.app` inside this directory. Mirchi is not currently code-signed or notarized, and this macOS source has not yet been compile-tested on a Mac.

## Controls

- Drag the top loop to reposition the hook.
- Drag or flick the pendant.
- Pull the bottom to stretch it, then release to snap.
- Scroll over the charm to cycle designs.
- Double-click for a wiggle.
- Right-click for the design picker, Reset, or Quit.

## Privacy

The app has no network, file-storage, telemetry, clipboard, camera, microphone, contacts, location, accessibility, input-monitoring, or background-service code. Its property list requests no protected capabilities or entitlements. It reads only its own window's pointer events and the main screen bounds required for initial placement.

## Remove

Choose **Quit** from the right-click menu, then delete `Mirchi.app` or the repository folder. The app does not install a login item, helper, or settings file.
