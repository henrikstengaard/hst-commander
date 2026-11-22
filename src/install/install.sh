#!/usr/bin/env bash
# Installs the Hst Commander TUI for the current user on macOS and Linux from a GitHub release, and adds it to the
# path, e.g.:
#   curl -fsSL https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.sh | bash
#   curl -fsSL https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.sh | bash -s -- --smb
#
# Options:
#   --smb              also installs the SMB plugin
#   --smb-only         only installs the SMB plugin (e.g. for the GUI)
#   --version <tag>    installs the release with the tag, e.g. 1.0.190 (also a prerelease), instead of the latest
#   --uninstall        removes the TUI and its link (the settings and plugins in ~/.hst-commander are kept)
#
# The TUI is installed in ~/.local/share/hst-commander/tui with a link ~/.local/bin/hst.commander; ~/.local/bin is
# added to the path in the shell's profile when it isn't on the path. The SMB plugin is installed in
# ~/.hst-commander/plugins. Installing again updates them. Needs curl or wget, tar and on macOS hdiutil; unzip for the
# SMB plugin.
set -euo pipefail

repository=henrikstengaard/hst-commander
install_dir="$HOME/.local/share/hst-commander/tui"
bin_dir="$HOME/.local/bin"
exe=hst.commander
plugins_dir="$HOME/.hst-commander/plugins"
plugin_name=Hst.Commander.Plugins.Smb

tui=1
smb=0
uninstall=0
tag=""
while [ $# -gt 0 ]; do
  case "$1" in
    --smb) smb=1; shift ;;
    --smb-only) smb=1; tui=0; shift ;;
    --version) tag="${2:?--version needs a release tag, e.g. 1.0.190}"; shift 2 ;;
    --uninstall) uninstall=1; shift ;;
    -h|--help)
      echo "usage: install.sh [--smb | --smb-only] [--version <tag>] [--uninstall]"
      echo "Installs the Hst Commander TUI in ~/.local/share/hst-commander/tui with a link in ~/.local/bin,"
      echo "--smb also installs the SMB plugin in ~/.hst-commander/plugins, --smb-only only the plugin."
      exit 0
      ;;
    *) echo "Unknown option '$1'" >&2; exit 2 ;;
  esac
done

fail() {
  echo "Error: $*" >&2
  exit 1
}

# downloads a url to a file, or to stdout without a file
download() {
  if command -v curl > /dev/null 2>&1; then
    if [ $# -gt 1 ]; then curl -fsSL -o "$2" "$1"; else curl -fsSL "$1"; fi
  elif command -v wget > /dev/null 2>&1; then
    if [ $# -gt 1 ]; then wget -q -O "$2" "$1"; else wget -q -O - "$1"; fi
  else
    fail "curl or wget is needed to download Hst Commander"
  fi
}

if [ "$uninstall" = 1 ]; then
  if [ -L "$bin_dir/$exe" ]; then
    rm -f "$bin_dir/$exe"
  fi
  rm -rf "$install_dir"
  echo "Hst Commander TUI has been uninstalled. Settings and plugins in ~/.hst-commander are kept."
  exit 0
fi

# the platform and architecture in the names of the release's files, like the publish scripts choose them: on Linux
# the architecture of the user space, as a 32-bit Raspberry Pi OS runs a 64-bit kernel, and on macOS that of the
# Mac, also from a shell running with Rosetta
case "$(uname -s)" in
  Darwin)
    os=macos
    if [ "$(sysctl -n hw.optional.arm64 2> /dev/null || echo 0)" = 1 ]; then arch=arm64; else arch=x64; fi
    tui_suffix="_tui_macos_$arch.dmg"
    ;;
  Linux)
    os=linux
    case "$(uname -m)" in
      x86_64|amd64) arch=x64 ;;
      aarch64|arm64) if [ "$(getconf LONG_BIT)" = 32 ]; then arch=arm; else arch=arm64; fi ;;
      arm*) arch=arm ;;
      *) fail "Hst Commander isn't released for $(uname -m)" ;;
    esac
    tui_suffix="_tui_linux_$arch.tar.gz"
    ;;
  *) fail "install.sh is for macOS and Linux; on Windows run install.ps1 in PowerShell" ;;
esac

# the release: its tag and the urls of its files, read from the github api without jq
if [ -n "$tag" ]; then
  release_url="https://api.github.com/repos/$repository/releases/tags/$tag"
