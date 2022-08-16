import { MarkupNode } from './slate-editor-core';

export class SlateEditorHandle {
    private readonly mention : Mention
    private placeholder : string;

    constructor() {
        this.mention = new Mention(this);
        this.placeholder = '';
    }

    public getText = () : string => ""

    public setMarkup = (nodes: MarkupNode[]) : void => {};

    public clearText = () : void => {}

    public onPost = (text : string) => {}

    public onCancel = () => {}

    public onEditLastMessage = () => {}

    public onHasContentChanged = (hasContent : boolean) => {}

    public onMentionCommand = (cmd : string, args : string = "") => {}

    public insertMention = (id: string, name: string) => {}

    get getMention() : Mention {
        return this.mention;
    }

    public getPlaceholder() : string {
        return this.placeholder;
    }

    public setPlaceholder = (placeholder: string) => {
        this.placeholder = placeholder;
        this.onPlaceholderUpdated();
    }

    public onPlaceholderUpdated = () => {}

    public moveCursorToEnd = () => {}

    public onRendered = () : void => {}
}

class Mention
{
    private owner : SlateEditorHandle;

    constructor(owner : SlateEditorHandle) {
        this.owner = owner;
    }

    show(search: string) {
        this.owner.onMentionCommand("show", search);
    }

    close() {
        this.owner.onMentionCommand("close")
    }

    moveDown() {
        this.owner.onMentionCommand("moveDown")
    }

    moveUp() {
        this.owner.onMentionCommand("moveUp")
    }

    insert() {
        this.owner.onMentionCommand("insert");
    }
}
