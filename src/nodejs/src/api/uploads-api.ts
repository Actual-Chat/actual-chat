// Uploads RPC module — service contract (IUploads), command DTO,
// the `UploadsApi` module class, and a typed `uploadsApi.uploads` accessor
// on its singleton instance.
//
// Usage:
//     Api.init('Example', { url, modules: [uploadsApi] });
//     const offset = await uploadsApi.uploads.GetOffset('~', uploadId);
//     await uploadsApi.uploads.OnAppend({ Session: '~', UploadId, Offset, Chunk });
//
// Naming: wire types use the bare C# record name; the `Dto` suffix is added
// only for disambiguation against browser globals or in-scope name clashes.
// See api.ts for the rationale.

import { defineRpcService, RpcRemoteExecutionMode, type RpcHub } from 'actuallab-rpc';
import { Api, type ApiModule } from './api.js';
import type { Int64 } from './rpc-scalars.js';

// Stream-push semantics for AppendStream: wait for the WS before the initial
// send, skip the call on same-peer reconnect (stream resumes via ACK). No
// AllowResend — on peer change the call + stream fail and the caller retries
// from the server offset. Matches the audio/video PushStream mode.
const StreamUploadMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect;

// --- IUploads (chunked upload control + chunk push) ---
// Both methods use RpcRemoteExecutionMode.Default (= AwaitForConnection
// | AllowReconnect | AllowResend = 7), matching the C# defaults:
//   - GetOffset has no [RpcMethod] attribute (Default mode).
//   - OnAppend has [RpcMethod(ConnectTimeout = +∞)] which keeps Default mode.
// Both calls are idempotent — GetOffset is a pure read, and OnAppend's server
// handler returns OffsetConflictException on offset mismatch which the client
// handles via retry. ConnectTimeout has no TS equivalent; the JS peer parks
// on Api.canConnect until the connection is allowed.
export const UploadsDef = defineRpcService('IUploads', {
    GetOffset: { args: ['session', 'uploadId'] },
    OnAppend: { args: ['command'] },
    AppendStream: {
        args: ['session', 'uploadId', 'offset', 'dataStream'],
        remoteExecutionMode: StreamUploadMode,
    },
});

/** Matches .NET Uploads_Append: AppMessagePackKeylessResolver serializes
 *  members by name (PascalCase) regardless of [Key(N)]. */
export interface UploadsAppendCommand {
    Session: string;
    UploadId: string;
    Offset: Int64;
    Chunk: Uint8Array;
}

/** Typed proxy for IUploads client calls. */
export interface UploadsClient {
    GetOffset(session: string, uploadId: string): Promise<Int64>;
    OnAppend(command: UploadsAppendCommand): Promise<Int64>;
    /** Streams upload data as RpcStream<byte[]> sub-chunks. `dataStreamRef` is
     *  a local RpcStream's `toRef(peer)` value (see audio/video PushStream). */
    AppendStream(session: string, uploadId: string, offset: Int64, dataStreamRef: unknown): Promise<Int64>;
}

/** Uploads module — pass `uploadsApi` to `Api.init` to enable the typed client.
 *  Singleton; class is intentionally not exported. */
class UploadsApi implements ApiModule {
    register(hub: RpcHub): void {
        hub.registry.registerService(UploadsDef.name, UploadsDef.methods);
    }

    private _uploads: UploadsClient | undefined;
    /** Typed `IUploads` client bound to the shared default peer. */
    get uploads(): UploadsClient {
        return this._uploads
            ??= Api.hub.addClient<UploadsClient>(Api.peer, UploadsDef);
    }
}

export const uploadsApi = new UploadsApi();
