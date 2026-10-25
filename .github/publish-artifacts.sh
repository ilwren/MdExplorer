#!/usr/bin/env bash
# 将文件发布到 ci-artifacts 分支（沙箱无法下载 Actions 工件/日志 blob 时的回传通道）。
# 用法: publish-artifacts.sh <subdir> <file...>   需要 env: GH_TOKEN, GITHUB_REPOSITORY
set -uo pipefail
SUB="${1:?subdir required}"; shift
WORK="$(mktemp -d)"
SRC_DIR="$WORK/src"; mkdir -p "$SRC_DIR"
for f in "$@"; do
  if [ -e "$f" ]; then cp -r "$f" "$SRC_DIR/"; fi
done
if [ -z "$(ls -A "$SRC_DIR" 2>/dev/null)" ]; then echo "nothing to publish"; exit 0; fi
REPO="https://x-access-token:${GH_TOKEN}@github.com/${GITHUB_REPOSITORY}.git"
DEST="$WORK/repo"
if git ls-remote --exit-code --heads "$REPO" ci-artifacts >/dev/null 2>&1; then
  git clone --depth 1 --branch ci-artifacts "$REPO" "$DEST" || exit 0
else
  mkdir -p "$DEST"; cd "$DEST" || exit 0
  git init -b ci-artifacts >/dev/null 2>&1
  git remote add origin "$REPO"
  printf '# CI artifacts\n\nAuto-generated screenshots and test logs. Do not edit.\n' > README.md
  git add README.md
  git -c user.name=ci -c user.email=ci@users.noreply.github.com commit -m "init" >/dev/null
  cd - >/dev/null || true
fi
mkdir -p "$DEST/$SUB"
find "$DEST/$SUB" -mindepth 1 -delete 2>/dev/null || true
cp -r "$SRC_DIR"/. "$DEST/$SUB/"
cd "$DEST" || exit 0
git add -A
if ! git -c user.name=ci -c user.email=ci@users.noreply.github.com commit -m "ci($SUB): run ${GITHUB_RUN_ID:-local} sha ${GITHUB_SHA:-}" >/dev/null 2>&1; then
  echo "no changes"; exit 0
fi
for i in 1 2 3 4 5; do
  if git push origin ci-artifacts; then exit 0; fi
  echo "push retry $i"; git pull --rebase origin ci-artifacts || true; sleep 3
done
echo "publish gave up (race)"; exit 0
