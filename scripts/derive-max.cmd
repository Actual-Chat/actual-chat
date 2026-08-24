:<<BATCH
    @echo off
    setlocal

    set script=%~dp0l10n\derive-max.py

    where python >nul 2>&1
    if not errorlevel 1 (
        python "%script%" %*
        exit /b %errorlevel%
    )
    where py >nul 2>&1
    if not errorlevel 1 (
        py -3 "%script%" %*
        exit /b %errorlevel%
    )
    echo ERROR: Python 3 not found in PATH.
    echo Install with one of:
    echo   winget install Python.Python.3.12
    echo   scoop install python
    exit /b 1
BATCH

#!/bin/sh
set -e

script="$(cd "$(dirname "$0")" && pwd)/l10n/derive-max.py"

if command -v python3 >/dev/null 2>&1; then
    exec python3 "$script" "$@"
elif command -v python >/dev/null 2>&1; then
    exec python "$script" "$@"
fi

echo "ERROR: Python 3 not found in PATH." >&2
echo "Install with one of:" >&2
echo "  brew install python           # macOS" >&2
echo "  sudo apt install python3      # Debian/Ubuntu" >&2
exit 1
