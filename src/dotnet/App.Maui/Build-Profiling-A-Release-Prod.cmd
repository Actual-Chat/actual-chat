dotnet publish -f:net9.0-android /p:IsProfilingEnabled=true -p:IsDevMaui=false -c:Release
adb install -r ..\..\..\artifacts\publish\App.Maui\release_net9.0-android\chat.actual.app-Signed.apk
