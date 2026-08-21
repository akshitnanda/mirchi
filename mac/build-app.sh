#!/bin/zsh
set -euo pipefail

project_dir="${0:A:h}"
cd "$project_dir"

swift build -c release

app_dir="$project_dir/Mirchi.app"
if [[ -d "$app_dir" ]]; then
  rm -rf "$app_dir"
fi

mkdir -p "$app_dir/Contents/MacOS"
cp ".build/release/MirchiMac" "$app_dir/Contents/MacOS/MirchiMac"
cp "$project_dir/Info.plist" "$app_dir/Contents/Info.plist"
chmod +x "$app_dir/Contents/MacOS/MirchiMac"

echo "Built $app_dir"
