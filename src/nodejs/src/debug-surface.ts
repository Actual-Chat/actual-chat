interface DebugUIRoot {
    debugUI?: Record<string, unknown>;
}

const surfaces = new Map<string, Record<string, unknown>>();
let isInterceptorInstalled = false;

/**
 * Hangs `api` off `globalThis.debugUI[name]`, merging it with whatever another module
 * already registered under the same name.
 */
export function registerDebugSurface(name: string, api: Record<string, unknown>): void {
    const surface = surfaces.get(name);
    surfaces.set(name, surface ? { ...surface, ...api } : api);
    const root = globalThis as unknown as DebugUIRoot;
    if (root.debugUI) {
        applySurfaces(root.debugUI);
        return;
    }
    if (isInterceptorInstalled)
        return;

    // DebugUI.init() assigns globalThis.debugUI from C# once Blazor is ready;
    // intercepting the assignment beats racing it with a timer.
    isInterceptorInstalled = true;
    let current: Record<string, unknown> | undefined;
    Object.defineProperty(root, 'debugUI', {
        configurable: true,
        get: () => current,
        set: (value: Record<string, unknown>) => {
            current = value;
            applySurfaces(value);
        },
    });
}

// Private methods

function applySurfaces(debugUI: Record<string, unknown>): void {
    for (const [name, api] of surfaces)
        debugUI[name] = api;
}
