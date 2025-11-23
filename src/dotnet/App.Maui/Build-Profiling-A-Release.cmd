dotnet publish -f:net10.0-android /p:IsProfilingEnabled=true -c:Release
adb install -r ..\..\..\artifacts\publish\App.Maui\release_net10.0-android\chat.actual.dev.app-Signed.apk
