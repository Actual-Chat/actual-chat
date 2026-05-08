/* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access */
import { Observable, Subject } from 'rxjs';
import { getLogs } from 'logging';

const { debugLog } = getLogs('AudioRecorder');

export interface RecordingActivityState {
    isRecording: boolean;
    isVoiceActive: boolean;
}

// JS-side UI signal store consumed by Lit recording-SVG components.
// Settable from web JS (opus-media-recorder) or MAUI Blazor (RecordingActivityClient JSI).
// Holds only fields the Lit components actually read: isRecording, isVoiceActive, audioPower.
export class RecordingActivity {
    private static readonly stateChangedSubject: Subject<RecordingActivityState> = new Subject<RecordingActivityState>();
    private static readonly audioPowerChangedSubject: Subject<number> = new Subject<number>();

    private static isRecording = false;
    private static isVoiceActive = false;

    public static get stateChanged$(): Observable<RecordingActivityState> {
        return RecordingActivity.stateChangedSubject.asObservable();
    }

    public static get audioPowerChanged$(): Observable<number> {
        return RecordingActivity.audioPowerChangedSubject.asObservable();
    }

    public static getState(): RecordingActivityState {
        return {
            isRecording: RecordingActivity.isRecording,
            isVoiceActive: RecordingActivity.isVoiceActive,
        };
    }

    public static setRecording(isRecording: boolean): void {
        if (RecordingActivity.isRecording === isRecording)
            return;
        RecordingActivity.isRecording = isRecording;
        RecordingActivity.emitStateChanged();
    }

    public static setVoiceActive(isVoiceActive: boolean): void {
        if (RecordingActivity.isVoiceActive === isVoiceActive)
            return;
        RecordingActivity.isVoiceActive = isVoiceActive;
        RecordingActivity.emitStateChanged();
    }

    public static setAudioPower(power: number): void {
        RecordingActivity.audioPowerChangedSubject.next(power);
    }

    private static emitStateChanged(): void {
        const state = RecordingActivity.getState();
        debugLog?.log(`RecordingActivity.stateChanged(): ${JSON.stringify(state)}`);
        RecordingActivity.stateChangedSubject.next(state);
    }
}

(globalThis as any)['recordingActivity'] = RecordingActivity;
