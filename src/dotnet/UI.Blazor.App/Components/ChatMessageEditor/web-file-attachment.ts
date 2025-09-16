import { Log } from 'logging';
import { Tune, TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { WebFileProvider } from '../../Services/FileUploads/web-file-providers';
import { AttachmentWebFilePickerStorage } from './attachment-web-file-picker';
import { v4 as uuidv4 } from 'uuid';

const { debugLog, errorLog } = Log.get('Attachments');

interface WebFileAttachment {
    id: string;
    previewUrl: string;
    fileProvider : WebFileProvider | null;
}

interface CreateWebFileAttachmentResult {
    id: string;
    previewUrl: string;
    fileProvider : any;
}

export class WebFileAttachments
{
    private static attachments: Map<string, WebFileAttachment> = new Map<string, WebFileAttachment>();

    public static create(fileId : number, chatId: string, blazorRef: DotNet.DotNetObject) : CreateWebFileAttachmentResult | null
    {
        const fileInfo = AttachmentWebFilePickerStorage.Get(fileId);
        if (!fileInfo)
            return null;

        const file = fileInfo.file;
        const url = URL.createObjectURL(file);

        try {
            const provider = new WebFileProvider('', fileInfo.fileHandle, file, file.name, chatId, blazorRef);
            const attachment: WebFileAttachment = {
                id: uuidv4(),
                previewUrl: url,
                fileProvider: provider,
            };
            this.attachments.set(attachment.id, attachment);
            // @ts-ignore
            const jsObjectReference = DotNet.createJSObjectReference(provider);
            return {
                id: attachment.id,
                previewUrl: attachment.previewUrl,
                fileProvider: jsObjectReference,
            };
        }
        catch (e) {
            errorLog?.log('Failed to create a web file attachment', e);
            URL.revokeObjectURL(url);
            return null;
        }
    }

    public static async dispose(id : string)
    {
        // TuneUI.play(Tune.ChangeAttachments);
        const attachment = this.attachments.get(id);
        if (!attachment)
            return;

        if (attachment.fileProvider) {
            attachment.fileProvider.cancel();
            await attachment.fileProvider.removeFileHandleFromDb();
        }
        URL.revokeObjectURL(attachment.previewUrl);
        this.attachments.delete(id);
    }
}
