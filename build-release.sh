#!/usr/bin/env bash
# Build every C# mod in Release and collect the ModBuildConfig zips in dist/.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: ./build-release.sh [--deploy] [-h|--help]

Builds every C# mod (each */*.csproj at the repo root, so new mods are picked
up automatically without touching the solution) in Release configuration and
copies each mod's release zip into ./dist/ (cleaned at the start of every run).

Options:
  --deploy    Also deploy the Release build into the game Mods folder.
              Default: -p:EnableModDeploy=false, so the deployed dev copy is
              left untouched.
  -h, --help  Show this help.

Exits non-zero if any build fails or a mod produces no zip.
EOF
}

deploy=false
for arg in "$@"; do
  case "$arg" in
    --deploy) deploy=true ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; usage >&2; exit 2 ;;
  esac
done

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dist="$root/dist"
cd "$root"

shopt -s nullglob
projects=(*/*.csproj)
shopt -u nullglob
if [[ ${#projects[@]} -eq 0 ]]; then
  echo "No */*.csproj found in $root" >&2
  exit 1
fi

rm -rf "$dist"
mkdir -p "$dist"

deploy_arg="-p:EnableModDeploy=false"
$deploy && deploy_arg="-p:EnableModDeploy=true"

failed=()
for proj in "${projects[@]}"; do
  dir="$(dirname "$proj")"
  echo "==> Building $proj (Release, deploy=$deploy)"
  # Remove old zips so a stale version can't be mistaken for this build's output.
  if [[ -d "$dir/bin/Release" ]]; then
    find "$dir/bin/Release" -name '*.zip' -delete
  fi
  if ! dotnet build "$proj" -c Release --no-incremental -nologo -v minimal "$deploy_arg"; then
    failed+=("$proj (build failed)")
    continue
  fi
  mapfile -t zips < <(find "$dir/bin/Release" -name '*.zip' 2>/dev/null)
  if [[ ${#zips[@]} -eq 0 ]]; then
    failed+=("$proj (no zip produced)")
    continue
  fi
  cp -- "${zips[@]}" "$dist/"
done

echo
echo "Release zips in $dist:"
shopt -s nullglob
for z in "$dist"/*.zip; do echo "  $(basename "$z")"; done
shopt -u nullglob

if [[ ${#failed[@]} -gt 0 ]]; then
  echo >&2
  echo "FAILED:" >&2
  printf '  %s\n' "${failed[@]}" >&2
  exit 1
fi
