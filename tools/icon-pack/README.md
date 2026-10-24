# Icon pack tooling

The bundled icons (`src/RazerController/icon-packs/icons.macroDeckIconPack`) are drawn in code, not generated.

1. `build.js` holds every icon as SVG. Add or change a `def(...)`.
2. `./render.sh <names...>` renders those icons with headless Chrome and converts them to white-on-transparent PNGs in `C:\Users\chadh\Pictures\Razer App Controller\icons`. `sheet.ps1` makes a contact sheet to check them.
3. `dotnet run --project webpenc -- <png dir> <work>\webp` encodes the PNGs as WebP masters.
4. `node make-pack.js pack <png dir> <work> <out.macroDeckIconPack>` writes the pack in Macro Deck's export format.
5. In `src/RazerController`: `macrodeck-plugin icon-pack add <out.macroDeckIconPack> --key icons --force`. The CLI rewrites `manifest.json` with CRLF line endings; convert it back to LF before committing.

Keep icon names stable: buttons find bundled icons by name.
