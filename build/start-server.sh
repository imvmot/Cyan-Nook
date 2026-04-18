#!/usr/bin/env bash
# Cyan Nook - Local WebGL Server (macOS / Linux)

set -e

cd "$(dirname "$0")"

PORT="${PORT:-8080}"

echo "============================================"
echo "  Cyan Nook - Local WebGL Server"
echo "  http://localhost:${PORT}"
echo "  Press Ctrl+C to stop"
echo "============================================"
echo

if ! command -v node >/dev/null 2>&1; then
    echo "ERROR: Node.js is not installed."
    echo
    echo "Please install Node.js 18+ from one of the following:"
    echo "  macOS (Homebrew): brew install node"
    echo "  Debian/Ubuntu:    sudo apt install nodejs"
    echo "  Official:         https://nodejs.org/"
    exit 1
fi

PORT="$PORT" node server.js
