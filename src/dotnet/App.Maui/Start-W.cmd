rem Starts the unpackaged Windows build produced by:
rem   dotnet publish -f:net11.0-windows10.0.22621.0 -c:Release -p:WindowsPackageType=None
rem Tiering settings travel with the app now (RuntimeHostConfigurationOption in the csproj),
rem so this launches it exactly as a shipped build runs. Set DOTNET_TC_CallCountingDelayMs
rem before calling to override for an experiment - the env var wins over runtimeconfig.
"%~dp0..\..\..\artifacts\publish\App.Maui\release_net11.0-windows10.0.22621.0_win-x64\ActualChat.exe" %*
