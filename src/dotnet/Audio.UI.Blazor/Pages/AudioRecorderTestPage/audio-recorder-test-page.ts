import { AudioRecorder } from '../../Components/AudioRecorder/audio-recorder';

export class AudioRecorderTestPage extends AudioRecorder {

    private _recordNumber: number;
    private _recordsRef: HTMLElement;

    public static createObj(
        blazorRef: DotNet.DotNetObject,
        debugMode: boolean,
        recordsRef: HTMLElement,
        recordNumber: number,
        sessionId: string) {
        return new AudioRecorderTestPage(blazorRef, debugMode, recordsRef, recordNumber, sessionId);
    }

    public constructor(
        blazorRef: DotNet.DotNetObject,
        debugMode: boolean,
        recordsRef: HTMLElement,
        recordNumber: number,
        sessionId: string) {
        super(blazorRef, sessionId);
        this._recordsRef = recordsRef;
        this._recordNumber = recordNumber;
    }

    public stopRecording(): Promise<void> {
        const result = super.stopRecording();
        const audio = document.createElement('audio');
        // const source = document.createElement('source');
        // const blob = (this.queue as DataUrlSendingQueue).getBlob();
        audio.className = 'block';
        audio.controls = true;
        // source.src = URL.createObjectURL(blob);
        // source.type = 'audio/webm';
        // audio.appendChild(source);
        // const link = document.createElement('a');
        // link.href = source.src;
        // link.type = source.type;
        // link['download'] = `record_${this._recordNumber}.webm`;
        // link.text = `Download ${link['download']}`;
        // link.className = 'text-xl block p-3 cursor-pointer';
        // const div = document.createElement('div');
        // div.className = 'mt-3 p-3 pt-4';
        // div.appendChild(audio);
        // div.appendChild(link);
        // this._recordsRef.appendChild(div);
        return result;
    }
}
