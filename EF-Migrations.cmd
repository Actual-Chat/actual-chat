@echo off
set project=%1
if "%project%"=="" (
    echo No PROJECT argument.
    echo Usage:   EF-Migrations PROJECT COMMAND [options]
    echo Example: EF-Migrations Chat.Service list
    exit 1
)
set mproject=%project%^.Migration
echo Make sure the project is built before you use this script to add migrations!
dotnet ef migrations --no-build --msbuildprojectextensionspath artifacts/obj/%mproject% --project src/dotnet/%mproject%/%mproject%.csproj %2 %3 %4 %5 %6 %7 %8 %9
