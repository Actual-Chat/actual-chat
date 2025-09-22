import { DelayedInvoker } from '../../../UI.Blazor/Components/delayed-invoker';

export async function requestFileHandlePermission(handle: FileSystemFileHandle, mode : FileSystemPermissionMode = "read") {
    const options: FileSystemHandlePermissionDescriptor = {
        mode: mode
    };
    if ((await handle.queryPermission(options)) === "granted") {
        return true;
    }
    let requestResult : PermissionState = 'denied';
    await grantFileUploadPermissionsInvoker.registerCallback(async() => {
        console.log("requestPermission");
        requestResult = await handle.requestPermission(options);
        console.log("requestPermission=" + requestResult);
    });
    // @ts-ignore
    return 'granted' === requestResult;
}

export const grantFileUploadPermissionsInvoker = new DelayedInvoker();
