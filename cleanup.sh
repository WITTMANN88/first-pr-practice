#!/usr/bin/env bash
#
# cleanup.sh - clean temporary files and reclaim disk space.
# Works on Linux and macOS. Safe by default: use --dry-run to preview.
#
# Usage:
#   ./cleanup.sh [--dry-run] [--yes]
#
#   --dry-run   Show what would be removed, but don't delete anything.
#   --yes       Skip the confirmation prompt.

set -euo pipefail

DRY_RUN=false
ASSUME_YES=false

for arg in "$@"; do
    case "$arg" in
        --dry-run) DRY_RUN=true ;;
        --yes) ASSUME_YES=true ;;
        -h|--help)
            grep '^#' "$0" | sed 's/^#//'
            exit 0
            ;;
        *)
            echo "Unknown option: $arg" >&2
            exit 1
            ;;
    esac
done

OS="$(uname -s)"

run() {
    if $DRY_RUN; then
        echo "[dry-run] $*"
    else
        eval "$@" 2>/dev/null || true
    fi
}

disk_usage() {
    if [[ "$OS" == "Darwin" ]]; then
        df -h / | awk 'NR==2 {print $4 " free of " $2}'
    else
        df -h / | awk 'NR==2 {print $4 " free of " $2}'
    fi
}

echo "Disk space before: $(disk_usage)"
echo

if ! $ASSUME_YES && ! $DRY_RUN; then
    read -r -p "This will delete temporary files and caches. Continue? [y/N] " reply
    if [[ ! "$reply" =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 0
    fi
fi

echo "Cleaning system temp files..."
run "find /tmp -mindepth 1 -type f -atime +1 -delete"
run "find /tmp -mindepth 1 -type d -empty -delete"

echo "Cleaning user cache directory..."
if [[ -d "$HOME/.cache" ]]; then
    run "find \"$HOME/.cache\" -mindepth 1 -type f -atime +7 -delete"
fi

if [[ "$OS" == "Darwin" ]]; then
    echo "Emptying macOS Trash..."
    run "rm -rf \"$HOME/.Trash\"/* 2>/dev/null"

    if command -v brew >/dev/null 2>&1; then
        echo "Cleaning Homebrew cache..."
        run "brew cleanup -s"
    fi
else
    echo "Emptying Trash..."
    run "rm -rf \"$HOME/.local/share/Trash/files\"/* \"$HOME/.local/share/Trash/info\"/* 2>/dev/null"

    if command -v apt-get >/dev/null 2>&1; then
        echo "Cleaning apt cache..."
        run "sudo apt-get clean"
        run "sudo apt-get autoremove -y"
    fi
fi

if command -v npm >/dev/null 2>&1; then
    echo "Cleaning npm cache..."
    run "npm cache clean --force"
fi

if command -v pip >/dev/null 2>&1; then
    echo "Cleaning pip cache..."
    run "pip cache purge"
fi

echo "Truncating large log files under /var/log (root required)..."
run "find /var/log -type f -name '*.log' -size +50M -exec truncate -s 0 {} +"

echo
echo "Disk space after: $(disk_usage)"
