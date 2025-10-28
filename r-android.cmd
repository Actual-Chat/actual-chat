pushd src\dotnet\App.Maui
rem dotnet publish -f:net9.0-android -c:Release /p:AndroidSigningKeyPass=%ActualChat_KeyPass% /p:AndroidSigningStorePass=%ActualChat_StorePass% /p:IsDevMaui=false /p:UseAppPack=true
dotnet publish -f:net9.0-android -c:Release /p:AndroidSigningKeyPass=%ActualChat_AndroidSigningKeyPass% /p:AndroidSigningStorePass=%ActualChat_AndroidSigningStorePass% /p:IsDevMaui=true
popd

adb install -r artifacts\publish\App.Maui\release_net9.0-android\chat.actual.dev.app-Signed.apk
