import React from "react"
import ReactDOM from "react-dom/client"
import { createSlateEditorCore, MarkupNode } from './slate-editor-core';
import { SlateEditorHandle } from './slate-editor-handle';
import './slate-editor.css';

export class SlateEditor {
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly editorDiv: HTMLDivElement;
    private readonly editorHandle: SlateEditorHandle;
    private readonly reactDomRoot: any;
    private readonly debug: boolean;
    private readonly autofocus: boolean;

    static create(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject, debug : boolean, autofocus : boolean): SlateEditor {
        return new SlateEditor(editorDiv, blazorRef, debug, autofocus);
    }

    constructor(editorDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject, debug : boolean, autofocus : boolean) {
        this.editorDiv = editorDiv;
        this.blazorRef = blazorRef;
        this.debug = debug;
        this.autofocus = autofocus;
        this.editorHandle = new SlateEditorHandle();
        this.editorHandle.onPost = this.onPost;
        this.editorHandle.onCancel = this.onCancel;
        this.editorHandle.onEditLastMessage = this.onEditLastMessage;
        this.editorHandle.onMentionCommand = this.onMentionCommand;
        this.editorHandle.onRendered = this.onRendered;

        // @ts-ignore
        this.editorDiv.editorHandle = this.editorHandle;

        const slateEditor = () => createSlateEditorCore(this.editorHandle, this.debug)
        this.reactDomRoot = ReactDOM.createRoot(this.editorDiv);
        this.reactDomRoot.render(React.createElement(slateEditor));
        if (debug) console.log("SlateEditor.constructor");
    }

    public getText = () =>
        this.editorHandle.getText();

    public setMarkup = (nodes: MarkupNode[]) => {
        this.clearText();
        this.editorHandle.setMarkup(nodes);
    };

    private onPost = () =>
        this.blazorRef.invokeMethodAsync("Post", this.getText());

    private onCancel = () =>
        this.blazorRef.invokeMethodAsync("Cancel");

    private onEditLastMessage = () =>
        this.blazorRef.invokeMethodAsync("EditLastMessage");

    public clearText = () =>
        this.editorHandle.clearText();

    private onMentionCommand = (cmd : string, args : string) : any =>
        this.blazorRef.invokeMethodAsync("MentionCommand", cmd, args);

    public insertMention = (mention: { id: string, name: string }) =>
        this.editorHandle.insertMention(mention.id, mention.name);

    public setPlaceholder = (placeholder: string) =>
        this.editorHandle.setPlaceholder(placeholder);

    public focus = () => {
        const input = this.editorDiv.querySelector('div');
        if (input)
            input.focus();
        else
            console.log('slate-editor : no input to focus.');
        if (this.debug) console.log('focus');
    }

    public moveCursorToEnd = () => {
        this.editorHandle.moveCursorToEnd();
    }

    private onRendered = () => {
        if (this.debug) console.log('slate-editor rendered.');
        if (this.autofocus) {
            // invoke focus with delay, otherwise in SSB editor gets focus but immediately loses it.
            setTimeout(this.focus, 250);
        }
    }

    private dispose() {
        this.reactDomRoot.unmount();
    }
}


