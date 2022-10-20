import { audioContextLazy } from 'audio-context-lazy';

const LogScope: string = 'UserInteractionUI';

export class UserInteractionUI {
    public static async check(blazorRef: DotNet.DotNetObject): Promise<void> {
        console.debug(`${LogScope}: check`);
        await audioContextLazy.get();
        console.debug(`${LogScope}: calling MarkInteractionHappened`);
        await blazorRef.invokeMethodAsync("MarkInteractionHappened");
    }
}
