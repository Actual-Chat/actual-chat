 // Commented out because it causes ts compilation issues in webpack release mode
// /// <reference lib="WebWorker" />
// export type { };
// declare const self: WorkerGlobalScope;

/// #if MEM_LEAK_DETECTION
import codec, { Codec, Decoder } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Decoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif

import { OpusDecoder } from './opus-decoder';
import { OpusDecoderWorker } from './opus-decoder-worker-contract';
import { rpcNoWait, RpcNoWait, rpcServer, RpcTimeout } from 'rpc';
import * as signalR from '@microsoft/signalr';
import { HubConnectionState, IStreamResult } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { delayAsync, retry } from 'promises';
import { Versioning } from 'versioning';
import { Log } from 'logging';
import { SAMPLE_RATE } from '../constants';

const { logScope, debugLog, infoLog, errorLog } = Log.get('OpusDecoderWorker');


// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;
let useSystemDecoder = false;
let hubConnection: signalR.HubConnection = null;

const worker = self as unknown as Worker;
const decoders = new Map<string, OpusDecoder>();
const systemCodecConfig: AudioEncoderConfig = {
    codec: 'opus',
    numberOfChannels: 1,
    sampleRate: SAMPLE_RATE,
};

const serverImpl: OpusDecoderWorker = {
    create: async (artifactVersions: Map<string, string>, hubUrl: string, _timeout?: RpcTimeout): Promise<void> => {
        debugLog?.log(`-> init`);
        if (codecModule){
            if (hubConnection.state !== HubConnectionState.Connected)
                await serverImpl.reconnect();

            return;
        }

        if (!hubConnection) {
            Versioning.init(artifactVersions);

            // Connect to SignalR Hub
            hubConnection = new signalR.HubConnectionBuilder()
                .withUrl(hubUrl, {
                    skipNegotiation: true,
                    transport: signalR.HttpTransportType.WebSockets,
                })
                // We use fixed number of attempts here, because the reconnection is anyway
                // triggered after SSB / Stl.Rpc reconnect. See:
                // - C#: ChatAudioUI.ReconnectOnRpcReconnect
                // - TS: AudioRecorder.ctor.
                // Some extra attempts are needed, coz there is a chance that the primary connection
                // stays intact, while this one drops somehow.
                .withAutomaticReconnect([50, 350, 500, 1000, 1000, 1000])
                .withHubProtocol(new MessagePackHubProtocol())
                .configureLogging(signalR.LogLevel.Information)
                .build();
            // stateful reconnect doesn't work with skipNegotiation and moreover provides glitches
            hubConnection.onreconnected(() => onReconnect());
            // hubConnection.onreconnecting(() => void server.onConnectionStateChanged(
            //     hubConnection.state === HubConnectionState.Connected,
            //     rpcNoWait));
            // hubConnection['_launchStreams'] = _launchStreams.bind(hubConnection);
            debugLog?.log(`create: hub created`);
        }

        if (!useSystemDecoder && globalThis.AudioDecoder) {
            const configSupport = await AudioDecoder.isConfigSupported(systemCodecConfig);
            useSystemDecoder = configSupport.supported;
        }

        if (!useSystemDecoder) {
            // Load & warm-up codec
            codecModule = await retry(3, () => codec(getEmscriptenLoaderOptions()));
            const decoder = new codecModule.Decoder(SAMPLE_RATE);
            decoder.delete();
        }

        debugLog?.log(`<- init`);
    },

    init: async (streamId: string, feederWorkletPort: MessagePort): Promise<void> => {
        debugLog?.log(`-> #${streamId}.create`);
        const decoder: Decoder | null = useSystemDecoder
            ? null
            : new codecModule.Decoder(SAMPLE_RATE);
        const opusDecoder = await OpusDecoder.create(streamId, decoder, feederWorkletPort);
        opusDecoder.init();
        decoders.set(streamId, opusDecoder);
        debugLog?.log(`<- #${streamId}.create`);
    },

    resume: async  (streamId: string, _noWait?: RpcNoWait): Promise<void> => {
        const opusDecoder = getDecoder(streamId, false);
        if (!opusDecoder) {
            errorLog?.log(`#${streamId}.resume() has failed - decoder does not exist`)
            return;
        }

        opusDecoder.init();
    },

    close: async (streamId: string, _noWait?: RpcNoWait): Promise<void> => {
        debugLog?.log(`#${streamId}.close`);
        const opusDecoder = getDecoder(streamId, false);
        if (!opusDecoder)
            return;

        try {
            await opusDecoder.end(true);
            await opusDecoder.disposeAsync();
        }
        catch (e) {
            errorLog?.log(`#${streamId}.close: error while closing the decoder:`, e);
        }
        finally {
            decoders.delete(streamId);
        }
    },

    end: async (streamId: string, mustAbort: boolean): Promise<void> => {
        debugLog?.log(`#${streamId}.end, mustAbort:`, mustAbort);
        await getDecoder(streamId).end(mustAbort);
    },

    frame: async (
        streamId: string,
        buffer: ArrayBuffer,
        offset: number,
        length: number,
        _noWait?: RpcNoWait,
    ): Promise<void> => {
        // debugLog?.log(`#${streamId}.onFrame`);
        getDecoder(streamId).decode(buffer, offset, length);
    },

    releaseBuffer: async(streamId: string, buffer: ArrayBuffer, _noWait?: RpcNoWait): Promise<void>  => {
        await getDecoder(streamId).releaseBuffer(buffer, _noWait);
    },

    reconnect: async (_noWait?: RpcNoWait): Promise<void> => {
        infoLog?.log(`reconnect: `, hubConnection.state);
        if (hubConnection.state === HubConnectionState.Connected)
            return;

        if (hubConnection.state == HubConnectionState.Connecting) {
            // Waiting 1s for connection to happen
            for (let i = 0; i < 10; i++) {
                await delayAsync(100);
                // @ts-ignore
                if (hubConnection.state === HubConnectionState.Connected)
                    return;
            }
        }

        // Reconnect
        if (hubConnection.state === HubConnectionState.Disconnected) {
            await hubConnection.start();
        }
        else {
            await hubConnection.stop();
            await hubConnection.start();
        }
        void onReconnect();
    },

    disconnect: async (_noWait?: RpcNoWait): Promise<void> => {
        infoLog?.log(`disconnect: `, hubConnection.state);
        await hubConnection.stop();
    },
};

const server = rpcServer(`${logScope}.server`, worker, serverImpl);

// Helpers

function getDecoder(streamId: string, failIfNone = true): OpusDecoder {
    const decoder = decoders.get(streamId);
    if (!decoder && failIfNone)
        throw new Error(`getDecoder: no decoder #${streamId}, did you forget to call 'create'?`);

    return decoder;
}

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(codecWasm);
            if (filename.slice(-4) === 'wasm')
                return codecWasmPath;
            /// #if MEM_LEAK_DETECTION
            else if (filename.slice(-3) === 'map')
                return codecWasmMap;
                /// #endif
                // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.slice(0, 5) === 'data:')
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}

 async function onReconnect(): Promise<void> {
     infoLog?.log(`onReconnect: `, hubConnection.state);
     const isConnected = hubConnection.state === HubConnectionState.Connected;
     // void server.onConnectionStateChanged(hubConnection.state === HubConnectionState.Connected, rpcNoWait);
     // if (isConnected && state === 'encoding') {
     //     await startRecording();
     // }
 }



