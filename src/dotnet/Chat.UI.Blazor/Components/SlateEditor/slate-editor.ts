import React from "react"
import ReactDOM from "react-dom/client"
import { Square } from './Square';
import { LikeButton } from './like-button';
import { SlateComponent } from './slate-component';
import { MentionExample } from './mentions';

export class SlateEditor {
    private blazorRef: DotNet.DotNetObject;
    private editorDiv: HTMLDivElement;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): SlateEditor {
        return new SlateEditor(editorDiv, blazorRef);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;

        const e = React.createElement;
        const root = ReactDOM.createRoot(editorDiv);
        //root.render(e(Square, { value : "xXx" }, null));
        //root.render(e(SlateComponent));
        root.render(e(MentionExample));
    }

    private dispose() {
    }
}


