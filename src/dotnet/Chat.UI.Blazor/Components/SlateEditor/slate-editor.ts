import React from "react"
import ReactDOM from "react-dom/client"
import { Square } from './Square';
import { LikeButton } from './like-button';
import { SlateComponent } from './slate-component';
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

        const e = React.createElement;
        const root = ReactDOM.createRoot(editorDiv);
        //root.render(e(Square, { value : "xXx" }, null));
        //root.render(e(SlateComponent));
        const mentionExampleWrapped = () =>
            MentionExample(this.editorHandle)
        root.render(e(mentionExampleWrapped));
    }

    public getText = () =>
        this.editorHandle.getText();

    private dispose() {
    }
}


