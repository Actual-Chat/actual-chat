import { MonoConfig } from 'dotnet';

// The code below is a slightly changed version of:
// - https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Services/WebRootComponentManager.ts#L466
// - Look for "areWebAssemblyResourcesLikelyCached" function there.

export function areWasmResourcesLikelyCached(): boolean {
    // @ts-expect-error - window.Blazor is defined in the root html
    // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
    const config = window.Blazor?.runtime?.config as MonoConfig | null;
    if (!config?.cacheBootResources)
        return false;

    const hashInfo = getWasmResourceHashInfo(config);
    if (!hashInfo)
        return false;

    const existingHash = window.localStorage.getItem(hashInfo.key);
    return hashInfo.value === existingHash;
}

function getWasmResourceHashInfo(config: MonoConfig): { key: string, value: string } | null {
    const hash = config.resources?.hash;
    const mainAssemblyName = config.mainAssemblyName;
    if (!hash || !mainAssemblyName)
        return null;

    return {
        key: `blazor-resource-hash:${mainAssemblyName}`,
        value: hash,
    };
}
