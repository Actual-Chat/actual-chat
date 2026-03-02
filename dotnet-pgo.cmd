:<<"::CMDLITERAL"
@echo off
dotnet "%~dp0tools\dotnet-pgo\dotnet-pgo.dll" %*
exit /b %errorlevel%
::CMDLITERAL
exec dotnet "$(dirname "$0")/tools/dotnet-pgo/dotnet-pgo.dll" "$@"
