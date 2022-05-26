import { onDeviceAwake } from 'on-device-awake';

const reloadOnDeviceAwake = () => {
    onDeviceAwake(() => location.reload());
};
export {reloadOnDeviceAwake};
