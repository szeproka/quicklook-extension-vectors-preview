# QuickLook vector preview (.ai)

A plugin for [QuickLook](https://github.com/QL-Win/QuickLook) on Windows. It previews Illustrator
`.ai` files on a **transparent background** (a checkerboard), so white art on a white artboard is
visible.

---

## Why

A PDF-compatible `.ai` file is a PDF inside. QuickLook's built-in PDF viewer opens it and paints the
page white, like every PDF viewer does. White art on that white page can't be seen.

This plugin renders the artboards with pdfium into a bitmap that keeps the page transparent, then
shows it over a background you choose.

---

## Features

- Every artboard is shown, one under the other, each labelled "Artboard N of M".

- The edges of each artboard are outlined.

- Press **B**, or click the button in the bottom-right corner, to cycle the background:
  dark checker → light checker → black → gray → white. The choice is remembered.

- Files are read into memory, so Illustrator can still save them while a preview is open.

- Old PostScript `.ai` files (no PDF content) are left to QuickLook's other viewers.

---

## Install

### From a release (no build needed)

1. Download `QuickLook.Plugin.AiViewer.qlplugin` from the
   [latest release](https://github.com/szeproka/quicklook-extension-vectors-preview/releases/latest).

2. Select the file in Explorer and press Space. QuickLook shows an install page: click **Install**.

3. Restart QuickLook (right-click its tray icon → Quit, then start it again).

It needs 64-bit QuickLook 4.x on Windows.

### From source

On a PC with QuickLook (Store or installer version) and the .NET SDK:

```powershell
.\AiViewer\install.ps1
```

The script will:

1. Build the plugin.

2. Copy QuickLook's own x64 `pdfium.dll` next to it as `pdfium_ai.dll`.

3. Install it in QuickLook's plugin data folder.

4. Restart QuickLook.

It also writes `AiViewer\dist\QuickLook.Plugin.AiViewer.qlplugin`, the file attached to releases.
`install.ps1 -PackageOnly` only builds that file, without installing anything.

**Uninstall:** delete the `QuickLook.Plugin.AiViewer` folder from QuickLook's `QuickLook.Plugin` data
folder, then restart QuickLook.

---

## How it works

| File | Role |
|---|---|
| [`AiViewer/src/Plugin.cs`](AiViewer/src/Plugin.cs) | QuickLook entry point. Priority 10, above the built-in PDF viewer (-1). Claims `.ai` files whose first 1 KB contains `%PDF`. |
| [`AiViewer/src/AiDocument.cs`](AiViewer/src/AiDocument.cs) | pdfium wrapper. Renders a page into a BGRA bitmap filled with transparent pixels. |
| [`AiViewer/src/AiPanel.cs`](AiViewer/src/AiPanel.cs) | The WPF viewer: artboard stack, backgrounds, B key. |
| [`AiViewer/install.ps1`](AiViewer/install.ps1) | Build, bundle pdfium, install, restart QuickLook, make the `.qlplugin`. |
| [`AiViewer/tools/test-render.ps1`](AiViewer/tools/test-render.ps1) | Headless test: renders files to PNG and prints pixel values. |
| [`AiViewer/tools/make-test-ai.ps1`](AiViewer/tools/make-test-ai.ps1) | Writes a white-on-white, two-artboard test file. |

Notes:

- The plugin targets .NET Framework 4.6.2, like QuickLook 4.x.

- Its pdfium copy is renamed to `pdfium_ai.dll`. QuickLook's PDF viewer loads its own `pdfium.dll`
  into the same process, and a shared module would share pdfium's global state.

- Only x64 pdfium is bundled. In a 32-bit QuickLook the plugin steps aside and the stock viewers
  are used.

---

## Test

```powershell
powershell -STA -File AiViewer\tools\test-render.ps1 -PluginDir AiViewer\bin\Release -Files AiViewer\test\white-on-white.ai -OutDir out
```

---

## License

[GPL-3.0](LICENSE), like QuickLook itself.

The release package bundles pdfium (BSD-3-Clause / Apache-2.0). See
[THIRD-PARTY-NOTICES.txt](AiViewer/THIRD-PARTY-NOTICES.txt).
