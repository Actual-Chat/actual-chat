const LogScope: string = 'ChatHighlight';

export class ChatHighlight {
    private blazorRef: DotNet.DotNetObject;
    private editorDiv: HTMLDivElement;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): ChatHighlight {
        return new ChatHighlight(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        console.log('Highlight ts working fine');
    }


    public dispose() {
    }
}

