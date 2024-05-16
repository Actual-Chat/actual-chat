export function randomIntFromInterval(min, max): number {
    return Math.floor(Math.random() * (max - min + 1) + min);
}

export enum MessageWidth {
    "w-1" = 1,
    "w-2" = 2,
    "w-3" = 3,
    "w-4" = 4,
    "w-5" = 5,
    "w-6" = 6,
    "w-7" = 7,
    "w-8" = 8,
    "w-9" = 9,
    "w-10" = 10,
}

export enum StringHeight {
    "h-1" = 1,
    "h-2" = 2,
    "h-3" = 3,
    "h-4" = 4,
    "h-5" = 5,
    "h-6" = 6,
}
