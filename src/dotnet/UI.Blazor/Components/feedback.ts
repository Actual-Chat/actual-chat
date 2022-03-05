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
        console.log('constructor this.defaultStars: ', this.defaultStars);
        this.feedbackDiv.addEventListener('mouseout', this.feedbackOutListener);
        this.feedbackDiv.addEventListener('mouseover', this.feedbackOverListener);
    }

    private starOverListener = ((event: Event & { target: Element; }) => {
        let star = event.target;
        let idString = star.getAttribute('id');
        if (idString != null && idString.length > 5) {
            let id = parseInt(idString.substring(5));
            let icon = this.stars[id].querySelector('i');
            if (icon.classList.contains('fa-star-o'))
            for (let i = 0; i <= id; i++) {
                let star = this.stars[i].querySelector('i');
                star.classList.replace('fa-star-o', 'fa-star');
            }
        }
    })

    private starOutListener = ((event: Event & { target: Element; }) => {
        let star = event.target;
        let idString = star.getAttribute('id');
        if (idString != null && idString.length > 5) {
            let id = parseInt(idString.substring(5));
            let icon = this.stars[id].querySelector('i');
            if (icon.classList.contains('fa-star'))
                for (let i = 0; i <= id; i++) {
                    let star = this.stars[i].querySelector('i');
                    star.classList.replace('fa-star', 'fa-star-o');
                }
        }
    })

    private feedbackOutListener = ((event: Event & {target: Element; }) => {

        for (let i = 0; i < this.stars.length; i++) {
            let star = this.stars[i];
            let defaultClass = this.defaultStars[i];
            if (star.querySelector('i').classList.contains('fa-star'))
                star.classList.remove('fa-star');
            if (star.querySelector('i').classList.contains('fa-star-o'))
                star.classList.remove('fa-star-o');
            star.querySelector('i').classList.add(defaultClass);
        }
        console.log('feedbackOutListener this.defaultStars: ', this.defaultStars);
    })

    private feedbackOverListener = ((event: Event & {target: Element; }) => {

        for (let i = 0; i < this.stars.length; i++) {
            this.stars[i].addEventListener('mouseover', this.starOverListener);
            this.stars[i].addEventListener('mouseout', this.starOutListener);
        }
        console.log('feedbackOutListener this.defaultStars: ', this.defaultStars);
    })

    private updateRating(id: number) {
        console.log('id: ', id);
        for (let i = 0; i < this.defaultStars.length; i++) {
            this.defaultStars[i] = i <= id ? 'fa-star' : 'fa-star-o';
            this.stars[i].removeEventListener('mouseover', this.starOverListener);
            this.stars[i].removeEventListener('mouseout', this.starOutListener);
        }
        console.log('this.defaultStars: ', this.defaultStars);
    }
}
