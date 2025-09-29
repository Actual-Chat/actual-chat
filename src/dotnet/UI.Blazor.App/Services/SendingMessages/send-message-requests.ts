import { Kvas } from 'kvas';
import { WebKvasBackend } from '../../../UI.Blazor/Services/Kvas/web-kvas-backend';

export const sendMessageRequests = new WebKvasBackend(new Kvas('send-message-requests', true), "1.0", false, true);
