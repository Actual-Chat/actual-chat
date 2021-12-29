import WebMOpusEncoder from 'opus-media-recorder/WebMOpusEncoder';
import {
    EncoderCommand,
    EncoderMessage,
    InitCommand,
    LoadEncoderCommand,
    PushInputDataCommand
} from "../opus-media-recorder-message";

interface Encoder {
    init(inputSampleRate: number, channelCount: number, bitsPerSecond: number): void;
    encode(channelBuffers: Float32Array[]): void;
    flush(): ArrayBuffer[];
    close(): void;
}
let encoder: Encoder;
let self: Worker;

self.onmessage = (ev: MessageEvent) => {
  const { command } : EncoderCommand = ev.data;
  switch (command) {
    case 'loadEncoder':
      const { mimeType, wasmPath }: LoadEncoderCommand = ev.data;
      // Setting encoder module
      const mime = mimeType.toLowerCase();
      let encoderModule;
      if (mime.indexOf("audio/webm") >= 0) {
        encoderModule = WebMOpusEncoder;
      }
      // Override Emscripten configuration
      let moduleOverrides = {};
      if (wasmPath) {
        moduleOverrides['locateFile'] = function (path, scriptDirectory) {
          return path.match(/.wasm/) ? wasmPath : (scriptDirectory + path);
        };
      }
      // Initialize the module
      encoderModule(moduleOverrides).then(Module => {
        encoder = Module;
        // Notify the host ready to accept 'init' message.
        self.postMessage({ command: 'readyToInit' });
      });
      break;

    case 'init':
      const { sampleRate, channelCount, bitsPerSecond }: InitCommand = ev.data;
      encoder.init(sampleRate, channelCount, bitsPerSecond);
      break;

    case 'pushInputData':
      const { channelBuffers } : PushInputDataCommand = ev.data; // eslint-disable-line
      encoder.encode(channelBuffers);
      break;

    case 'getEncodedData':
    case 'done':
      if (command === 'done') {
        encoder.close();
      }

      const buffers = encoder.flush();
      const message: EncoderMessage = {
          command: command === 'done' ? 'lastEncodedData' : 'encodedData',
          buffers
      };
      self.postMessage(message, buffers);

      break;

    default:
      // Ignore
      break;
  }
};
