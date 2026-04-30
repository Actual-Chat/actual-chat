// Public surface of the Api layer.
//
// Use:
//     import { Api, streamingApi, streamServer } from 'api';
//     Api.init('Example', { url: 'wss://host/rpc/ws', modules: [streamingApi] });
//     await streamServer().RequestKeyFrame(streamId);

export { Api, WorkerKind } from './api.js';
export type { ApiConnectivityUI, ApiInitOptions, ApiModule, SessionTokenProvider } from './api.js';

export * from './rpc-scalars.js';
export * from './core-api.js';
export * from './streaming-api.js';
export * from './uploads-api.js';
