import { RpcNoWait } from 'rpc';

export interface RecorderStateServer {
    getSessionToken(minLifespanMs?: number): Promise<string>;
    onConnectionStateChanged(isConnected: boolean, noWait?: RpcNoWait): Promise<void>;
    onVoiceStateChanged(isVoiceActive: boolean, noWait?: RpcNoWait): Promise<void>;
    onAudioPowerChange(power: number, noWait?: RpcNoWait): Promise<void>;

    microphoneIsCaptured(noWait?: RpcNoWait): Promise<void>;
    onRecorderShutdown(reason: string, noWait?: RpcNoWait): Promise<void>;
}

export type RecorderStateChanged = (isRecording: boolean, isSignalDetected: boolean, isConnected: boolean, isVoiceActive: boolean) => Promise<void>;
export interface RecorderState {
    isRecording: boolean;
    isSignalDetected: boolean;
    isConnected: boolean;
    isVoiceActive: boolean;
}
