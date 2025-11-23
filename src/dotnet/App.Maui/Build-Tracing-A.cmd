dotnet publish -f:net10.0-android -p:IsTracingEnabled=true -p:EmbedAssembliesIntoApk=true -c:Debug
adb install -r ..\..\..\artifacts\publish\App.Maui\debug_net10.0-android\chat.actual.dev.app-Signed.apk
