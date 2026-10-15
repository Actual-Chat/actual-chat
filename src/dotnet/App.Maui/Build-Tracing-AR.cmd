dotnet publish -f:net11.0-android -r android-arm64 -p:IsTracingEnabled=true -p:EmbedAssembliesIntoApk=true -c:Release
adb install -r ..\..\..\artifacts\publish\App.Maui\release_net11.0-android_android-arm64\chat.actual.dev.app-Signed.apk
