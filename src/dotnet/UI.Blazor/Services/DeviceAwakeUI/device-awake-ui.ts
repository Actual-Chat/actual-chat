import { Log } from 'logging';
import { OnDeviceAwake } from 'on-device-awake';

const { debugLog, errorLog } = Log.get('DeviceAwakeUI');

export class DeviceAwakeUI {
    private static backendRef: DotNet.DotNetObject;
    public static init(backendRef: DotNet.DotNetObject) {
        this.backendRef = backendRef;
        OnDeviceAwake.events.add(() => this.onDeviceAwake());
    }

    private static async onDeviceAwake() {
        debugLog?.log('onDeviceAwake');
        try {
            await this.backendRef.invokeMethodAsync('OnDeviceAwake');
        } catch (e) {
            errorLog?.log('onDeviceAwake: failed to notify backend', e)
        }
    }
}
