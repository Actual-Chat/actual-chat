import React from "react"
import ReactDOM from "react-dom/client"
import { createSlateEditorCore, MarkupNode } from './slate-editor-core';
import { SlateEditorHandle } from './slate-editor-handle';
import './slate-editor.css';

const LogScope = 'SlateEditor';

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
        this.editorHandle.onOpenPrevious = this.onOpenPrevious;
        this.editorHandle.onMentionCommand = this.onMentionCommand;
        this.editorHandle.onRendered = this.onRendered;

        // @ts-ignore
        this.editorDiv.editorHandle = this.editorHandle;

        const slateEditor = () => createSlateEditorCore(this.editorHandle, this.debug)
        this.reactDomRoot = ReactDOM.createRoot(this.editorDiv);
        this.reactDomRoot.render(React.createElement(slateEditor));
        if (debug)
            console.debug(`${LogScope}.ctor`);
    }

    public dispose() {
        this.reactDomRoot.unmount();
    }
    public getText = () =>
        this.editorHandle.getText();

    public setMarkup = (nodes: MarkupNode[]) => {
        this.clearText();
        this.editorHandle.setMarkup(nodes);
    };

    public clearText = () =>
        this.editorHandle.clearText();

    public insertMention = (id: string, name: string) =>
        this.editorHandle.insertMention(id, name);

    public setPlaceholder = (placeholder: string) =>
        this.editorHandle.setPlaceholder(placeholder);

    public focus = () => {
        if (this.debug)
            console.debug(`${LogScope}.focus`);
        const input = this.editorDiv.querySelector('div');
        if (input)
            input.focus();
        else
            console.log(`${LogScope}.focus: no input to focus`);
    }

    public moveCursorToEnd = () => {
        this.editorHandle.moveCursorToEnd();
    }

    // Private methods

    private onRendered = () => {
        if (this.debug)
            console.debug(`${LogScope}.onRendered`);
        if (this.autofocus) {
            const width = document.documentElement.clientWidth;
            if (width >= 768) {
                // invoke focus with delay, otherwise in SSB editor gets focus but immediately loses it.
                setTimeout(this.focus, 250);
            }
        }
    }
    private onPost = () =>
        this.blazorRef.invokeMethodAsync("OnPost", this.getText());

    private onCancel = () =>
        this.blazorRef.invokeMethodAsync("OnCancel");

    private onOpenPrevious = () =>
        this.blazorRef.invokeMethodAsync("OnOpenPrevious");

    private onMentionCommand = (cmd: string, args: string) : any =>
        this.blazorRef.invokeMethodAsync("OnMentionCommand", cmd, args);
}


