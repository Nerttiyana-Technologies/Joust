#!/usr/bin/env bash
#
# Builds the Joust Rust shim and copies the artifact into the per-RID layout
# (native/runtimes/<rid>/native/) that the .NET resolver and packaging expect.
#
# Usage:
#   ./build-native.sh            # auto-detects the current RID
#   ./build-native.sh osx-arm64  # explicit RID
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHIM_DIR="$SCRIPT_DIR/native/shim"

echo "==> Building shim (release)"
( cd "$SHIM_DIR" && cargo build --release )

RID="${1:-}"
if [ -z "$RID" ]; then
  OS_NAME="$(uname -s)"; ARCH_NAME="$(uname -m)"
  case "$OS_NAME" in
    Darwin) OS=osx ;;
    Linux)  OS=linux ;;
    *)      OS=unknown ;;
  esac
  case "$ARCH_NAME" in
    arm64|aarch64) ARCH=arm64 ;;
    x86_64)        ARCH=x64 ;;
    *)             ARCH="$ARCH_NAME" ;;
  esac
  RID="$OS-$ARCH"
fi

case "$RID" in
  win-*) FILE="joust_lancedb.dll" ;;
  osx-*) FILE="libjoust_lancedb.dylib" ;;
  *)     FILE="libjoust_lancedb.so" ;;
esac

SRC="$SHIM_DIR/target/release/$FILE"
DEST="$SCRIPT_DIR/native/runtimes/$RID/native"

if [ ! -f "$SRC" ]; then
  echo "ERROR: expected artifact not found: $SRC" >&2
  echo "Check the crate's [lib] name and crate-type in native/shim/Cargo.toml." >&2
  exit 1
fi

mkdir -p "$DEST"
cp "$SRC" "$DEST/$FILE"
echo "==> Copied $FILE -> native/runtimes/$RID/native/"
echo "==> Now rebuild .NET so the binary flows to the output:  dotnet build && dotnet test"
