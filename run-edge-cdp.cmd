@echo off
setlocal

set "EDGE_CDP_PORT=9223"
set "EDGE_EXE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE_EXE%" set "EDGE_EXE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"

"%EDGE_EXE%" ^
  --remote-debugging-port=%EDGE_CDP_PORT% ^
  --user-data-dir="%LOCALAPPDATA%\Microsoft\Edge\User Data" ^
  --profile-directory="Default"
