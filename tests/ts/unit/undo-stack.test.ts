import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { UndoStack } from '../../../src/dotnet/UI.Blazor.App/Components/MarkupEditor/undo-stack';

describe('UndoStack', () => {
    let text: string;
    let stack: UndoStack<string>;

    const edit = (value: string) => {
        text = value;
        stack.pushThrottled();
        vi.advanceTimersByTime(80);
    };

    beforeEach(() => {
        vi.useFakeTimers();
        text = '';
        stack = new UndoStack<string>(() => text, (value: string) => { text = value; }, (x, y) => x === y, 333);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('undo should restore everything a backspace burst deleted', () => {
        // arrange
        for (const value of ['h', 'he', 'hel', 'hell', 'hello'])
            edit(value);
        vi.advanceTimersByTime(1000);
        for (const value of ['hell', 'hel', 'he'])
            edit(value);

        // act
        stack.undo();

        // assert
        expect(text, 'the first undo must bring back everything the burst deleted').toBe('hello');
    });

    it('undo should not stop right after the first typed character', () => {
        // arrange
        for (const value of ['h', 'he', 'hel'])
            edit(value);

        // act
        stack.undo();

        // assert
        expect(text).toBe('');
    });

    it('redo should return to the state before undo', () => {
        // arrange
        for (const value of ['h', 'he', 'hel'])
            edit(value);
        stack.undo();

        // act
        stack.redo();

        // assert
        expect(text).toBe('hel');
    });
});
