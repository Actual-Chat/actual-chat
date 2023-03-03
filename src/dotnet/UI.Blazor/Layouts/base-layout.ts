import { OnDeviceAwake } from 'on-device-awake';

export function reloadOnDeviceAwake(): void {
    OnDeviceAwake.events.add(() => location.reload());
}
