import { Kvas } from 'kvas';
import { WebKvasBackend } from '../../../UI.Blazor/Services/Kvas/web-kvas-backend';

export const uploadSessions = new WebKvasBackend(new Kvas('upload-sessions', true), "1.0", false, true);
