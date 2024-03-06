dotnet publish -f:net8.0-android -p:IsTracingEnabled=true -p:IsDevMaui=false -p:EmbedAssembliesIntoApk=true -c:Debug
adb install -r ..\..\..\artifacts\publish\App.Maui\debug_net8.0-android\chat.actual.app-Signed.apk
