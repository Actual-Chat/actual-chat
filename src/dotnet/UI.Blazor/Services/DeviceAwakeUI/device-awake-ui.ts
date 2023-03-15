import { Log } from 'logging';
import { OnDeviceAwake } from 'on-device-awake';

const { errorLog } = Log.get('DeviceAwakeUI');

export class DeviceAwakeUI {
    private static backendRef: DotNet.DotNetObject;
    public static init(backendRef: DotNet.DotNetObject) {
        this.backendRef = backendRef;
        OnDeviceAwake.events.add(x => this.onDeviceAwake(x));
    }

    private static async onDeviceAwake(totalSleepDurationMs: number) {
        try {
            await this.backendRef.invokeMethodAsync('OnDeviceAwake', totalSleepDurationMs);
        } catch (e) {
            errorLog?.log('onDeviceAwake: failed to notify backend, error:', e)
        }
    }
}
