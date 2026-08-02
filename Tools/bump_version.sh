#!/usr/bin/env bash
# 커밋 전 VERSION 파일의 버전을 올리는 스크립트.
# 버전 형식: x.xx.xxx (major.minor.patch) — 커밋할 때마다 patch(마지막 자리)를 1 증가시킨다.
# patch가 999를 넘으면 minor를 1 올리고 patch는 0으로, minor가 99를 넘으면 major를 1 올린다.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_FILE="$REPO_ROOT/VERSION"

# 버전 파일이 없으면 최초 버전으로 생성한다.
if [ ! -f "$VERSION_FILE" ]; then
  echo "0.01.000" > "$VERSION_FILE"
fi

current="$(tr -d '[:space:]' < "$VERSION_FILE")"
major="$(echo "$current" | cut -d. -f1)"
minor="$(echo "$current" | cut -d. -f2)"
patch="$(echo "$current" | cut -d. -f3)"

patch=$((10#$patch + 1))
if [ "$patch" -gt 999 ]; then
  patch=0
  minor=$((10#$minor + 1))
  if [ "$minor" -gt 99 ]; then
    minor=0
    major=$((10#$major + 1))
  fi
fi

new_version=$(printf "%d.%02d.%03d" "$major" "$minor" "$patch")
echo "$new_version" > "$VERSION_FILE"
echo "$new_version"
