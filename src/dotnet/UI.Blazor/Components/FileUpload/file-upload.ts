import { Disposable } from 'disposable';
import { filter, fromEvent, Subject, takeUntil } from 'rxjs';
import { Log } from 'logging';
import { ChunkedFileUpload } from './chunked-file-upload';

const { debugLog, errorLog } = Log.get('FileUpload');

export interface Options {
    maxSize?: number;
}

export class FileUpload implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private pendingFile: File | null = null;

    public static create(
        input: HTMLInputElement,
        blazorRef: DotNet.DotNetObject): FileUpload {
        return new FileUpload(input, blazorRef);
    }

    constructor(
        private readonly input: HTMLInputElement,
        blazorRef: DotNet.DotNetObject)
    {
        fromEvent(input, 'change')
            .pipe(
                takeUntil(this.disposed$),
                filter(() => !!this.input.files?.[0]),
            )
            .subscribe(() => {
                const file = this.input.files![0];
                this.pendingFile = file;
                void blazorRef.invokeMethodAsync(
                    'FileSelected',
                    file.name,
                    file.type || 'application/octet-stream',
                    file.size
                );
            });
    }

    public startUpload(uploadId: string, blazorRef: DotNet.DotNetObject): void {
        const file = this.pendingFile;
        if (!file) {
            errorLog?.log('startUpload called but no pending file');
            return;
        }

        ChunkedFileUpload.startWithReporter(uploadId, file, blazorRef, () => { this.clear() } );
    }

    public clear()
    {
        this.pendingFile = null;
        this.input.value = null!;
    }

    public dispose(): void {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
