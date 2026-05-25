dotnet publish src/dotnet/App.Maui -c Release -f:net10.0-windows10.0.22621.0 -p:UseNativeAot=true %*
start artifacts\publish\App.Maui\release_net10.0-windows10.0.22621.0_win-x64\ActualChat.exe
