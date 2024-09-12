dotnet publish -f:net9.0-android -p:IsProfilingEnabled=true -p:EmbedAssembliesIntoApk=true -c:Debug
adb install -r ..\..\..\artifacts\publish\App.Maui\debug_net9.0-android\chat.actual.dev.app-Signed.apk
