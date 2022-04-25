export class SlateEditorHandle {
    private readonly mention : Mention

    constructor() {
        this.mention = new Mention(this);
    }

    public getText = () : string => ""

    public clearText = () : void => {}

    public onPost = (text : string) => {}

    public onHasContentChanged = (hasContent : boolean) => {}

    public onMentionCommand = (cmd : string, args : string = "") => {}

    public insertMention = (mention : any) => {}

    get getMention() : Mention {
        return this.mention;
    }
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
