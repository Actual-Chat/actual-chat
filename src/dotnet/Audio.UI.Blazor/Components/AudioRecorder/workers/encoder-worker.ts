const WebMOpusEncoder = require('opus-media-recorder/WebMOpusEncoder.js');
let encoder;

self.onmessage = function (e) {
  const { command } = e.data;
  switch (command) {
    case 'loadEncoder':
      const { mimeType, wasmPath } = e.data;
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
      const { sampleRate, channelCount, bitsPerSecond } = e.data;
      encoder.init(sampleRate, channelCount, bitsPerSecond);
      break;

    case 'pushInputData':
      const { channelBuffers, length, duration } = e.data; // eslint-disable-line
      encoder.encode(channelBuffers);
      break;

    case 'getEncodedData':
    case 'done':
      if (command === 'done') {
        encoder.close();
      }

      const buffers = encoder.flush();
      self.postMessage({
        command: command === 'done' ? 'lastEncodedData' : 'encodedData',
        buffers
      }, buffers);

      if (command === 'done') {
        self.close();
      }
      break;

    default:
      // Ignore
      break;
  }
};
