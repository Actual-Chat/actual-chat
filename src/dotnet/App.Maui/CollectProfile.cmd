set PATH=%PATH%;"C:\.ng\mono.aotprofiler.android\7.0.0\tools"
adb shell setprop debug.mono.profile aot:port=9999
adb forward tcp:9999 tcp:9999
aprofutil -s -v -p 9999 -o "_Profiling/android.aprof"
