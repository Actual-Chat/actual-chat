:<<BATCH
    @echo off

    set adb=C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe
    set avdmanager=C:\Program Files (x86)\Android\android-sdk\cmdline-tools\5.0\bin\avdmanager.bat
    set emulator=C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe

    set avdName=pixel_5_-_api_31

    echo :: stopping avd
    Taskkill /IM qemu-system-x86_64.exe /F /T
    "%adb%" emu kill

    echo :: creating avd
    echo no | "%avdmanager%" create avd -f -n %avdName% -d pixel_5 -k "system-images;android-31;google_apis;x86_64"

    start "" "%emulator%" -avd %avdName% -writable-system -no-snapshot-load

    echo :: waiting for device to root
    :repeat1
        timeout 3
        adb -e get-state 1>NUL 2>NUL || goto :repeat1
    "%adb%" -e root

    echo :: waiting for device to remount
    :repeat2
        timeout 3
        adb -e get-state 1>NUL 2>NUL || goto :repeat2
    "%adb%" -e remount

    echo :: waiting for device to patch hosts
    :repeat3
        timeout 3
        adb -e get-state 1>NUL 2>NUL || goto :repeat3
    "%adb%" -e pull /system/etc/hosts artifacts/hosts
    echo 10.0.2.2 local.actual.chat media.local.actual.chat cdn.local.actual.chat >> artifacts/hosts
    "%adb%" -e push artifacts/hosts /system/etc/

    exit /b
BATCH

#!/bin/sh
echo not implemented yet!
