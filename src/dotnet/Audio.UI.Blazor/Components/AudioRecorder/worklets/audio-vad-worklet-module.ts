import { VadAudioWorkletProcessor } from "./audio-vad-worklet-processor";
registerProcessor('audio-vad-worklet-processor', VadAudioWorkletProcessor as unknown as AudioWorkletProcessorConstructor);
