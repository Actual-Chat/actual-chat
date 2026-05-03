// Core RPC module — shared service contracts and typed clients that sit below
// the streaming layer. Currently exposes `ISystemProperties`, used as a cheap
// "is the server still talking to us?" probe after connect / reconnect.
//
// Usage:
//     Api.init('Example', { url, modules: [coreApi] });
//     const info = await coreApi.systemProperties.GetServerApiInfo('');
//
// Naming: every wire DTO interface in this file ends in `Dto` (see api.ts
// for the rationale). New DTOs MUST follow the same convention.

import { defineRpcService, type RpcHub } from 'actuallab-rpc';
import { Api, type ApiModule } from './api.js';

// --- ISystemProperties (server info + maintenance) ---
// `GetServerApiInfoNC` is the non-compute ("NC") variant — plain method call,
// no ComputeMethod wrapping on the server. Use this for probing; the compute
// variant requires callTypeId=Reliable, which the TS client doesn't emit.
export const SystemPropertiesDef = defineRpcService('ISystemProperties', {
    GetServerApiInfoNC: { args: ['expectedVersion'] },
});

/** Matches .NET ServerApiInfo: [MessagePackObject(true)] → PascalCase keys. */
export interface ServerApiInfoDto {
    CompatibilityLevel: number;
    VersionString: string;
    FullVersionString: string;
    DisplayVersionString: string;
}

/** Typed proxy for ISystemProperties client calls. */
export interface SystemPropertiesClient {
    GetServerApiInfoNC(expectedVersion: string): Promise<ServerApiInfoDto>;
}

/** Core module — pass `coreApi` to `Api.init` to enable shared services.
 *  Singletons below; class is intentionally not exported. */
class CoreApi implements ApiModule {
    register(hub: RpcHub): void {
        hub.registry.registerService(SystemPropertiesDef.name, SystemPropertiesDef.methods);
    }

    private _systemProperties: SystemPropertiesClient | undefined;
    /** Typed `ISystemProperties` client bound to the shared default peer. */
    get systemProperties(): SystemPropertiesClient {
        return this._systemProperties
            ??= Api.hub.addClient<SystemPropertiesClient>(Api.peer, SystemPropertiesDef);
    }
}

export const coreApi = new CoreApi();
