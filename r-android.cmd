rmdir /S /Q artifacts
dotnet restore ActualChat.Maui.sln

pushd src/dotnet/App.Maui
dotnet publish -f:net8.0-android -c:Release /p:AndroidSigningKeyPass=%ActualChat_KeyPass% /p:AndroidSigningStorePass=%ActualChat_StorePass% /p:IsDevMaui=false
popd

adb install -r artifacts\publish\App.Maui\release_net8.0-android\chat.actual.app-Signed.apk
