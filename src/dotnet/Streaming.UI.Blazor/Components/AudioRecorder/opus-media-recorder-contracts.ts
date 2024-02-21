import { RpcNoWait } from 'rpc';

export interface RecorderStateEventHandler {
    onConnectionStateChanged(isConnected: boolean, noWait?: RpcNoWait): Promise<void>;
    onRecordingStateChanged(isRecording: boolean, noWait?: RpcNoWait): Promise<void>;
    onVoiceStateChanged(isVoiceActive: boolean, noWait?: RpcNoWait): Promise<void>;
    onAudioPowerChange(power: number, noWait?: RpcNoWait): Promise<void>;

    recordingInProgress(noWait?: RpcNoWait): Promise<void>;
}

export interface RecorderStateChanged {
    (isRecording: boolean, isConnected: boolean, isVoiceActive: boolean): Promise<void>;
}

