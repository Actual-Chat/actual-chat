dotnet publish src/dotnet/App.Maui -c Release -f:net9.0-windows10.0.22000.0 -p:UseAppPack=true -p:WindowsPackageType=None
start artifacts\publish\App.Maui\release_net9.0-windows10.0.22000.0\ActualChat.exe 
