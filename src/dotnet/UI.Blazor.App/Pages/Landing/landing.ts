import { debounce } from 'promises';

const LogScope = 'Landing';

enum ScrollBlock {
    start = 'start',
    end = 'end',
}

export class Landing {
    private blazorRef: DotNet.DotNetObject;
    private readonly landing: HTMLElement;
    private pages: {};
    private bottom: number;
    private pageCount: number;
    private currentPageNumber: number = 1;
    private nearestTopPageNumber: number = 1;
    private nearestBottomPageNumber: number = 2;
    private header: HTMLElement;
    readonly menu: HTMLElement;
    private menuObserver : MutationObserver;
    private links: HTMLElement[];

    static create(landing: HTMLElement, blazorRef: DotNet.DotNetObject): Landing {
        return new Landing(landing, blazorRef);
    }

    constructor(landing: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.landing = landing;
        this.blazorRef = blazorRef;
        this.header = this.landing.querySelector('.landing-header');
        this.menu = this.landing.querySelector('.landing-menu');
        this.getInitialData();
        window.addEventListener('keydown', this.keyboardHandler, { passive: false, });
        this.landing.addEventListener('wheel', this.smartScrollThrottled);
        this.menuObserver = new MutationObserver(this.menuStateObserver);
        this.menuObserver.observe(this.menu, {
            attributes: true,
            childList: true,
            subtree: true,
        })
    }

    private getLinks = () => {
        let linksNodes = this.landing.querySelectorAll('.landing-links');
        let links = new Array<HTMLElement>();
        for (let i = 0; i < linksNodes.length; i++) {
            let elem = linksNodes[i] as HTMLElement;
            links.push(elem);
        }
        this.links = links;
    }

    private menuStateObserver = (mutationsList, observer) => {
        for (const mutation of mutationsList) {
            if (mutation.type === 'attributes' && mutation.target.classList.contains('open')) {
                this.landing.addEventListener('click', this.onCloseMenu);
            }
        }
    };

    private onCloseMenu = ((event: Event & { target: Element; }) => {
        if (this.menu.classList.contains('open')) {
            const withinMenu = event.composedPath().includes(this.menu);
            if (!withinMenu) {
                this.blazorRef.invokeMethodAsync('CloseMenu');
                if (this.menu.classList.contains('closed'))
                    this.landing.removeEventListener('click', this.onCloseMenu);
            }
        }
    });

    private keyboardHandler = ((event: KeyboardEvent & { target: Element; }) => {
        if (event.keyCode == 40 || event.keyCode == 38)
            this.smartScrollThrottled(event);
    });

    private smartScrollThrottled = debounce(
                (event: Event & { target: Element; }) => this.smartScroll(event), 300, true);

    private smartScroll = ((event: Event & { target: Element; }) => {
        setTimeout(() => {
            this.scrollHandler(event);
        },200);
    });

    private scrollHandler = (event: Event) => {
        let windowHeight = window.innerHeight;
        let oldBottom = this.bottom;
        this.bottom = this.getRoundValue(this.landing.getBoundingClientRect().bottom);
        let delta = this.getRoundValue(oldBottom - this.bottom);
        let scrollUp = delta < 0;
        let rect = (this.pages[this.currentPageNumber] as HTMLElement).getBoundingClientRect();
        let bottom = this.getRoundValue(rect.bottom);
        let top = this.getRoundValue(rect.top);
        if (Math.abs(delta) <= 200 && delta != 0) {
            if (top != 0 && bottom != windowHeight) {
                if (!scrollUp) {
                    // little scroll down => smart scroll to end of current page or to start of next page
                    let nextPage = this.pages[this.nearestBottomPageNumber] as HTMLElement;
                    setTimeout(() => {
                        if (this.nearestBottomPageNumber == this.currentPageNumber) {
                            let currentPage = this.pages[this.currentPageNumber] as HTMLElement;
                            if (this.getRoundValue(currentPage.getBoundingClientRect().bottom) > windowHeight
                                && this.getRoundValue(currentPage.getBoundingClientRect().top) <= 0) {
                                this.scrollToPage(nextPage, event, ScrollBlock.end);
                            } else {
                                this.scrollToPage(nextPage, event, ScrollBlock.start);
                            }
                        } else {
                            this.scrollToPage(nextPage, event, ScrollBlock.start);
                        }
                    }, 100);
                } else {
                    let previousPage = this.pages[this.nearestTopPageNumber] as HTMLElement;
                    setTimeout(() => {
                        if (this.nearestTopPageNumber == this.currentPageNumber) {
                            let currentPage = this.pages[this.currentPageNumber] as HTMLElement;
                            if (this.getRoundValue(currentPage.getBoundingClientRect().top) < 0
                            && this.getRoundValue(currentPage.getBoundingClientRect().bottom) >= windowHeight) {
                                this.scrollToPage(previousPage, event, ScrollBlock.start);
                            } else {
                                this.scrollToPage(previousPage, event, ScrollBlock.end);

                            }
                        } else {
                            this.scrollToPage(previousPage, event, ScrollBlock.end);
                        }
                    }, 100);
                }
            }
        } else {
            this.getNearestPages();
        }
    }

