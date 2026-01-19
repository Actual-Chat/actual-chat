@echo off
setlocal enabledelayedexpansion

:: Project mappings (Windows folder name -> Docker path)
set "MAP_ActualLab.Fusion=/fusion"
set "MAP_ActualLab.Fusion.Samples=/fusion-samples"
set "MAP_ActualChat=/actual-chat"

:: Find the project root by traversing up from current directory
set "CURRENT_DIR=%CD%"
set "PROJECT_ROOT="
set "PROJECT_NAME="

:: Start from current directory and go up
set "CHECK_DIR=%CD%"
:find_project_loop
for %%I in ("%CHECK_DIR%") do set "DIR_NAME=%%~nxI"

:: Check if this directory matches a known project
if defined MAP_%DIR_NAME% (
    set "PROJECT_ROOT=%CHECK_DIR%"
    set "PROJECT_NAME=%DIR_NAME%"
    call set "DOCKER_PROJECT_PATH=%%MAP_%DIR_NAME%%%"
    goto :found_project
)

:: Go up one level
for %%I in ("%CHECK_DIR%\..") do set "PARENT_DIR=%%~fI"
if "%PARENT_DIR%"=="%CHECK_DIR%" (
    echo Error: Could not find a known project root.
    echo Known projects: ActualLab.Fusion, ActualLab.Fusion.Samples, ActualChat
    exit /b 1
)
set "CHECK_DIR=%PARENT_DIR%"
goto :find_project_loop

:found_project
:: Calculate relative path from project root to current directory
:: Remove PROJECT_ROOT prefix from CURRENT_DIR to get REL_PATH
call set "REL_PATH=%%CURRENT_DIR:!PROJECT_ROOT!=%%"

:: Convert backslashes to forward slashes for Docker
set "REL_PATH=!REL_PATH:\=/!"

:: Build the Docker working directory
set "DOCKER_WORKDIR=%DOCKER_PROJECT_PATH%%REL_PATH%"

echo Starting Claude in project: %PROJECT_NAME%
echo Docker working directory: %DOCKER_WORKDIR%

wt docker run -it --rm ^
  -v "D:\Projects\ActualLab.Fusion:/fusion" ^
  -v "D:\Projects\ActualLab.Fusion.Samples:/fusion-samples" ^
  -v "D:\Projects\ActualChat:/actual-chat" ^
  -v "%USERPROFILE%\.claude:/home/claude/.claude" ^
  -v "%USERPROFILE%\.claude.json:/home/claude/.claude.json" ^
  -e ANTHROPIC_API_KEY=%ANTHROPIC_API_KEY% ^
  -e Claude_GeminiAPIKey=%Claude_GeminiAPIKey% ^
  --network host ^
  -w "%DOCKER_WORKDIR%" ^
  claude-fusion ^
  claude --dangerously-skip-permissions %*

endlocal
