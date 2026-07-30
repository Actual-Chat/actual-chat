dotnet publish -f:net11.0-android -p:IsTracingEnabled=true -p:EmbedAssembliesIntoApk=true -c:Release
adb install -r ..\..\..\artifacts\publish\App.Maui\release_net11.0-android\chat.actual.dev.app-Signed.apk
