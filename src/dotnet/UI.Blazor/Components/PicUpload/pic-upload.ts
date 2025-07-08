import { Disposable } from 'disposable';
import { fromEvent, map, filter, Subject, takeUntil, tap, merge } from 'rxjs';
import { preventDefaultForEvent } from 'event-handling';

export class PicUpload implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();

    public static create(
        dropZone: HTMLElement,
        inputId: string): PicUpload {
        return new PicUpload(dropZone, document.getElementById(inputId) as HTMLInputElement);
    }

    constructor(
        private readonly dropZone: HTMLElement,
        private readonly input: HTMLInputElement) {
        merge(fromEvent(this.dropZone, 'dragenter'), fromEvent(this.dropZone, 'dragover')).pipe(
            takeUntil(this.disposed$),
            tap((e: DragEvent) => preventDefaultForEvent(e)),
        ).subscribe(() => this.addHoverClass());

        fromEvent(this.dropZone, 'dragleave').pipe(
            takeUntil(this.disposed$),
            tap((e: DragEvent) => preventDefaultForEvent(e)),
        ).subscribe(() => this.removeHoverClass());

        fromEvent(this.dropZone, 'drop').pipe(
            takeUntil(this.disposed$),
            tap((e: DragEvent) => {
                preventDefaultForEvent(e);
                this.removeHoverClass();
            }),
            map((e: DragEvent) => e.dataTransfer?.files),
            filter((files: FileList | null) => !!files && files.length > 0),
        ).subscribe(f => this.raiseChangeEvent(f!));

        fromEvent(document, 'paste').pipe(
            takeUntil(this.disposed$),
            map((e: ClipboardEvent) => e.clipboardData?.files),
            filter((files: FileList | null) => !!files && files.length > 0),
        ).subscribe(f => this.raiseChangeEvent(f!));
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private addHoverClass() {
        this.dropZone.classList.add('pic-upload-drop-zone-hover');
    }

    private removeHoverClass() {
        this.dropZone.classList.remove('pic-upload-drop-zone-hover');
    }

    private raiseChangeEvent(files: FileList) {
        if (!files)
            return;

        this.input.files = files;
        const event = new Event('change', { bubbles: true });
        this.input.dispatchEvent(event);
    }
}
