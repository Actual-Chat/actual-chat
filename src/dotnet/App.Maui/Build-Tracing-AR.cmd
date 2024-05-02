dotnet publish -f:net8.0-android -p:IsTracingEnabled=true -p:EmbedAssembliesIntoApk=true -c:Release
adb install -r ..\..\..\artifacts\publish\App.Maui\debug_net8.0-android\chat.actual.dev.app-Signed.apk

