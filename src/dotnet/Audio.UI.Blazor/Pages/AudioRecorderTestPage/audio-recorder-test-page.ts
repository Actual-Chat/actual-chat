import {AudioRecorder} from "../../Components/AudioRecorder/audio-recorder";
import {
    IRecordingEventQueue,
    RecordingEvent,
    RecordingEventType
} from "../../Components/AudioRecorder/recording-event-queue";

class DataUrlSendingQueue implements IRecordingEventQueue {
    private readonly _buffer: Uint8Array = new Uint8Array(10*1024);
    private _data: ArrayBuffer[] = [];
    public flushAsync(): Promise<void> {
        return Promise.resolve();
    }
    public getBlob(): Blob {
        return new Blob(this._data);
    }
    public reset(): void {
        this._data = [];
    }

    append(command: RecordingEvent): void {
        if (command.type != RecordingEventType.Data) {
            return;
        }
        const length = command.serialize(this._buffer, 0);
        this._data.push(this._buffer.slice(0, length));
    }
}

export class AudioRecorderTestPage extends AudioRecorder {

    private _recordNumber: number;
    private _recordsRef: HTMLElement;

    public static createObj(blazorRef: DotNet.DotNetObject, debugMode: boolean, recordsRef: HTMLElement, recordNumber: number) {
        return new AudioRecorderTestPage(blazorRef, debugMode, recordsRef, recordNumber);
    }

    public constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean, recordsRef: HTMLElement, recordNumber: number) {
        super(blazorRef, debugMode, new DataUrlSendingQueue());
        this._recordsRef = recordsRef;
        this._recordNumber = recordNumber;
    }

    public stopRecording(): Promise<void> {
        const result = super.stopRecording();
        const audio = document.createElement('audio');
        const source = document.createElement('source');
        const blob = (this._queue as DataUrlSendingQueue).getBlob();
        audio.className = "block";
        audio.controls = true;
        source.src = URL.createObjectURL(blob);
        source.type = "audio/webm";
        audio.appendChild(source)
        const link = document.createElement('a');
        link.href = source.src;
        link.type = source.type;
        link["download"] = `record_${this._recordNumber}.webm`;
        link.text = `Download ${link["download"]}`;
        link.className = "text-xl block p-3 cursor-pointer";
        const div = document.createElement('div');
        div.className = "mt-3 p-3 pt-4";
        div.appendChild(audio);
        div.appendChild(link);
        this._recordsRef.appendChild(div);
        return result;
    }
}
