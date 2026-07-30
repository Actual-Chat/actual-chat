pushd src\dotnet\App.Maui
dotnet publish -f:net11.0-android -c:Release -p:EmbedAssembliesIntoApk=true -p:UseMemoryPack=false -p:UseNativeAot=true %*
popd

adb install -r artifacts\publish\App.Maui\release_net11.0-android\chat.actual.dev.app-Signed.apk
