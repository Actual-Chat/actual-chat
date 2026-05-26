import { Observable, Subject } from 'rxjs';
import { debounce } from 'actuallab-core';
import { RecorderState } from './opus-media-recorder-contracts';
import { getLogs } from 'logging';

const { debugLog } = getLogs('AudioRecorder');

const RecordingFailedInterval = 500;

// Web-only relay: forwards JS-side recorder state to Blazor.
// Pushed into by opus-media-recorder.ts (the JS side is the source of truth on web);
// subscribed by audio-recorder.ts which calls IAudioRecorderBackend.OnRecordingStateChange.
// NOT used on MAUI (audio-recorder.ts is never instantiated; RecordingActivityClient targets RecordingActivity instead).
export class AudioRecorderState {
    private static readonly stateChangedSubject: Subject<RecorderState> = new Subject<RecorderState>();
    private static readonly recordingHeartbeatSubject: Subject<void> = new Subject<void>();

    private static isRecording = false;
    private static isSignalDetected = false;
    private static isConnected = false;
    private static isVoiceActive = false;
    private static lastState: RecorderState = { isRecording: false, isSignalDetected: false, isConnected: false, isVoiceActive: false };

    public static get stateChanged$(): Observable<RecorderState> {
        return AudioRecorderState.stateChangedSubject.asObservable();
    }

    public static get recordingHeartbeat$(): Observable<void> {
        return AudioRecorderState.recordingHeartbeatSubject.asObservable();
    }

    public static getState(): RecorderState {
        return {
            isRecording: AudioRecorderState.isRecording,
            isSignalDetected: AudioRecorderState.isSignalDetected,
            isConnected: AudioRecorderState.isConnected,
            isVoiceActive: AudioRecorderState.isVoiceActive,
        };
    }

    public static setRecording(isRecording: boolean): void {
        if (AudioRecorderState.isRecording === isRecording)
            return;
        AudioRecorderState.isRecording = isRecording;
        AudioRecorderState.emitStateChanged();
    }

    public static setSignalDetected(isSignalDetected: boolean): void {
        if (AudioRecorderState.isSignalDetected === isSignalDetected)
            return;
        AudioRecorderState.isSignalDetected = isSignalDetected;
        AudioRecorderState.emitStateChanged();
    }

    public static setConnected(isConnected: boolean): void {
        if (AudioRecorderState.isConnected === isConnected)
            return;
        AudioRecorderState.isConnected = isConnected;
        AudioRecorderState.emitStateChanged();
    }

    public static setVoiceActive(isVoiceActive: boolean): void {
        if (AudioRecorderState.isVoiceActive === isVoiceActive)
            return;
        AudioRecorderState.isVoiceActive = isVoiceActive;
        AudioRecorderState.emitStateChanged();
    }

    public static microphoneIsCaptured(): void {
        AudioRecorderState.recordingHeartbeatSubject.next();
        if (!AudioRecorderState.isSignalDetected)
            AudioRecorderState.setSignalDetected(true);
        AudioRecorderState.signalLostDebounced();
    }

    private static readonly signalLostDebounced = debounce(
        () => AudioRecorderState.setSignalDetected(false),
        RecordingFailedInterval);

    private static emitStateChanged(): void {
        const state = AudioRecorderState.getState();
        debugLog?.log(`AudioRecorderState.stateChanged(): ${JSON.stringify(state)}`);
        const last = AudioRecorderState.lastState;
        if (state.isRecording === last.isRecording
            && state.isSignalDetected === last.isSignalDetected
            && state.isConnected === last.isConnected
            && state.isVoiceActive === last.isVoiceActive)
            return;
        AudioRecorderState.lastState = state;
        AudioRecorderState.stateChangedSubject.next(state);
    }
}
