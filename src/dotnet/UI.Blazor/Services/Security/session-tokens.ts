import {EventHandlerSet} from "event-handling";
import {opusMediaRecorder} from "../../../Audio.UI.Blazor/Components/AudioRecorder/opus-media-recorder";
import {Log} from 'logging';

const { debugLog } = Log.get('SessionTokens');

export class SessionTokens {
    public static readonly headerName = 'Session';
    public static current = '';
    public static changedEvents = new EventHandlerSet<string>();

    public static setCurrent(value: string) {
        debugLog?.log(`setCurrent:`, value);
        this.current = value;
        void opusMediaRecorder.setSessionToken(value);
        this.changedEvents.triggerSilently(value);
    };
}
