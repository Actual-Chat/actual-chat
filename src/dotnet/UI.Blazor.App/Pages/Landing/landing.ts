import { debounce } from 'promises';

const LogScope = 'Landing';

export class Landing {
    private blazorRef: DotNet.DotNetObject;
    private readonly landing: HTMLElement;
    private pages: {};
    private pageBottoms: {};
    private pageNumbers: {};
    private bottom: number;
    private currentPageNumber: number = 1;
    private header: HTMLElement;

    static create(landing: HTMLElement, blazorRef: DotNet.DotNetObject): Landing {
        return new Landing(landing, blazorRef);
    }

    constructor(landing: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.landing = landing;
        this.blazorRef = blazorRef;
        this.header = this.landing.querySelector('.landing-header');
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
        this.setHeaderStyle();
    }

    private setHeaderStyle = () => {
        let page = this.currentPageNumber;
        let list = this.header.classList;
        if (page == 1) {
            list.remove('filled');
        } else {
            if (!list.contains('filled')) {
                list.add('filled');
            }
        }
        if (page == 7 || page == 8 || page == 9 || page == 11) {
            list.add('blur-bg');
        } else {
            list.remove('blur-bg');
        }
    }

    private getScrollDirectionThrottled = debounce(
        (event: WheelEvent & { target: Element; }) => this.getScrollDirectionInternal(event), 300, true);

    private getScrollDirectionInternal = ((event: WheelEvent & { target: Element; }) => {
        let page = this.pages[this.currentPageNumber];
        if (event.deltaY > 0) {
            // scroll down
            if ((this.currentPageNumber <= Object.keys(this.pages).length)
                && (page.classList.contains('page-scrolling'))
                && Math.abs(page.getBoundingClientRect().bottom - this.bottom) > 30) {
                this.scrollToPageEnd(page);
            } else if (this.currentPageNumber < Object.keys(this.pages).length) {
                this.scrollToNextPage();
            }
        } else if (event.deltaY < 0) {``
            // scroll up
            if (this.currentPageNumber >= 1
                && page.classList.contains('page-scrolling')
                && Math.abs(page.getBoundingClientRect().top) > 30) {
                this.scrollToPageStart(page);
            } else if (this.currentPageNumber > 1) {
                this.scrollToPreviousPage();
            }
        } else this.getPageData();
    });

    private getScrollDirection = ((event: WheelEvent & { target: Element; }) => {
        this.preventEvent(event);
        this.getScrollDirectionThrottled(event);
    });

    private getDownClickThrottled = debounce(
        (page: HTMLElement) => {
            if (page.classList.contains('page-scrolling')
                && this.currentPageNumber <= Object.keys(this.pages).length
                && Math.abs(page.getBoundingClientRect().bottom - this.bottom) > 100) {
                this.scrollToPageEnd(page);
            } else if (this.currentPageNumber < Object.keys(this.pages).length) {
                this.scrollToNextPage();
            }
        }, 300, true);

    private getUpClickThrottled = debounce(
        (page: HTMLElement) => {
            if (page.classList.contains('page-scrolling')
                && this.currentPageNumber >= 1
                && Math.abs(page.getBoundingClientRect().top) > 100) {
                this.scrollToPageStart(page);
            } else if (this.currentPageNumber > 1) {
                this.scrollToPreviousPage();
            }
        }, 300, true);

    private onUpOrDownClick = ((event: KeyboardEvent & { target: Element; }) => {
        if (document.activeElement === document.querySelector('body')) {
            let page = this.pages[this.currentPageNumber] as HTMLElement;
            if (event.keyCode == 40) {
                // scroll down
                this.preventEvent(event);
                this.getDownClickThrottled(page);
            } else if (event.keyCode == 38) {
                // scroll up
                this.preventEvent(event);
                this.getUpClickThrottled(page);
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
        }, 500);
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
        }, 500);
    }

    private scrollToPageEnd = (page: HTMLElement) => {
        let windowHeight = document.documentElement.clientHeight;
        let pageRect = page.getBoundingClientRect();
        let pageHeight = pageRect.height;
        let pageBottom = pageRect.bottom;
        if (pageHeight > windowHeight * 2 && pageBottom > windowHeight * 2) {
            let pageItems = page.querySelectorAll('.page-item');
            let pageItem: HTMLElement;
            let minDelta = 10000;
            pageItems.forEach(i => {
                let elem = i as HTMLElement;
                let itemTop = elem.getBoundingClientRect().top;
                let itemBottom = elem.getBoundingClientRect().bottom;
                let delta = windowHeight - itemTop;
                if (delta > 0 && delta < minDelta && itemBottom > windowHeight) {
                    minDelta = delta;
                    pageItem = elem;
                }
            })
            pageItem.scrollIntoView({ behavior: 'smooth', block: 'start' })
        } else page.scrollIntoView({ behavior: 'smooth', block: 'end' });
    }

    private scrollToPageStart = (page: HTMLElement) => {
        let windowHeight = document.documentElement.clientHeight;
        let pageRect = page.getBoundingClientRect();
        let pageHeight = pageRect.height;
        let pageTop = pageRect.top;
        if (pageHeight > windowHeight * 2 && Math.abs(pageTop) > windowHeight) {
            let pageItems = page.querySelectorAll('.page-item');
            let pageItem = HTMLElement;
            let minDelta = 10000;
            pageItems.forEach(i => {
                let elem = i as HTMLElement;
                let delta = elem.getBoundingClientRect().bottom;
                console.log('delta: ', delta);
                if (delta > 0 && delta < minDelta) {
                    minDelta = delta;
                    pageItem = elem;
                }
            })
            pageItem.scrollIntoView({ behavior: 'smooth', block: 'end' })
        } else page.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }


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

