#!/bin/bash

# Detect connected iOS device, preferring USB-connected devices.
# Returns: DeviceId (UDID)
# Exits with error if no device found or user cancels selection.

# Get device name from xctrace by UDID
_get_device_name() {
    local udid="$1"
    local line
    line=$(xcrun xctrace list devices 2>/dev/null | grep "$udid")
    if [ -n "$line" ]; then
        echo "$line" | sed 's/ ([^)]*) ([^)]*)$//'
    else
        echo "Unknown device"
    fi
}

detect_ios_device() {
    # Collect all physical devices from xctrace (excluding Mac and simulators).
    # Includes offline devices since wirelessly paired iPhones often appear there
    # but are still reachable via `devicectl`.
    local devices_section
    devices_section=$(xcrun xctrace list devices 2>/dev/null | sed -n '/^== Devices ==$/,/^== Simulators ==$/p' | grep -E '\([0-9]+\.[0-9]+')

    if [ -z "$devices_section" ]; then
        echo "Error: No available iOS device found. Please connect an iPhone and ensure it's paired." >&2
        exit 1
    fi

    # Filter out Mac entries (they have UUIDs with dashes, not device UDIDs starting with 0000)
    local phone_devices
    phone_devices=$(echo "$devices_section" | grep -E '\(0000[0-9A-Fa-f]+-' )

    if [ -z "$phone_devices" ]; then
        echo "Error: No iOS device (iPhone/iPad) found among connected devices." >&2
        echo "" >&2
        echo "Available devices:" >&2
        echo "$devices_section" >&2
        exit 1
    fi

    # Try to detect USB-connected devices via idevice_id
    local usb_udids=()
    if command -v idevice_id &>/dev/null; then
        while IFS= read -r udid; do
            [ -n "$udid" ] && usb_udids+=("$udid")
        done < <(idevice_id -l 2>/dev/null)
    fi

    if [ ${#usb_udids[@]} -eq 1 ]; then
        # Exactly one USB device — use it
        local deviceId="${usb_udids[0]}"
        local deviceName
        deviceName=$(_get_device_name "$deviceId")
        echo "Using USB device: $deviceName ($deviceId)" >&2
        echo "$deviceId"
        return
    fi

    if [ ${#usb_udids[@]} -gt 1 ]; then
        # Multiple USB devices — ask user to choose among USB devices
        echo "Multiple USB-connected iOS devices found:" >&2
        local i=1
        local usb_entries=()
        for udid in "${usb_udids[@]}"; do
            local name
            name=$(_get_device_name "$udid")
            echo "  $i) $name ($udid)" >&2
            usb_entries+=("$udid")
            ((i++))
        done
        echo -n "Select device [1-${#usb_entries[@]}]: " >&2
        read -r choice
        if [[ "$choice" =~ ^[0-9]+$ ]] && [ "$choice" -ge 1 ] && [ "$choice" -le ${#usb_entries[@]} ]; then
            local deviceId="${usb_entries[$((choice-1))]}"
            local deviceName
            deviceName=$(_get_device_name "$deviceId")
            echo "Using device: $deviceName ($deviceId)" >&2
            echo "$deviceId"
            return
        else
            echo "Error: Invalid selection." >&2
            exit 1
        fi
    fi

    # idevice_id not available or returned no USB devices — fall back to xctrace list
    local all_udids=()
    local all_names=()
    while IFS= read -r line; do
        local udid
        udid=$(echo "$line" | sed 's/.*(\([^)]*\))$/\1/')
        local name
        name=$(echo "$line" | sed 's/ ([^)]*) ([^)]*)$//')
        all_udids+=("$udid")
        all_names+=("$name")
    done <<< "$phone_devices"

    if [ ${#all_udids[@]} -eq 1 ]; then
        echo "Using device: ${all_names[0]} (${all_udids[0]})" >&2
        echo "${all_udids[0]}"
        return
    fi

    # Multiple devices, no USB info — ask user to choose
    echo "Multiple iOS devices found (install 'libimobiledevice' to auto-detect USB):" >&2
    local i=1
    for idx in "${!all_udids[@]}"; do
        echo "  $((idx+1))) ${all_names[$idx]} (${all_udids[$idx]})" >&2
        ((i++))
    done
    echo -n "Select device [1-${#all_udids[@]}]: " >&2
    read -r choice
    if [[ "$choice" =~ ^[0-9]+$ ]] && [ "$choice" -ge 1 ] && [ "$choice" -le ${#all_udids[@]} ]; then
        local deviceId="${all_udids[$((choice-1))]}"
        echo "Using device: ${all_names[$((choice-1))]} ($deviceId)" >&2
        echo "$deviceId"
        return
    else
        echo "Error: Invalid selection." >&2
        exit 1
    fi
}