    private scrollToPage = (page: HTMLElement, event: Event, block: ScrollBlock = ScrollBlock.start) => {
        if (document.body.classList.contains('wide')) {
            this.preventEvent(event);
            page.scrollIntoView({ behavior: 'smooth', block: block, })
        }
        setTimeout(() => {
            this.getPageData();
        }, 500);
    }

    private getNearestPages = () => {
        setTimeout(() => {
            let windowHeight = window.innerHeight;
            for (let i = 1; i <= this.pageCount; i++) {
                let page = this.pages[i] as HTMLElement;
                let pageTop = this.getRoundValue(page.getBoundingClientRect().top);
                let pageBottom = this.getRoundValue(page.getBoundingClientRect().bottom);
                if (pageTop == 0 && pageBottom >= windowHeight) {
                    this.currentPageNumber = i;
                    this.nearestTopPageNumber = i == 1 ? i : i - 1;
                    this.nearestBottomPageNumber = i + 1;
                    if (i == this.pageCount || pageBottom > windowHeight)
                        this.nearestBottomPageNumber = i;
                    break;
                } else if (pageTop > 0 && pageTop < windowHeight) {
                    this.currentPageNumber = i;
                    this.nearestTopPageNumber = i == 1 ? i : i - 1;
                    this.nearestBottomPageNumber = i;
                    break;
                } else if (pageTop < 0 && pageBottom >= windowHeight) {
                    this.currentPageNumber = i;
                    this.nearestTopPageNumber = i;
                    this.nearestBottomPageNumber = i == this.pageCount ? i : i + 1;
                    break;
                }
            }
            this.setHeaderStyle();
        }, 100);
    }

    private getBottom = () => {
        this.bottom = this.getRoundValue(this.landing.getBoundingClientRect().bottom);
    }

    private getRoundValue = (value: number) : number =>
        Math.round(value);

    private getInitialData = () => {
        this.getBottom();
        let pages = this.landing.querySelectorAll('.page');
        this.pageCount = pages.length;
        let i = 1;
        this.pages = {};
        pages.forEach((page: HTMLElement) => {
            this.pages[i] = page;
            i++;
        });
    }

    private getPageData() {
        this.pages = {};
        let pages = this.landing.querySelectorAll('.page');
        let i = 1;
        pages.forEach((elem: HTMLElement) => {
            this.pages[i] = elem;
            i++;
        });
        this.getBottom();
        this.getNearestPages();
    }

    private setHeaderStyle = () => {
        let list = this.header.classList;
        let firstPage = this.pages[1] as HTMLElement;
        let firstPageBottom = firstPage.getBoundingClientRect().bottom;
        if (firstPageBottom <= 0) {
            list.add('filled');
        } else {
            list.remove('filled');
        }
        this.hideOrShowHeader();
    }

    private hideOrShowHeader = () => {
        this.getLinks();
        if (this.links.length == 0)
            return;
        let headerRect = this.header.getBoundingClientRect();
        let i = 1;
        this.links.forEach(item => {
            let linksRect = item.getBoundingClientRect();
            if (linksRect.top <= headerRect.top && linksRect.bottom >= headerRect.bottom) {
                if (!this.header.classList.contains('hide-header')) {
                    setTimeout(() => {
                        this.header.classList.add('hide-header');
                    }, 100);
                    return;
                }
            } else {
                this.header.classList.remove('hide-header');
            }
            i++;
        });
    }

    private preventEvent(e: Event) {
        e.preventDefault();
        e.stopPropagation();
    }

    public dispose() {
        window.removeEventListener('keydown', this.smartScrollThrottled);
        this.landing.removeEventListener('wheel', this.smartScrollThrottled);
        this.landing.removeEventListener('click', this.onCloseMenu);
    }
}

