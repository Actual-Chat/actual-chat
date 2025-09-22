import { Tune, TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { Log } from 'logging';
import { fromEvent, Subject, takeUntil } from 'rxjs';

const { errorLog } = Log.get('Attachments');

function hasShowOpenFilePicker(
    win: Window
): win is Window & { showOpenFilePicker: (options?: OpenFilePickerOptions) => Promise<FileSystemFileHandle[]> } {
    return "showOpenFilePicker" in win;
}

export class AttachmentWebFilePickerRegistry
{
    private static filesMap: Map<number, PickFileResult> = new Map<number, PickFileResult>();
    private static filesMapIdSeed: number = 0;

    public static Add(file: PickFileResult) : number
    {
        const id = this.filesMapIdSeed++;
        this.filesMap.set(id, file);
        return id;
    }

    public static Get(id : number) : PickFileResult | undefined
    {
        return this.filesMap.get(id);
    }

    public static Remove(id : number)
    {
        this.filesMap.delete(id);
    }
}

interface PickFileResult {
    file: File,
    fileHandle: FileSystemFileHandle | null
}

interface WebFileInfo {
    id: number,
    fileName: string,
    fileType: string,
    size: number,
}

export class AttachmentWebFilePickerBackend {
    public constructor(private readonly blazorRef: DotNet.DotNetObject)
    {
    }

    public async add(fileResults: PickFileResult[]): Promise<void> {
        const webFileInfos : WebFileInfo[] = [];
        for (const fileResult of fileResults) {
            const id = AttachmentWebFilePickerRegistry.Add(fileResult);
            const file = fileResult.file;
            webFileInfos.push({
                id: id,
                fileName: file.name,
                fileType: file.type,
                size: file.size,
            });
        }
        try {
            await this.invokeFilePicked(webFileInfos);
        }
        finally {
            for (const fileInfo of webFileInfos) {
                AttachmentWebFilePickerRegistry.Remove(fileInfo.id);
            }
        }
    }

    private async invokeFilePicked(webFileInfos: WebFileInfo[]) {
        return this.blazorRef.invokeMethodAsync('OnFilePicked', webFileInfos);
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
        const fileResults : PickFileResult[] = [];
        for (const file of this.filePickerElement.files ?? []) {
            fileResults.push({
                file: file,
                fileHandle: null,
            })
        }
        await this.add(fileResults);
        this.filePickerElement.value = '';
    });

    private async onFilesPicked(fileHandles : FileSystemFileHandle[])
    {
        const fileResults : PickFileResult[] = [];
        for (const fileHandle of fileHandles) {
            const file = await fileHandle.getFile();
            fileResults.push({
                file: file,
                fileHandle: fileHandle,
            })
        }
        await this.add(fileResults);
    }

    private async add(fileResults: PickFileResult[]): Promise<void> {
        await this.backend.add(fileResults);
    }
}
