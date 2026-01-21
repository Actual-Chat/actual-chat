import { DelayedInvoker } from '../../../UI.Blazor/Components/delayed-invoker';

export async function requestFileHandlePermission(handle: FileSystemFileHandle, mode : FileSystemPermissionMode = "read") : Promise<GetFilePermissionsRequest> {
    const options: FileSystemHandlePermissionDescriptor = {
        mode: mode
    };
    if ((await handle.queryPermission(options)) === "granted") {
        return {
            granted: Promise.resolve(true),
            cancel: () => {}
        };
    }
    let requestResult : PermissionState = 'denied';
    const requestCallback = async () => {
        console.log("requestPermission");
        requestResult = await handle.requestPermission(options);
        console.log("requestPermission=" + requestResult);
    };
    const grantedPromise = (async () => {
        await grantFileUploadPermissionsInvoker.registerCallback(requestCallback);
        // @ts-ignore
        return requestResult === 'granted';
    })();
    return {
        granted: grantedPromise,
        cancel: () => {
            grantFileUploadPermissionsInvoker.unregisterCallback(requestCallback);
        }
    };
}

export type GetFilePermissionsRequest = { granted : Promise<boolean>; cancel : () => void; };

export const grantFileUploadPermissionsInvoker = new DelayedInvoker();
