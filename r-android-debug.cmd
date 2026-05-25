pushd src\dotnet\App.Maui
dotnet publish -f:net10.0-android -c:Debug -p:EmbedAssembliesIntoApk=true %*
popd

adb install -r artifacts\publish\App.Maui\debug_net10.0-android\chat.actual.dev.app-Signed.apk
