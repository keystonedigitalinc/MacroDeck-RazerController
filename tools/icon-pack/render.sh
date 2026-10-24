#!/bin/sh
# usage: render.sh [names...]  -> svg in ./svg, black-bg png in raw dir, then convert
here=$(cd "$(dirname "$0")" && pwd -W)
raw="$here/raw"
rm -rf "$raw"; mkdir -p "$raw"
rm -f "$here/svg/"*.svg
node "$here/build.js" "$here/svg" "$@"
for f in "$here/svg/"*.svg; do
  n=$(basename "$f" .svg)
  "/c/Program Files/Google/Chrome/Application/chrome.exe" --headless=new --disable-gpu --hide-scrollbars \
    --window-size=1024,1024 --screenshot="$raw/$n.png" "file:///$f" >/dev/null 2>&1
done
powershell -NoProfile -ExecutionPolicy Bypass -File "$here/convert.ps1" -In "$raw" -Out "C:/Users/chadh/Pictures/Razer App Controller/icons"
