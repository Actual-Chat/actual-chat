adb shell setprop debug.mono.profile aot:port=9999
adb forward tcp:9999 tcp:9999
adb shell am start -S -n "chat.actual.dev.app/crc6444c698770736d3d5.MainActivity"
