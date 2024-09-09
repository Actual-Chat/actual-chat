pushd src\dotnet\App.Maui
rem dotnet publish -f:net8.0-android -c:Release /p:AndroidSigningKeyPass=%ActualChat_KeyPass% /p:AndroidSigningStorePass=%ActualChat_StorePass% /p:IsDevMaui=false /p:UseAppPack=true
dotnet publish -f:net8.0-android -c:Release /p:AndroidSigningKeyPass=%ActualChat_KeyPass% /p:AndroidSigningStorePass=%ActualChat_StorePass% /p:IsDevMaui=false
popd

adb install -r artifacts\publish\App.Maui\release_net8.0-android\chat.actual.app-Signed.apk
