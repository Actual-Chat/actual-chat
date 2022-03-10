import './feedback.css';

export class Feedback {
    private blazorRef: DotNet.DotNetObject;
    private feedbackDiv: HTMLDivElement;
    private stars: Array<HTMLButtonElement>;
    private defaultStars: Array<string>;

    static create(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject): Feedback {
        return new Feedback(feedbackDiv, blazorRef);
    }

    constructor(feedbackDiv: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.feedbackDiv = feedbackDiv;
        let stars = this.feedbackDiv.querySelectorAll('button');
        const arr = Array(5);
        const defaultStars = Array(5);
        for(let i = 0; i < stars.length; i++) {
            let starButton = stars[i];
            arr[i] = starButton;
            defaultStars[i] = 'fa-star-o';
        }
        this.stars = arr;
        this.defaultStars = defaultStars;
        this.feedbackDiv.addEventListener('mouseleave', this.feedbackLeaveListener);
        this.feedbackDiv.addEventListener('mouseenter', this.feedbackEnterListener);
    }

    private fillStar(star: HTMLElement) {
        if (star.classList.contains('fa-star-o')) {
            star.classList.replace('fa-star-o', 'fa-star');
        }
    }

    private clearStar(star: HTMLElement) {
        if (star.classList.contains('fa-star')) {
            star.classList.replace('fa-star', 'fa-star-o');
        }
    }

    private starEnterListener = ((event: Event & { target: Element; }) => {
        let star = event.target;
        let idString = star.querySelector('i').getAttribute('id');
        if (idString != null && idString.length > 5) {
            let id = parseInt(idString.substring(5));
            for (let i = 0; i < this.stars.length; i++) {
                if (i <= id) {
                    this.fillStar(this.stars[i].querySelector('i'));
                } else {
                    this.clearStar(this.stars[i].querySelector('i'));
                }
            }
        }
    })

    private feedbackLeaveListener = ((event: Event & {target: Element; }) => {
        for (let i = 0; i < this.stars.length; i++) {
            let star = this.stars[i];
            let icon = star.querySelector('i');
            let defaultClass = this.defaultStars[i];
            if (icon.classList.contains('fa-star'))
                icon.classList.remove('fa-star');
            if (icon.classList.contains('fa-star-o'))
                icon.classList.remove('fa-star-o');
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
            this.defaultStars[i] = i <= id ? 'fa-star' : 'fa-star-o';
            this.stars[i].removeEventListener('mouseenter', this.starEnterListener);
        }
    }
}
