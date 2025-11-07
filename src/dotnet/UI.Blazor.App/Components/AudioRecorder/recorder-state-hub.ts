import { Observable, Subject } from 'rxjs';
import { debounce, PromiseSource } from 'promises';
import { RecorderState } from './opus-media-recorder-contracts';
import { Log } from 'logging';

const { debugLog } = Log.get('AudioRecorder');

const RecordingFailedInterval = 500;

export class RecorderStateHub {
    private static readonly audioPowerChangedSubject: Subject<number> = new Subject<number>();
    private static readonly recorderStateChangedSubject: Subject<RecorderState> = new Subject<RecorderState>();
    private static readonly recordingHeartbeatSubject: Subject<void> = new Subject<void>();

    private static isRecording: boolean = false;
    private static isSignalDetected: boolean = false;
    private static isConnected: boolean = false;
    private static isVoiceActive: boolean = false;
    private static lastState: RecorderState = { isRecording: false, isSignalDetected: false, isConnected: false, isVoiceActive: false };

    public static get audioPowerChanged$(): Observable<number> {
        return RecorderStateHub.audioPowerChangedSubject.asObservable();
    }

    public static get recorderStateChanged$(): Observable<RecorderState> {
        return RecorderStateHub.recorderStateChangedSubject.asObservable();
    }

    public static get recordingHeartbeat$(): Observable<void> {
        return RecorderStateHub.recordingHeartbeatSubject.asObservable();
    }

    public static getState(): RecorderState {
        return {
            isRecording: RecorderStateHub.isRecording,
            isSignalDetected: RecorderStateHub.isSignalDetected,
            isConnected: RecorderStateHub.isConnected,
            isVoiceActive: RecorderStateHub.isVoiceActive,
        };
    }

    public static setRecording(isRecording: boolean): void {
        if (RecorderStateHub.isRecording === isRecording)
            return;
        RecorderStateHub.isRecording = isRecording;
        RecorderStateHub.stateChanged();
    }

    public static setConnected(isConnected: boolean): void {
        if (RecorderStateHub.isConnected === isConnected)
            return;
        RecorderStateHub.isConnected = isConnected;
        RecorderStateHub.stateChanged();
    }

    public static setSignalDetected(isSignalDetected: boolean): void {
        if (RecorderStateHub.isSignalDetected === isSignalDetected)
            return;
        RecorderStateHub.isSignalDetected = isSignalDetected;
        RecorderStateHub.stateChanged();
    }

    public static setVoiceActive(isVoiceActive: boolean): void {
        if (RecorderStateHub.isVoiceActive === isVoiceActive)
            return;
        RecorderStateHub.isVoiceActive = isVoiceActive;
        RecorderStateHub.stateChanged();
    }

    public static onAudioPowerChange(power: number): void {
        RecorderStateHub.audioPowerChangedSubject.next(power);
    }

    public static microphoneIsCaptured(): void {
        RecorderStateHub.recordingHeartbeatSubject.next();
        if (!RecorderStateHub.isSignalDetected)
            RecorderStateHub.setSignalDetected(true);
        RecorderStateHub.recordingFailedDebounced();
    }

    private static recordingFailedDebounced = debounce(() => RecorderStateHub.recordingFailed(), RecordingFailedInterval);
    private static async recordingFailed(): Promise<void> {
        RecorderStateHub.setSignalDetected(false);
    }

    private static stateChanged(): void {
        const state: RecorderState = {
            isRecording: RecorderStateHub.isRecording,
            isSignalDetected: RecorderStateHub.isSignalDetected,
            isConnected: RecorderStateHub.isConnected,
            isVoiceActive: RecorderStateHub.isVoiceActive,
        };
        debugLog?.log(`stateChanged(): ${JSON.stringify(state)}`);
        const last = RecorderStateHub.lastState;
        if (state.isRecording === last.isRecording
            && state.isSignalDetected === last.isSignalDetected
            && state.isConnected === last.isConnected
            && state.isVoiceActive === last.isVoiceActive)
            return;
        RecorderStateHub.lastState = state;
        RecorderStateHub.recorderStateChangedSubject.next(state);
    }
}

(globalThis as any)['recorderState'] = RecorderStateHub;
