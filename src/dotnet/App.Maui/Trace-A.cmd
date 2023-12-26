adb reverse tcp:9000 tcp:9000
start dotnet-dsrouter client-server -tcps 127.0.0.1:9000 -ipcc /tmp/maui-app --verbose debug
start dotnet-trace collect --diagnostic-port /tmp/maui-app --output "_Profiling/android.nettrace" --providers Microsoft-Windows-DotNETRuntime:0x1F000080018:5
