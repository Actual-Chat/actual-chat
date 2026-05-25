dotnet build src/dotnet/App.Maui -c Debug -f:net10.0-windows10.0.22621.0 -p:WindowsPackageType=None %*
start artifacts\bin\App.Maui\debug_net10.0-windows10.0.22621.0\ActualChat.exe
