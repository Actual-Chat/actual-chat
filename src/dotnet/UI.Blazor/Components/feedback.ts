import './feedback.css';

export class Feedback {
    private blazorRef: DotNet.DotNetObject;
    private feedbackDiv: HTMLDivElement;
    private stars: Array<HTMLButtonElement>;
    private defaultStars: Array<string>;
    private readonly filledStar: string = 'fa-star';
    private readonly emptyStar: string = 'fa-star-o';

    public static create(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): Feedback {
        return new Feedback(feedbackDiv, blazorRef);
    }

    constructor(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.feedbackDiv = feedbackDiv;
        const stars = this.feedbackDiv.querySelectorAll('.rating-button');
        const arr = [];
        const defaultStars = [];
        for(let i = 0; i < stars.length; i++) {
            arr[i] = stars[i];
            defaultStars[i] = this.emptyStar;
        }
        this.stars = arr;
        this.defaultStars = defaultStars;
        this.feedbackDiv.addEventListener('mouseleave', this.feedbackLeaveListener);
        this.feedbackDiv.addEventListener('mouseenter', this.feedbackEnterListener);
    }

    private fillStar(star: HTMLElement) {
        star.classList.replace(this.emptyStar, this.filledStar);
    }

    private clearStar(star: HTMLElement) {
        star.classList.replace(this.filledStar, this.emptyStar);
    }

    private starEnterListener = ((event: Event & { target: Element; }) => {
        const star = event.target;
        let idString = star.querySelector('.rating-icon').getAttribute('id');
        if (idString != null && idString.length > 14) {
            let id = parseInt(idString.substring(14));
            for (let i = 0; i < this.stars.length; i++) {
                if (i <= id) {
                    this.fillStar(this.stars[i].querySelector('.rating-icon'));
                } else {
                    this.clearStar(this.stars[i].querySelector('.rating-icon'));
                }
            }
        }
    })

    private feedbackLeaveListener = ((event: Event & {target: Element; }) => {
        for (let i = 0; i < this.stars.length; i++) {
            let icon = this.stars[i].querySelector('.rating-icon');
            let defaultClass = this.defaultStars[i];
            icon.classList.remove(this.filledStar);
            icon.classList.remove(this.emptyStar);
            icon.classList.add(defaultClass);
        }
    })

    private feedbackEnterListener = ((event: Event & {target: Element; }) => {
        for (let i = 0; i < this.stars.length; i++) {
            this.stars[i].addEventListener('mouseenter', this.starEnterListener);
        }
    })

    private updateRating(id: number) {
        for (let i = 0; i < this.defaultStars.length; i++) {
            this.defaultStars[i] = i <= id ? this.filledStar : this.emptyStar;
            this.stars[i].removeEventListener('mouseenter', this.starEnterListener);
        }
    }

    private dispose() {
        this.feedbackDiv.removeEventListener('mouseenter', this.feedbackEnterListener);
        this.feedbackDiv.removeEventListener('mouseleave', this.feedbackLeaveListener);
        for (let i = 0; i < this.stars.length; i++) {
            let star = this.stars[i];
            star.removeEventListener('mouseenter', this.starEnterListener);
        }
    }
}
