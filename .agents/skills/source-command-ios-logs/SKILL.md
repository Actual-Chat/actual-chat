---
name: "source-command-ios-logs"
description: "Stream and search iOS device logs. Use proactively when debugging iOS issues or when user asks to check device logs."
---

# source-command-ios-logs

Use this skill when the user asks to run the migrated source command `ios-logs`.

## Command Template

# iOS Logs

Stream and search logs from a connected iOS device using `idevicesyslog`.

## Prerequisites

- `libimobiledevice` installed (`brew install libimobiledevice`)
- Physical iOS device connected via USB (not network-only)
- Device must be paired/trusted

## Find Device UDID

First, list available devices:

```bash
idevice_id -l
```

This returns UDIDs of devices visible to libimobiledevice. Note: Newer devices using CoreDevice protocol may not appear here.

## Stream All Logs

Stream all logs from a device (replace UDID):

```bash
/opt/homebrew/bin/idevicesyslog -u <UDID>
```

## Stream Filtered Logs

Filter by process name (e.g., ActualChat):

```bash
/opt/homebrew/bin/idevicesyslog -u <UDID> -p ActualChat
```

Filter by message content:

```bash
/opt/homebrew/bin/idevicesyslog -u <UDID> -m "error\|warning\|VoIP"
```

Combine process and message filters:

```bash
/opt/homebrew/bin/idevicesyslog -u <UDID> -p ActualChat -m "INF\|error"
```

## Capture for Duration

Capture logs for a specific duration (e.g., 10 seconds):

```bash
/opt/homebrew/bin/idevicesyslog -u <UDID> -p ActualChat &
PID=$!
sleep 10
kill $PID 2>/dev/null
wait $PID 2>/dev/null
```

## Common Filters for ActualChat

Search for VoIP/PushKit logs:
```bash
-m "voip\|VoIP\|PushKit\|DidUpdate"
```

Search for application INFO logs:
```bash
-m "INF \[ActualChat"
```

Search for errors:
```bash
-m "error\|Error\|ERR\|Exception"
```

## Log Format

ActualChat app logs appear as:
```
Mar 16 18:15:46.425 ActualChat[2869:195195] INF [Namespace.ClassName] Message
```

- Timestamp
- Process name and PID
- Log level (INF, DBG, WRN, ERR)
- Logger class name in brackets
- Message

## Troubleshooting

If no logs appear:
- Verify device UDID with `idevice_id -l`
- Try without `-p` filter to see all logs
- Check device is connected via USB, not just WiFi
- Newer iOS devices (iOS 17+) may require Xcode Console instead
