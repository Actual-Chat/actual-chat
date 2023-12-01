dotnet publish -f:net8.0-android /p:IsProfilingEnabled=true -c:Debug
adb install -r ..\..\..\artifacts\publish\App.Maui\debug_net8.0-android\chat.actual.dev.app-Signed.apk
call _StartDevApp.cmd
