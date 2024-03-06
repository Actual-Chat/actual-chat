rem See https://github.com/steveharter/dotnet_coreclr/blob/master/Documentation/project-docs/clr-configuration-knobs.md#appdomain-configuration-knobs
set DOTNET_TieredPGO=1
set DOTNET_TC_QuickJitForLoops=1
rem Don't promote to the next tier while the call count is < N
set DOTNET_TC_CallCountThreshold=10000
rem Don't promote to the next tier unless N ms passed from the startup
set DOTNET_TC_CallCountingDelayMs=10000
set DOTNET_ReadyToRun=0
dotnet trace collect -o "_Profiling/windows.nettrace" --providers Microsoft-Windows-DotNETRuntime:0x1F000080018:5 -- ..\..\..\artifacts\publish\App.Maui\debug_net8.0-windows10.0.22000.0\ActualChat.exe
