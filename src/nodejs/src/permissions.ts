export async function tryQueryPermissionState(name: string): Promise<PermissionState | null> {
    try {
        if (!('permissions' in navigator))
            return null;

        const status = await navigator.permissions.query({ name: name as PermissionName });
        return status.state;
    }
    catch (error) {
        return null;
    }
}
