import { addPostInteractionHandler } from 'first-interaction';

const LogScope: string = 'UserInteractionUI';

export class UserInteractionUI {
    public static initialize(blazorRef: DotNet.DotNetObject): void {
        console.debug(`${LogScope}: initialize`);
        addPostInteractionHandler(() => {
            console.debug(`${LogScope}: calling MarkInteractionHappened`);
            blazorRef.invokeMethodAsync("MarkInteractionHappened");
        });
    }
}
