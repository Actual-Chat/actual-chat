export class VideoInputTestPage {
    public static async getVideoInputDevices(): Promise<MediaDeviceInfo[]> {
        const devices = await navigator.mediaDevices.enumerateDevices();
        return devices.filter(d => d.kind === 'videoinput');
    }

    public static async getTrackCapabilities(deviceId: string): Promise<Record<string, unknown> | null> {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({
                video: { deviceId: { exact: deviceId } },
            });
            const track = stream.getVideoTracks()[0];
            const capabilities = track.getCapabilities();
            track.stop();
            return { ...capabilities };
        }
        catch {
            return null;
        }
    }

    public static async getDeviceCapabilities(deviceId: string): Promise<Record<string, unknown> | null> {
        const devices = await navigator.mediaDevices.enumerateDevices();
        const device = devices.find(d => d.deviceId === deviceId) as InputDeviceInfo | undefined;
        if (device?.getCapabilities)
            return { ...device.getCapabilities() };
        return null;
    }

    public static async requestVideoPermission(): Promise<void> {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true });
        stream.getTracks().forEach(t => t.stop());
    }
}
