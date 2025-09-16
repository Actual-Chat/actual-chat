import { Tune, TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { Log } from 'logging';
import { fromEvent, Subject, takeUntil } from 'rxjs';

const { debugLog, errorLog } = Log.get('Attachments');

function hasShowOpenFilePicker(
    win: Window
): win is Window & { showOpenFilePicker: (options?: OpenFilePickerOptions) => Promise<FileSystemFileHandle[]> } {
    return "showOpenFilePicker" in win;
}

interface FileInfo {
    id: number;
    file: File;
    fileHandle: FileSystemFileHandle | null;
}

export class AttachmentWebFilePickerStorage
{
    private static filesMap: Map<number, FileInfo> = new Map<number, FileInfo>();
    private static filesMapIdSeed: number = 0;

    public static Add(file: File, fileHandle: FileSystemFileHandle | null) : FileInfo
    {
        const fileInfo : FileInfo = {
            id: this.filesMapIdSeed,
            file: file,
            fileHandle: fileHandle,
        }
        this.filesMapIdSeed++;
        this.filesMap.set(fileInfo.id, fileInfo);
        return fileInfo;
    }

    public static Get(id : number) : FileInfo | undefined
    {
        return this.filesMap.get(id);
    }

    public static Remove(id : number)
    {
        this.filesMap.delete(id);
    }
}

export class AttachmentWebFilePickerBackend {
    public constructor(private readonly blazorRef: DotNet.DotNetObject)
    {
    }

    public async add(file: File, fileHandle: FileSystemFileHandle | null): Promise<boolean> {
        const fileInfo = AttachmentWebFilePickerStorage.Add(file, fileHandle);
        const isAdded = await this.invokeFilePicked(fileInfo);
        if (!isAdded) {
            AttachmentWebFilePickerStorage.Remove(fileInfo.id);
            return false;
        }

        return true;
    }

    private async invokeFilePicked(fileInfo: FileInfo) {
        const file = fileInfo.file;
        return this.blazorRef.invokeMethodAsync<boolean>(
            'OnFilePicked', fileInfo.id, file.name, file.type, file.size);
    }
}

export class AttachmentWebFilePicker {
    private readonly disposed$: Subject<void> = new Subject<void>();

    public constructor(
        private readonly backend: AttachmentWebFilePickerBackend,
        private readonly filePickerElement: HTMLInputElement) {
        fromEvent(this.filePickerElement, 'change').pipe(takeUntil(this.disposed$)).subscribe(this.onFilePickerChange);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public showFilePicker = async (acceptTypes: string = "") => {
        TuneUI.play(Tune.ChangeAttachments);
        // NOTE: acceptTypes is not empty on an Android platform only. Let's implement it later.
        if (acceptTypes === "" && hasShowOpenFilePicker(window)) {
            try {
                const files = await window.showOpenFilePicker({
                    multiple: true,
                });
                await this.onFilesPicked(files);
            }
            catch (e) {
                // NOTE: showOpenFilePicker throws AbortError when the user cancels the picker.
                const isAbortError = e instanceof DOMException && e.name == 'AbortError';
                if (!isAbortError)
                    errorLog?.log('showOpenFilePicker failed', e);
            }
        }
        else {
            this.filePickerElement.accept = acceptTypes;
            this.filePickerElement.click();
        }
    };

    private onFilePickerChange = (async (event: Event & { target: Element; }) => {
        for (const file of this.filePickerElement.files ?? []) {
            await this.add(file, null);
        }
        this.filePickerElement.value = '';
    });

    private async onFilesPicked(fileHandles : FileSystemFileHandle[])
    {
        console.log(fileHandles);
        for (const fileHandle of fileHandles) {
            const file = await fileHandle.getFile();
            await this.add(file, fileHandle);
        }
    }

    private async add(file: File, fileHandle: FileSystemFileHandle | null): Promise<void> {
        await this.backend.add(file, fileHandle);
    }
}
