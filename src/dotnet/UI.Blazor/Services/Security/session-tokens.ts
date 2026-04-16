import { EventHandlerSet } from 'event-handling';
import { opusMediaRecorder } from '../../../UI.Blazor.App/Components/AudioRecorder/opus-media-recorder';
import { getLogs } from 'logging';

const { debugLog } = getLogs('SessionTokens');

export class SessionTokens {
    public static readonly headerName = 'Session';
    public static current = '';
    public static changedEvents = new EventHandlerSet<string>();

    public static setCurrent(value: string) {
        debugLog?.log(`setCurrent:`, value);
        this.current = value;
        opusMediaRecorder.setSessionToken(value);
        this.changedEvents.triggerSilently(value);
    };
}
