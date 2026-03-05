import { Disposable } from 'disposable';
import { filter, fromEvent, Subject, takeUntil } from 'rxjs';
import { IUploadStreamSource } from '../../Services/FileUploads/web-uploads';

export interface Options {
    maxSize?: number;
}

export class FileUploadPicker implements Disposable, IUploadStreamSource {
    private disposed$: Subject<void> = new Subject<void>();
    private pendingFile: File | null = null;

    public static create(
        input: HTMLInputElement,
        blazorRef: DotNet.DotNetObject): FileUploadPicker {
        return new FileUploadPicker(input, blazorRef);
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

    getBlob(): Blob {
        const file = this.pendingFile;
        if (!file)
            throw new Error('FileUpload has no selected file.');
        return file;
    }

    public clear()
    {
        this.pendingFile = null;
        this.input.value = null!;
    }

    public dispose(): void {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
