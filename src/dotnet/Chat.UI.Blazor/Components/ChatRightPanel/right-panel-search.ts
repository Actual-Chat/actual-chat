const debounce = require('lodash/debounce');

export class RightPanelSearch {
    private blazorRef: DotNet.DotNetObject;
    private authorTab: HTMLDivElement;
    private input: HTMLDivElement;
    private searchButton: HTMLButtonElement;

    static create(authorTab: HTMLDivElement, blazorRef: DotNet.DotNetObject): RightPanelSearch {
        return new RightPanelSearch(authorTab, blazorRef);
    }

    constructor(authorTab: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        this.authorTab = authorTab;
        this.blazorRef = blazorRef;
        const inputParent = this.authorTab.querySelector('.search-contacts');
        this.input = inputParent.querySelector('input');
        this.searchButton = this.authorTab.querySelector('.search-contacts').querySelector('button');

        this.input.addEventListener('input', debounce(this.inputListener, 1000));
    }

    private inputListener = ((event: ClipboardEvent & { target: Element; }) => {
        // Need code
    })
}
