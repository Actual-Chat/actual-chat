import './feedback.css';

const filledStar: string = 'fa-star';
const emptyStar: string = 'fa-star-o';

export class Feedback {
    private blazorRef: DotNet.DotNetObject;
    private feedbackDiv: HTMLDivElement;
    private stars: HTMLButtonElement[];
    private defaultStars: string[];

    public static create(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): Feedback {
        return new Feedback(feedbackDiv, blazorRef);
    }

    constructor(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.feedbackDiv = feedbackDiv;
        const stars = this.feedbackDiv.querySelectorAll('.rating-button');
        const arr = [];
        const defaultStars = [];
        for (let i = 0; i < stars.length; i++) {
            arr[i] = stars[i];
            defaultStars[i] = emptyStar;
        }
        this.stars = arr;
        this.defaultStars = defaultStars;
        this.feedbackDiv.addEventListener('mouseleave', this.onMouseLeave);
        this.feedbackDiv.addEventListener('mouseenter', this.onMouseEnter);
    }

    private dispose(): void {
        this.feedbackDiv.removeEventListener('mouseenter', this.onMouseEnter);
        this.feedbackDiv.removeEventListener('mouseleave', this.onMouseLeave);
        for (let i = 0; i < this.stars.length; i++) {
            let star = this.stars[i];
            star.removeEventListener('mouseenter', this.onStarMouseEnter);
        }
    }

    private updateRating(id: number): void {
        for (let i = 0; i < this.defaultStars.length; i++) {
            this.defaultStars[i] = i <= id ? filledStar : emptyStar;
            this.stars[i].removeEventListener('mouseenter', this.onStarMouseEnter);
        }
    }

    private fillStar(star: HTMLElement): void {
        star.classList.replace(emptyStar, filledStar);
    }

    private clearStar(star: HTMLElement): void {
        star.classList.replace(filledStar, emptyStar);
    }

    private onMouseLeave = (event: Event & {target: Element; }): void => {
        for (let i = 0; i < this.stars.length; i++) {
            let icon = this.stars[i].querySelector('.rating-icon');
            let defaultClass = this.defaultStars[i];
            icon.classList.remove(filledStar);
            icon.classList.remove(emptyStar);
            icon.classList.add(defaultClass);
        }
    }

    private onMouseEnter = (event: Event & {target: Element; }): void => {
        for (let i = 0; i < this.stars.length; i++) {
            this.stars[i].addEventListener('mouseenter', this.onStarMouseEnter);
        }
    }

    private onStarMouseEnter = (event: Event & { target: Element; }): void => {
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
    }
}
