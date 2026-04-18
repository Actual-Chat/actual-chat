// Public surface of the Api layer.
//
// Use:
//     import { Api, streamingApi, streamServer } from 'api';
//     Api.init('wss://host/rpc/ws', streamingApi);
//     await streamServer().RequestKeyFrame(streamId);

export { Api } from './api.js';
export type { ApiModule } from './api.js';

export * from './streaming-api.js';
