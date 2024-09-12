dotnet publish -f:net9.0-android /p:IsProfilingEnabled=true -c:Release
adb install -r ..\..\..\artifacts\publish\App.Maui\release_net9.0-android\chat.actual.dev.app-Signed.apk
