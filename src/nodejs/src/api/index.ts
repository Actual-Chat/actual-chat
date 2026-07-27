// Public surface of the Api layer.
//
// Use:
//     import { Api, streamingApi } from 'api';
//     Api.init('Example', { url: 'wss://host/rpc/ws', modules: [streamingApi] });
//     await streamingApi.liveVideoStreams.RequestKeyFrame(session, streamId);

export { Api, MediaRpcStreamOptions, WorkerKind } from './api.js';
export type { ApiConnectivityUI, ApiInitOptions, ApiModule, SessionTokenProvider } from './api.js';

export * from './rpc-scalars.js';
export * from './core-api.js';
export * from './streaming-api.js';
export * from './uploads-api.js';
