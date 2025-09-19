import { Log } from 'logging';
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

    public static create(fileId : number, blazorRef: DotNet.DotNetObject) : CreateWebFileAttachmentResult | null
    {
        const fileInfo = AttachmentWebFilePickerStorage.Get(fileId);
        if (!fileInfo)
            return null;

        const file = fileInfo.file;
        let previewUrl = "";
        try {
            const provider = new WebFileProvider('', fileInfo.fileHandle, file, file.name, blazorRef);
            previewUrl = provider.createPreviewUrl();
            const attachment: WebFileAttachment = {
                id: uuidv4(),
                previewUrl: previewUrl,
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
            if (previewUrl)
                URL.revokeObjectURL(previewUrl);
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
            attachment.fileProvider.revokePreviewUrl();
            await attachment.fileProvider.removeFileHandleFromDb();
        }
        this.attachments.delete(id);
    }
}