else
  release_url="https://api.github.com/repos/$repository/releases/latest"
fi
release=$(download "$release_url") || fail "Release ${tag:-latest} wasn't found ($release_url)"
tag=$(printf '%s\n' "$release" | grep -o '"tag_name": *"[^"]*"' | head -n 1 | sed 's/.*"\([^"]*\)"$/\1/' || true)
urls=$(printf '%s\n' "$release" | grep -o '"browser_download_url": *"[^"]*"' | sed 's/.*"\(https[^"]*\)"$/\1/' || true)

# the url of the release's file ending with the suffix
asset_url() {
  local url
  url=$(printf '%s\n' "$urls" | grep -F -- "$1" | head -n 1 || true)
  [ -n "$url" ] || fail "Release $tag has no file ending with '$1'"
  printf '%s\n' "$url"
}

work=$(mktemp -d)
mounted=""
cleanup() {
  if [ -n "$mounted" ]; then hdiutil detach -quiet "$mounted" || true; fi
  rm -rf "$work"
}
trap cleanup EXIT

if [ "$tui" = 1 ]; then
  url=$(asset_url "$tui_suffix")
  echo "Downloading Hst Commander TUI $tag for $os $arch"
  download "$url" "$work/tui"
  mkdir -p "$work/files"
  if [ "$os" = macos ]; then
    # the dmg has the tui's files at its root, signed and notarized
    mounted="$work/mnt"
    mkdir -p "$mounted"
    hdiutil attach -quiet -nobrowse -readonly -mountpoint "$mounted" "$work/tui"
    cp -R "$mounted"/* "$work/files/"
    hdiutil detach -quiet "$mounted"
    mounted=""
  else
    tar -xzf "$work/tui" -C "$work/files"
  fi
  [ -f "$work/files/$exe" ] || fail "$(basename "$url") has no $exe"
  chmod 755 "$work/files/$exe"

  rm -rf "$install_dir"
  mkdir -p "$(dirname "$install_dir")" "$bin_dir"
  mv "$work/files" "$install_dir"
  ln -sf "$install_dir/$exe" "$bin_dir/$exe"
  echo "Installed Hst Commander TUI $tag in $install_dir"

  # ~/.local/bin on the path, in the profile of the user's shell
  case ":$PATH:" in
    *":$bin_dir:"*) path_note="" ;;
    *)
      case "$(basename "${SHELL:-sh}")" in
        zsh) profile="$HOME/.zshrc" ;;
        bash) if [ "$os" = macos ]; then profile="$HOME/.bash_profile"; else profile="$HOME/.bashrc"; fi ;;
        fish) profile="" ;;
        *) profile="$HOME/.profile" ;;
      esac
      if [ -z "$profile" ]; then
        path_note="Add $bin_dir to the path with: fish_add_path $bin_dir"
      else
        if ! grep -qs '\.local/bin' "$profile"; then
          printf '\n# added by the Hst Commander installer\nexport PATH="$HOME/.local/bin:$PATH"\n' >> "$profile"
          echo "Added $bin_dir to the path in $profile"
        fi
        path_note="Open a new terminal (or run: export PATH=\"\$HOME/.local/bin:\$PATH\") to start it with $exe"
      fi
      ;;
  esac
fi

if [ "$smb" = 1 ]; then
  command -v unzip > /dev/null 2>&1 || fail "unzip is needed to install the SMB plugin"
  url=$(asset_url "_smb-plugin.zip")
  echo "Downloading the SMB plugin $tag"
  download "$url" "$work/smb-plugin.zip"
  unzip -q "$work/smb-plugin.zip" -d "$work/smb"
  [ -f "$work/smb/$plugin_name/$plugin_name.dll" ] || fail "$(basename "$url") has no $plugin_name"
  # the source of SMBLibrary is kept with the plugin
  cp "$work"/smb/SMBLibrary-source-*.zip "$work/smb/$plugin_name/" 2> /dev/null || true
  mkdir -p "$plugins_dir"
  rm -rf "${plugins_dir:?}/$plugin_name"
  mv "$work/smb/$plugin_name" "$plugins_dir/$plugin_name"
  echo "Installed the SMB plugin $tag in $plugins_dir/$plugin_name"
fi

if [ "$tui" = 1 ]; then
  if [ -n "$path_note" ]; then
    echo "$path_note"
  else
    echo "Start it with $exe"
  fi
fi
