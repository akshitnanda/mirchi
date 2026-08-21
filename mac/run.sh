#!/bin/zsh
set -euo pipefail

project_dir="${0:A:h}"
if [[ ! -d "$project_dir/Mirchi.app" ]]; then
  "$project_dir/build-app.sh"
fi
open "$project_dir/Mirchi.app"
