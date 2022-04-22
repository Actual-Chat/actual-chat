export class SlateEditorHandle {
    public getText = () : string => ""

    public clearText = () : void => {}

    public onPost = (text : string) => {}

    public onMention = (cmd : string) => {}

    public insertMention = (mention : any) => {}
}
