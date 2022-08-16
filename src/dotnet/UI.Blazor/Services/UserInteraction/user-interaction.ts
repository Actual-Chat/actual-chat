import { addPostInteractionHandler } from '../../../../nodejs/src/first-interaction';

const LogScope: string = 'UserInteraction';

export class UserInteraction {
    public static initialize(blazorRef: DotNet.DotNetObject): void {
        console.debug(`${LogScope}: initialize`);
        addPostInteractionHandler(() => {
            console.debug(`${LogScope}: calling MarkInteractionHappened`);
            blazorRef.invokeMethodAsync("MarkInteractionHappened");
        });
    }
}
