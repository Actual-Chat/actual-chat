export function randomIntFromInterval(min, max): number {
    return Math.floor(Math.random() * (max - min + 1) + min);
}

export enum MessageWidth {
    "width-1" = 1,
    "width-2" = 2,
    "width-3" = 3,
    "width-4" = 4,
    "width-5" = 5,
    "width-6" = 6,
    "width-7" = 7,
    "width-8" = 8,
    "width-9" = 9,
    "width-10" = 10,
}

export enum StringHeight {
    "height-1" = 1,
    "height-2" = 2,
    "height-3" = 3,
    "height-4" = 4,
    "height-5" = 5,
    "height-6" = 6,
    "height-7" = 7,
    "height-8" = 8,
    "height-9" = 9,
    "height-10" = 10,
}

export enum HeightAndWidth {
    "width-1 height-1" = 1,
    "width-2 height-2" = 2,
    "width-3 height-3" = 3,
    "width-4 height-4" = 4,
    "width-5 height-5" = 5,
    "width-6 height-6" = 6,
    "width-7 height-7" = 7,
    "width-8 height-8" = 8,
    "width-9 height-9" = 9,
    "width-10 height-10" = 10,
    "width-11 height-11" = 11,
    "width-12 height-12" = 12,
}
