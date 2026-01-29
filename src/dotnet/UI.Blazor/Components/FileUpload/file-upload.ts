import { Disposable } from 'disposable';
import { filter, from, fromEvent, map, Subject, switchMap, takeUntil } from 'rxjs';
import { Log } from 'logging';
import { ChunkedFileUpload } from '../../../UI.Blazor.App/Services/FileProviders/web-file-providers';

const { debugLog, errorLog } = Log.get('FileUpload');

export interface Options {
    maxSize?: number;
}

export class FileUpload implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();

    public static create(
        input: HTMLInputElement,
        blazorRef: DotNet.DotNetObject,
        options: Options): FileUpload {
        return new FileUpload(input, blazorRef, options);
    }

    constructor(
        private readonly input: HTMLInputElement,
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly options: Options)
    {
        fromEvent(input, 'change')
            .pipe(
                takeUntil(this.disposed$),
                map(() => this.input.files?.[0]),
                filter((file: File) => !!file),
                filter((file: File) => {
                    if (options.maxSize != null && file.size > (options.maxSize ?? 0)) {
                        input.value = null!;
                        void blazorRef.invokeMethodAsync('OnInvalidSize');
                        return false;
                    }
                    return true;
                }),
                map((file: File) => this.uploadFile(file)),
                switchMap((promise: Promise<void>) => from(promise)),
            )
            .subscribe();
    }

    private async uploadFile(file: File): Promise<void> {
        try {
            // Step 1: Reserve MediaId
            debugLog?.log('Reserving MediaId...');
            const mediaIdSid = await this.blazorRef.invokeMethodAsync<string>('ReserveMediaId');
            debugLog?.log(`Reserved MediaId: ${mediaIdSid}`);

            // Step 2: Create Upload
            debugLog?.log('Creating upload...');
            const uploadIdSid = await this.blazorRef.invokeMethodAsync<string>(
                'CreateUpload',
                file.name,
                file.type || 'application/octet-stream',
                file.size
            );
            debugLog?.log(`Created Upload: ${uploadIdSid}`);

            // Step 3: Upload file chunks via TUS protocol
            debugLog?.log('Starting chunked upload...');
            const chunkedUpload = new ChunkedFileUpload(
                uploadIdSid,
                file,
                (progress) => {}
            );
            chunkedUpload.start();
            await chunkedUpload.whenCompleted;
            debugLog?.log('Chunked upload completed');

            // Step 4: Process upload - bind to media and convert
            debugLog?.log('Processing upload...');
            const mediaContent = await this.blazorRef.invokeMethodAsync<any>(
                'ProcessUpload',
                mediaIdSid,
                uploadIdSid
            );
            debugLog?.log('Upload processed successfully');

            // Step 5: Notify completion
            await this.blazorRef.invokeMethodAsync('OnUploaded', mediaContent);
        }
        catch (e) {
            errorLog?.log('Failed to upload file:', e);
            const message = e instanceof Error ? e.message : 'Upload failed';
            void this.blazorRef.invokeMethodAsync('OnUploadError', message);
        }
        finally {
            // Clear the input to allow re-selecting the same file
            this.input.value = null!;
        }
    }

    public dispose(): void {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
