#!/usr/bin/env bash
# Adds files to a GitHub Release, waiting for it to exist (the Windows workflow creates v<version> when it finishes).
# Usage: attach-to-release.sh v1.6.0 file...
set -euo pipefail
tag="$1"; shift
for i in $(seq 1 90); do
  if gh release view "$tag" --repo "$GITHUB_REPOSITORY" > /dev/null 2>&1; then
    have=$(gh release view "$tag" --repo "$GITHUB_REPOSITORY" --json assets -q '.assets[].name')
    todo=()
    for f in "$@"; do
      if grep -qxF "$(basename "$f")" <<< "$have"; then echo "Already there: $(basename "$f")"; else todo+=("$f"); fi
    done
    if [ ${#todo[@]} -gt 0 ]; then gh release upload "$tag" "${todo[@]}" --repo "$GITHUB_REPOSITORY"; echo "Added to $tag: ${todo[*]}"; fi
    exit 0
  fi
  echo "Waiting for release $tag ($i/90)…"
  sleep 60
done
echo "::error::Release $tag didn't appear within 90 minutes"
exit 1
