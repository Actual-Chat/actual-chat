import './feedback.css';

export class Feedback {
    private blazorRef: DotNet.DotNetObject;
    private feedbackDiv: HTMLDivElement;

    static create(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): Feedback {
        return new Feedback(feedbackDiv, blazorRef);
    }

    constructor(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.feedbackDiv = feedbackDiv;

        this.feedbackDiv.addEventListener('mouseover', this.starHoverListener);

    }

    private starHoverListener = ((event: Event & { target: Element; }) => {
        console.log(Element.length);
    })
}
