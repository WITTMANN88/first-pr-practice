#!/usr/bin/env bash
# Package the three published flavors as release assets and write release notes.
#
#   prepare-release.sh <tag> <git-ref> <artifacts-dir> <out-dir> <repo-url>
#
#   <tag>            vMAJOR.MINOR.PATCH[-prerelease]; names the assets
#   <git-ref>        end of the changelog range (the tag, or HEAD for a preview)
#   <artifacts-dir>  STAKEOUT-{standard,lite,trimmed}/ as uploaded by the publish jobs
#   <out-dir>        receives the assets; the notes go to <out-dir>/../release-notes.md
#
# Fails if any flavor is missing or its SHA-256 no longer matches the one the
# publish job recorded (the file changed between build and release).
set -euo pipefail

tag="$1" ref="$2" in="$3" out="$4" repo_url="$5"

if [[ ! "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "::error::'$tag' is not vMAJOR.MINOR.PATCH[-prerelease]" >&2
  exit 1
fi

flavors=(standard lite trimmed)
declare -A asset_suffix=(
  [standard]="win-x64"
  [lite]="win-x64-lite"
  [trimmed]="win-x64-trimmed-EXPERIMENTAL"
)
declare -A description=(
  [standard]="self-contained single file; runs on any Windows 10/11 x64"
  [lite]="framework-dependent; requires the .NET 8 Desktop Runtime (x64)"
  [trimmed]="**experimental** forced trimming, unsupported by Microsoft; verify before use"
)

rm -rf "$out"
mkdir -p "$out"
notes="$(dirname "$out")/release-notes.md"

for flavor in "${flavors[@]}"; do
  src="$in/STAKEOUT-$flavor"
  if [[ ! -f "$src/STAKEOUT.exe" || ! -f "$src/STAKEOUT.exe.sha256" ]]; then
    echo "::error::the $flavor build is missing from $in" >&2
    exit 1
  fi
  # Integrity: the hash recorded at build time must still hold (CRLF-tolerant).
  (cd "$src" && tr -d '\r' < STAKEOUT.exe.sha256 | sha256sum --check --strict --quiet -)

  name="STAKEOUT-$tag-${asset_suffix[$flavor]}.exe"
  cp "$src/STAKEOUT.exe" "$out/$name"
  (cd "$out" && sha256sum --binary "$name" > "$name.sha256")
done
(cd "$out" && cat ./*.exe.sha256 > SHA256SUMS.txt)

# Changelog: commits since the previous version tag reachable from <git-ref>.
prev="$(git describe --tags --abbrev=0 --match 'v[0-9]*.[0-9]*.[0-9]*' "$ref^" 2>/dev/null || true)"
if [[ -n "$prev" ]]; then
  range="$prev..$ref"
  heading="Changes since $prev"
else
  range="$ref"
  heading="Changes"
fi

{
  echo "## STAKEOUT $tag"
  echo
  echo "### $heading"
  echo
  git log --no-merges --pretty='format:- %s (%h)' "$range"
  echo
  echo
  echo "### Downloads"
  echo
  echo "| File | Size | SHA-256 |"
  echo "|---|---|---|"
  for flavor in "${flavors[@]}"; do
    name="STAKEOUT-$tag-${asset_suffix[$flavor]}.exe"
    size="$(stat -c %s "$out/$name" | awk '{ printf "%.1f MB", $1 / 1048576 }')"
    hash="$(cut -d ' ' -f 1 "$out/$name.sha256")"
    echo "| \`$name\` | $size | \`$hash\` |"
  done
  echo
  for flavor in "${flavors[@]}"; do
    echo "- **$flavor**: ${description[$flavor]}."
  done
  echo
  echo "Verify a download: \`sha256sum -c SHA256SUMS.txt\` (Linux/WSL) or"
  echo "\`Get-FileHash <file> -Algorithm SHA256\` (PowerShell) against the table above."
  if [[ -n "$prev" ]]; then
    echo
    echo "**Full diff:** $repo_url/compare/$prev...$tag"
  fi
} > "$notes"

echo "Assets in $out:"
ls -l "$out"
echo "Notes: $notes"
