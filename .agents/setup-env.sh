#!/usr/bin/env bash
set -euo pipefail

# Idempotent, non-interactive cloud Linux environment setup for RevitGeoSuite.
# Installs user-space toolchains (.NET 8 SDK, Revit 2024 API assemblies, Mono 6.12
# for net48 xUnit tests, PowerShell 7.4 LTS for VerifyContracts.ps1), persists
# environment exports in ~/.bashrc, and restores .NET and web UI dependencies.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

ensure_bashrc_line() {
  local line="$1"
  touch "$HOME/.bashrc"
  grep -qxF "$line" "$HOME/.bashrc" || printf '%s\n' "$line" >> "$HOME/.bashrc"
}

# Strip any non-interactive early-return guard from ~/.bashrc so non-interactive
# subshells sourcing ~/.bashrc via BASH_ENV pick up PATH and env exports.
sed -i '/case \$- in/,/esac/d' "$HOME/.bashrc" 2>/dev/null || true

# 1. Install .NET 8.0 SDK into ~/.dotnet
if [ ! -x "$HOME/.dotnet/dotnet" ] || ! "$HOME/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'; then
  tmp_dotnet_install="$(mktemp /tmp/dotnet-install.XXXXXX.sh)"
  trap 'rm -f "$tmp_dotnet_install"' EXIT
  curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$tmp_dotnet_install"
  bash "$tmp_dotnet_install" --channel 8.0 --install-dir "$HOME/.dotnet" --no-path
  rm -f "$tmp_dotnet_install"
  trap - EXIT
fi

# 2. Provision Revit 2024 API reference assemblies (matching .github/workflows/ci.yml)
REVIT_API_ROOT="$HOME/.local/share/revit-api-2024"
REVIT_API_DIR="$REVIT_API_ROOT/lib/NET480"
REVIT_API_URL="https://api.nuget.org/v3-flatcontainer/revit_api_x64/2024.0.2/revit_api_x64.2024.0.2.nupkg"
REVIT_API_SHA256="0065a7c8ad8b51011fda19ecdbe299aab9cac0e1f2aab62f91c4d45fadd5d65b"

if [ ! -f "$REVIT_API_DIR/RevitAPI.dll" ] || [ ! -f "$REVIT_API_DIR/RevitAPIUI.dll" ]; then
  mkdir -p "$REVIT_API_ROOT"
  tmp_revit_pkg="$(mktemp /tmp/revit-api.XXXXXX.nupkg)"
  trap 'rm -f "$tmp_revit_pkg"' EXIT
  curl -fsSL "$REVIT_API_URL" -o "$tmp_revit_pkg"
  printf '%s  %s\n' "$REVIT_API_SHA256" "$tmp_revit_pkg" | sha256sum -c -
  python3 -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$tmp_revit_pkg" "$REVIT_API_ROOT"
  rm -f "$tmp_revit_pkg"
  trap - EXIT
fi

# 3. Install user-space Mono 6.12 via micromamba so net48 xUnit tests can execute on Linux
if [ ! -x "$HOME/.local/mono/bin/mono" ]; then
  mkdir -p "$HOME/.local/bin"
  if [ ! -x "$HOME/.local/bin/micromamba" ]; then
    curl -fsSL "https://micro.mamba.pm/api/micromamba/linux-64/latest" \
      | tar -xj -C "$HOME/.local/bin" --strip-components=1 bin/micromamba
  fi
  "$HOME/.local/bin/micromamba" create -y -p "$HOME/.local/mono" -c conda-forge mono
fi

# Map libdl for SQLitePCLRaw / GeoPackage tests under Debian 12+ (glibc 2.36+)
MONO_CONFIG="$HOME/.local/mono/etc/mono/config"
if [ -f "$MONO_CONFIG" ] && ! grep -q 'dll="dl"' "$MONO_CONFIG"; then
  sed -i 's|</configuration>|\t<dllmap dll="dl" target="libdl.so.2" os="!windows"/>\n</configuration>|' "$MONO_CONFIG"
fi

# Export environment for current shell and persist idempotently in ~/.bashrc
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/bin:$HOME/.local/mono/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export RevitInstallDir="$REVIT_API_DIR"
export RevitApiDir="$REVIT_API_DIR"
export MONO_PATH="$REVIT_API_DIR"
export TEMP="${TEMP:-/tmp}"

ensure_bashrc_line 'export DOTNET_ROOT="$HOME/.dotnet"'
ensure_bashrc_line 'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/bin:$HOME/.local/mono/bin:$PATH"'
ensure_bashrc_line 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
ensure_bashrc_line 'export RevitInstallDir="$HOME/.local/share/revit-api-2024/lib/NET480"'
ensure_bashrc_line 'export RevitApiDir="$HOME/.local/share/revit-api-2024/lib/NET480"'
ensure_bashrc_line 'export MONO_PATH="$HOME/.local/share/revit-api-2024/lib/NET480"'
ensure_bashrc_line 'export TEMP="${TEMP:-/tmp}"'

if [ -x /usr/bin/chromium ]; then
  export REVITGEOSUITE_TEST_CHROMIUM="/usr/bin/chromium"
  ensure_bashrc_line 'export REVITGEOSUITE_TEST_CHROMIUM="/usr/bin/chromium"'
fi

# 4. Install PowerShell 7.4 LTS global tool for tools/VerifyContracts.ps1
if [ ! -x "$HOME/.dotnet/tools/pwsh" ]; then
  dotnet tool install -g PowerShell --version 7.4.7
fi

# 5. Restore .NET solution packages and Svelte web UI dependencies
dotnet restore "$REPO_ROOT/RevitGeoSuite.sln"
(cd "$REPO_ROOT/src/RevitGeoSuite.SharedUI.Web" && npm ci)
