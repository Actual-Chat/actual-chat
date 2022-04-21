import React from "react"
import ReactDOM from "react-dom/client"
import { MentionExample } from './mentions';
import { SlateEditorHandle } from './slate-editor-handle';

export class SlateEditor {
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly editorDiv: HTMLDivElement;
    private readonly editorHandle: SlateEditorHandle;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): SlateEditor {
        return new SlateEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;
        this.editorHandle = new SlateEditorHandle();
        this.editorHandle.onPost = this.onPost;

        const slateEditor = () => MentionExample(this.editorHandle)
        const root = ReactDOM.createRoot(editorDiv);
        root.render(React.createElement(slateEditor));
    }

    public getText = () =>
        this.editorHandle.getText();

    private onPost = () =>
        this.blazorRef.invokeMethodAsync("Post", this.getText());

    public clearText = () =>
        this.editorHandle.clearText();

    private dispose() {
    }
}


