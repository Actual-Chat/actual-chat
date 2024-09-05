dotnet publish src/dotnet/App.Maui -c Debug -f:net8.0-windows10.0.22000.0 -p:PublishReadyToRun=true -p:WindowsPackageType=None
start artifacts\publish\App.Maui\debug_net8.0-windows10.0.22000.0\ActualChat.exe
