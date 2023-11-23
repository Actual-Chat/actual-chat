rmdir /S /Q D:\Projects\ActualChat\artifacts
dotnet restore
dotnet publish -f:net8.0-android -c:Release /p:AndroidSigningKeyPass=%ActualChat_KeyPass% /p:AndroidSigningStorePass=%ActualChat_StorePass% /p:IsDevMaui=false
adb install -r D:\Projects\ActualChat\artifacts\publish\App.Maui\release_net8.0-android\chat.actual.app-Signed.apk
