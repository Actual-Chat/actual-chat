import { Disposable } from 'disposable';
import { filter, from, fromEvent, map, Subject, switchMap, takeUntil } from 'rxjs';
import { BrowserInit } from "../../Services/BrowserInit/browser-init";
import { SessionToken } from "../../Services/Security/session-token";

export interface Options {
    maxSize?: number;
    uploadUrl: string;
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
        let url = this.getUrl(options.uploadUrl);

        fromEvent(input, 'change')
            .pipe(
                takeUntil(this.disposed$),
                map(() => this.input.files[0]),
                filter((file: File) => !!file),
                filter((file: File) => {
                    if (options.maxSize !== null && file.size > options.maxSize) {
                        input.value = null;
                        void blazorRef.invokeMethodAsync('OnInvalidSize');
                        return false;
                    }
                    return true;
                }),
                map((file: File) => {
                    const formData = new FormData();
                    formData.append('file', file, file.name);
                    return formData;
                }),
                map((formData: FormData) => fetch(url, {
                    method: 'POST',
                    body: formData,
                    headers: { [SessionToken.headerName]: SessionToken.current },
                })),
                switchMap((promise: Promise<Response>) => from(promise)),
            )
            .subscribe(async (response: Response) => {
                const mediaContent = await response.json();
                await blazorRef.invokeMethodAsync('OnUploaded', mediaContent);
            });
    }

    public dispose(): void {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private getUrl(url: string) {
        // @ts-ignore
        const baseUri = BrowserInit.baseUri;
        return baseUri ? new URL(url, baseUri).toString() : url;
    }
}
