import React from "react"
import ReactDOM from "react-dom/client"
import { MentionExample } from './mentions';
import { SlateEditorHandle } from './slate-editor-handle';

export class SlateEditor {
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly editorDiv: HTMLDivElement;
    private readonly editorHandle: SlateEditorHandle;
    private readonly reactDomRoot: any;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): SlateEditor {
        return new SlateEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;
        this.editorHandle = new SlateEditorHandle();
        this.editorHandle.onPost = this.onPost;
        this.editorHandle.onMentionCommand = this.onMentionCommand;

        // @ts-ignore
        this.editorDiv.editorHandle = this.editorHandle;

        const slateEditor = () => MentionExample(this.editorHandle)
        this.reactDomRoot = ReactDOM.createRoot(this.editorDiv);
        this.reactDomRoot.render(React.createElement(slateEditor));
    }

    public getText = () =>
        this.editorHandle.getText();

    private onPost = () =>
        this.blazorRef.invokeMethodAsync("Post", this.getText());

    public clearText = () =>
        this.editorHandle.clearText();

    private onMentionCommand = (cmd : string, args : string) : any =>
        this.blazorRef.invokeMethodAsync("MentionCommand", cmd, args);

    public insertMention = (mention : any) =>
        this.editorHandle.insertMention(mention);

    private dispose() {
        this.reactDomRoot.unmount();
    }
}


