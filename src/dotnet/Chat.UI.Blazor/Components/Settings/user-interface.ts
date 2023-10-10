import { Subject, takeUntil } from 'rxjs';
import { Log } from 'logging';
import { Disposable } from 'disposable';
import { FontSizeInit } from 'font-size-init';

const { debugLog } = Log.get('UserInterface');

export class UserInterface implements Disposable {
    private readonly disposed$ = new Subject<void>();

    public static create(blazorRef: DotNet.DotNetObject, fontBox: HTMLElement): UserInterface {
        return new UserInterface(blazorRef, fontBox);
    }

    constructor(
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly fontBox: HTMLElement,
    ) {
        debugLog?.log('constructor');
        this.blazorRef = blazorRef;
        this.fontBox = fontBox;
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public getFontSizes() {
        return FontSizeInit.getFontSizeValues();
    }

    public getRootSize() {
        return FontSizeInit.getRootFontSize();
    }

    public setRootSize(fontTitle: string) {
        return FontSizeInit.setRootFontSize(fontTitle);
    }
}
