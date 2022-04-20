export class SlateEditor {
    private blazorRef: DotNet.DotNetObject;
    private editorDiv: HTMLDivElement;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): SlateEditor {
        return new SlateEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;
    }

    private dispose() {
    }
}
