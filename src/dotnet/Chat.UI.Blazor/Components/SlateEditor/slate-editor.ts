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
        root.render(e(LikeButton));
    }

    private dispose() {
    }
}

const e = React.createElement;
class LikeButton extends React.Component {
    constructor(props) {
        super(props);
        this.state = {
            liked: false
        };
    }
    render() {
        if (this.state.liked) {
            return 'You liked this.';
        }
        return e(
            'button', {
                onClick: () => this.setState({
                                                 liked: true
                                             })
            },
            'Like'
        );
    }
}
