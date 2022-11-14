// import './mention-list.css';

const LogScope = 'Landing';

export class Landing {
    private blazorRef: DotNet.DotNetObject;
    private readonly landing: HTMLElement;
    private pages: {};
    private pageBottoms: {};
    private pageNumbers: {};
    private bottom: number;
    private currentPage: number = 1;

    static create(landing: HTMLElement, blazorRef: DotNet.DotNetObject): Landing {
        return new Landing(landing, blazorRef);
    }

    constructor(landing: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.landing = landing;
        this.blazorRef = blazorRef;
        this.bottom = window.innerHeight;
        this.getPageData();
        this.getPageBottom();
        window.addEventListener('wheel', this.getScrollDirection, {passive: false});
        window.addEventListener('keydown', this.onUpOrDownClick);
    }

    private getPageData() {
        this.pages = {};
        let pages = this.landing.querySelectorAll('.page');
        let bottoms = {};
        let numbers = {};
        let i = 1;
        pages.forEach((elem: HTMLElement) => {
            let id = elem.getAttribute('id');
            this.pages[i] = elem;
            numbers[i] = id;
            bottoms[i] = Math.round(elem.getBoundingClientRect().bottom);
            i++;
        });
        this.pageNumbers = numbers;
        this.pageBottoms = bottoms;
        console.log('current page bottom: ', this.pageBottoms[this.currentPage]);
        console.log('this.bottom: ', this.bottom);
    }

    private getPageBottom = () =>
        this.bottom = window.innerHeight;

    private preventEvent(e: Event) {
        e.preventDefault();
        e.stopPropagation();
    }

    private getScrollDirection = ((event: WheelEvent & { target: Element; }) => {
        let currentPage = this.pages[this.currentPage];
        if (event.deltaY > 0 && this.currentPage < Object.keys(this.pages).length && Math.abs(currentPage.getBoundingClientRect().bottom - this.bottom) < 5) {
            // scroll down
            this.preventEvent(event);
            this.scrollToNextPage();
        } else if (event.deltaY < 0 && this.currentPage > 1 && Math.abs(currentPage.getBoundingClientRect().top) < 5) {
            // scroll up
            this.preventEvent(event);
            this.scrollToPreviousPage();
        } else this.getPageData();
    });

    private onUpOrDownClick = ((event: KeyboardEvent & { target: Element; }) => {
        if (document.activeElement === document.querySelector('body')) {
            let currentPage = this.pages[this.currentPage];
            if (event.keyCode == 40 && this.currentPage < Object.keys(this.pages).length && Math.abs(currentPage.getBoundingClientRect().bottom - this.bottom) < 5) {
                // scroll down
                this.preventEvent(event);
                this.scrollToNextPage();
            } else if (event.keyCode == 38 && this.currentPage > 1 && Math.abs(currentPage.getBoundingClientRect().top) < 5) {
                // scroll up
                this.preventEvent(event);
                this.scrollToPreviousPage();
            } else this.getPageData();
        }
    });

    private scrollToNextPage() {
        let nextPageId = '#' + this.pageNumbers[this.currentPage + 1];
        let nextPage = this.landing.querySelector(`${nextPageId}`);
        nextPage.scrollIntoView({ behavior: 'smooth', });
        this.currentPage += 1;
        this.getPageData();
        console.log('this.currentPage: ', this.currentPage);
    }

    private scrollToPreviousPage() {
        let previousPageId = '#' + this.pageNumbers[this.currentPage - 1];
        let previousPage = this.landing.querySelector(`${previousPageId}`);
        previousPage.scrollIntoView({ behavior: 'smooth', });
        this.currentPage -= 1;
        this.getPageData();
        console.log('this.currentPage: ', this.currentPage);
    }

    public dispose() {
        window.removeEventListener('scroll', this.getScrollDirection);
        window.removeEventListener('keydown', this.onUpOrDownClick);
    }
}

