const LogScope = 'Landing';

export class Landing {
    private blazorRef: DotNet.DotNetObject;
    private readonly landing: HTMLElement;
    private pages: {};
    private pageBottoms: {};
    private pageNumbers: {};
    private bottom: number;
    private currentPageNumber: number = 1;

    static create(landing: HTMLElement, blazorRef: DotNet.DotNetObject): Landing {
        return new Landing(landing, blazorRef);
    }

    constructor(landing: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.landing = landing;
        this.blazorRef = blazorRef;
        this.bottom = window.innerHeight;
        this.getPageData();
        this.getPageBottom();
        window.addEventListener('wheel', this.getScrollDirection, {passive: false, });
        window.addEventListener('keydown', this.onUpOrDownClick, {passive: false, });

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
    }

    private getScrollDirection = ((event: WheelEvent & { target: Element; }) => {
        let page = this.pages[this.currentPageNumber];
        if (event.deltaY > 0
            && this.currentPageNumber < Object.keys(this.pages).length) {
            // scroll down
            this.preventEvent(event);
            if (page.classList.contains('page-scrolling')
                && Math.abs(page.getBoundingClientRect().bottom - this.bottom) > 100) {
                this.scrollToPageEnd(page);
            } else {
                this.scrollToNextPage();
            }
        } else if (event.deltaY < 0
            && this.currentPageNumber > 1) {
            // scroll up
            this.preventEvent(event);
            if (page.classList.contains('page-scrolling')
                && Math.abs(page.getBoundingClientRect().top) > 100) {
                this.scrollToPageStart(page);
            } else {
                this.scrollToPreviousPage();
            }
        } else this.getPageData();
    });

    private onUpOrDownClick = ((event: KeyboardEvent & { target: Element; }) => {
        if (document.activeElement === document.querySelector('body')) {
            let page = this.pages[this.currentPageNumber] as HTMLElement;
            if (event.keyCode == 40 && this.currentPageNumber < Object.keys(this.pages).length) {
                // scroll down
                this.preventEvent(event);
                if (page.classList.contains('page-scrolling')
                && Math.abs(page.getBoundingClientRect().bottom - this.bottom) > 100) {
                    this.scrollToPageEnd(page);
                } else {
                    this.scrollToNextPage();
                }
            } else if (event.keyCode == 38 && this.currentPageNumber > 1) {
                // scroll up
                this.preventEvent(event);
                if (page.classList.contains('page-scrolling')
                    && Math.abs(page.getBoundingClientRect().top) > 100) {
                    this.scrollToPageStart(page);
                } else {
                    this.scrollToPreviousPage();
                }
            } else this.getPageData();
        }
    });

    private scrollToNextPage() {
        let nextPageId = '#' + this.pageNumbers[this.currentPageNumber + 1];
        let nextPage = this.landing.querySelector(`${nextPageId}`);
        nextPage.scrollIntoView({ behavior: 'smooth', });
        this.currentPageNumber += 1;
        setTimeout(() => {
            this.getPageData();
        }, 1000);
    }

    private scrollToPreviousPage() {
        let previousPageId = '#' + this.pageNumbers[this.currentPageNumber - 1];
        let previousPage = this.landing.querySelector(`${previousPageId}`);
        if (previousPage.classList.contains('page-scrolling')) {
            previousPage.scrollIntoView({ behavior: 'smooth', block: 'end'});
        } else {
            previousPage.scrollIntoView({ behavior: 'smooth', });
        }
        this.currentPageNumber -= 1;
        setTimeout(() => {
            this.getPageData();
        }, 1000);
    }

    private scrollToPageEnd = (page: HTMLElement) =>
        page.scrollIntoView({behavior: 'smooth', block: 'end'});

    private scrollToPageStart = (page: HTMLElement) =>
        page.scrollIntoView({behavior: 'smooth', block: 'start'});


    private getPageBottom = () =>
        this.bottom = window.innerHeight;

    private preventEvent(e: Event) {
        e.preventDefault();
        e.stopPropagation();
    }

    public dispose() {
        window.removeEventListener('scroll', this.getScrollDirection);
        window.removeEventListener('keydown', this.onUpOrDownClick);
    }
}

