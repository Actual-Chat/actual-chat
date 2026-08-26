import { Observable, Subject } from 'rxjs';
import { debounce } from 'actuallab-core';
import { RecorderState } from './opus-media-recorder-contracts';
import { getLogs } from 'logging';

const { debugLog } = getLogs('AudioRecorder');

const RecordingFailedInterval = 500;

export interface RecordingFailure {
    chatId: string;
    failure: string;
}

// getUserMedia's DOMException names are the only place the browser says what actually went wrong,
// and they're specific enough to give the user advice they can act on. The result is
// RecorderStartOutcome's wire form: "<RecorderStartResult>:<code>".
export function describeRecordingError(error: unknown): string {
    const name = error instanceof DOMException || error instanceof Error ? error.name : '';
    switch (name) {
    case 'NotAllowedError':
    case 'SecurityError':
        return `NoPermission:${name}`;
    case 'NotFoundError':
    case 'OverconstrainedError':
        return `NoDevice:${name}`;
    case 'NotReadableError':
    case 'AbortError':
        return `DeviceBusy:${name}`;
    default:
        return `Unknown:${name || 'Error'}`;
    }
}

// Web-only relay: forwards JS-side recorder state to Blazor.
// Pushed into by opus-media-recorder.ts (the JS side is the source of truth on web);
// subscribed by audio-recorder.ts which calls IAudioRecorderBackend.OnRecordingStateChange.
// NOT used on MAUI (audio-recorder.ts is never instantiated; RecordingActivityClient targets
// RecordingActivity instead).
export class AudioRecorderState {
    private static readonly stateChangedSubject: Subject<RecorderState> = new Subject<RecorderState>();
    private static readonly recordingHeartbeatSubject: Subject<void> = new Subject<void>();
    private static readonly recordingFailedSubject: Subject<RecordingFailure> = new Subject<RecordingFailure>();

    private static isRecording = false;
    private static isSignalDetected = false;
    private static isConnected = false;
    private static isVoiceActive = false;
    private static lastState: RecorderState =
        { isRecording: false, isSignalDetected: false, isConnected: false, isVoiceActive: false };

    public static get stateChanged$(): Observable<RecorderState> {
        return AudioRecorderState.stateChangedSubject.asObservable();
    }

    public static get recordingHeartbeat$(): Observable<void> {
        return AudioRecorderState.recordingHeartbeatSubject.asObservable();
    }

    public static get recordingFailed$(): Observable<RecordingFailure> {
        return AudioRecorderState.recordingFailedSubject.asObservable();
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

    // The mic is acquired inside an un-awaited recording action, so a getUserMedia failure can't
    // reach whoever called start() - it has to be pushed out from where it happens.
    public static setFailed(chatId: string, error: unknown): void {
        const failure = describeRecordingError(error);
        debugLog?.log(`AudioRecorderState.setFailed(): #${chatId} - ${failure}`);
        AudioRecorderState.recordingFailedSubject.next({ chatId, failure });
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
