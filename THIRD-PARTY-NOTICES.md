# Third-party notices

UGreen Capture Card Viewer's own source code and artwork are released under the
[MIT License](LICENSE). It depends on the following third-party software, which
keeps its own license.

## DirectShowLib.Standard 2.1.0

- Project: https://github.com/luthfiampas/DirectShowLib (a .NET Standard port of
  DirectShowLib, http://directshownet.sourceforge.net/)
- License: GNU Lesser General Public License v2.1, full text in
  [licenses/DirectShowLib-LGPL-2.1.txt](licenses/DirectShowLib-LGPL-2.1.txt)
- Used unmodified, as a NuGet package reference. You can swap in your own build of
  the library by changing the `PackageReference` in `UGreenCapViewer.csproj` (or
  replacing it with a project/DLL reference) and rebuilding from this source.

## Artwork

The app icon (`art/app.ico`, `art/app.png`) and the "waiting" background
(`art/nosignal.jpg`) were generated locally with ComfyUI using the
Z-Image Turbo model, via `tools/gen_art.py`. They are covered by this project's
MIT License.

## Trademarks

"UGREEN" is a trademark of its owner. This project is an independent, unofficial
viewer and is not affiliated with or endorsed by UGREEN.
