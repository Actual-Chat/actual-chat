import React, { useMemo, useCallback, useRef, useEffect, useState } from 'react'
import { Editor, Transforms, Range, createEditor, Descendant } from 'slate'
import { withHistory } from 'slate-history'
import {
    Slate,
    Editable,
    ReactEditor,
    withReact,
    useSelected,
    useFocused,
} from 'slate-react'

import { Portal } from './components'
import { MentionElement } from './custom-types'
import { SlateEditorHandle } from './slate-editor-handle';
import { serialize } from './serializer';

export const MentionExample = (handle : SlateEditorHandle) => {
    const [target, setTarget] = useState<Range | undefined>()
    const [search, setSearch] = useState('')
    const renderElement = useCallback(props => <Element {...props} />, [])
    const editor = useMemo(
        () => withMentions(withReact(withHistory(createEditor() as ReactEditor))),
        []
    )

    handle.getText = () => serialize(editor)

    const resetEditor = () => {
        console.log('reset editor started')

        Transforms.delete(editor, {
            at: {
                anchor: Editor.start(editor, []),
                focus: Editor.end(editor, []),
            },
        });

        console.log('reset editor completed')
    }

    handle.clearText = () => {
        console.log('clear text')
        resetEditor()
    }

    handle.insertMention = (mention : any) => {
        const { id, name } = mention;
        Transforms.select(editor, target)
        insertMention(editor, name)
        setTarget(null)
    }

    const onKeyDown = useCallback(
        event => {
            if (target) {
                switch (event.key) {
                    case 'ArrowDown':
                        event.preventDefault()
                        handle.getMention.moveDown();
                        break
                    case 'ArrowUp':
                        event.preventDefault()
                        handle.getMention.moveUp();
                        break
                    case 'Tab':
                    case 'Enter':
                        event.preventDefault()
                        handle.getMention.insert();
                        break
                    case 'Escape':
                        event.preventDefault()
                        setTarget(null)
                        break
                }
            }
            else {
                switch (event.key) {
                    case 'Enter':
                        if (!event.shiftKey) {
                            event.preventDefault()
                            handle.onPost(handle.getText())
                        }
                        break
                }
            }
        },
        [search, target]
    )

    const onMagicButtonClick = () => {
      console.log('magic button clicked')
      resetEditor()
    }

    useEffect(() => {
        console.log("search: " + search)
        console.log("target: " + target)
        if (target)
            handle.getMention.show(search)
        else
            handle.getMention.close()
    }, [search, target])

    return (
        <Slate
            editor={editor}
            value={editorInitialValue}
            onChange={() => {
                const { selection } = editor

                if (selection && Range.isCollapsed(selection)) {
                    const [start] = Range.edges(selection)
                    const wordBefore = Editor.before(editor, start, { unit: 'word' })
                    const before2 = wordBefore && Editor.before(editor, wordBefore, { distance: 2 })
                    const before2Range = before2 && Editor.range(editor, before2, start)
                    const beforeText = before2Range && Editor.string(editor, before2Range)
                    const beforeMatch = beforeText && beforeText.match(/(^|\s)@(\w*)$/)
                    const after = Editor.after(editor, start)
                    const afterRange = Editor.range(editor, start, after)
                    const afterText = Editor.string(editor, afterRange)
                    const afterMatch = afterText.match(/^(\s|$)/)

                    if (beforeMatch && afterMatch) {
                        const before1 = Editor.before(editor, wordBefore)
                        const before1Range = Editor.range(editor, before1, start)
                        setSearch(beforeMatch[2])
                        setTarget(before1Range)
                        return
                    }
                }

                setTarget(null)
            }}
        >
            {/*Test button*/}
            {/*<div>*/}
            {/*    <button onClick={onMagicButtonClick} style={{background: "red"}}>Does Magic!</button>*/}
            {/*</div>*/}
            <Editable
                renderElement={renderElement}
                onKeyDown={onKeyDown}
                placeholder="Enter some text..."
            />
        </Slate>
    )
}

const withMentions = editor => {
    const { isInline, isVoid } = editor

    editor.isInline = element => {
        return element.type === 'mention' ? true : isInline(element)
    }

    editor.isVoid = element => {
        return element.type === 'mention' ? true : isVoid(element)
    }

    return editor
}

const insertMention = (editor, character) => {
    const mention: MentionElement = {
        type: 'mention',
        character,
        children: [{ text: '' }],
    }
    Transforms.insertNodes(editor, mention)
    Transforms.move(editor)
}

const Element = props => {
    const { attributes, children, element } = props
    switch (element.type) {
        case 'mention':
            return <Mention {...props} />
        default:
            return <p {...attributes}>{children}</p>
    }
}

const Mention = ({ attributes, children, element }) => {
    const selected = useSelected()
    const focused = useFocused()
    return (
        <span
            {...attributes}
            contentEditable={false}
            data-cy={`mention-${element.character.replace(' ', '-')}`}
            style={{
                padding: '3px 3px 2px',
                margin: '0 1px',
                verticalAlign: 'baseline',
                display: 'inline-block',
                borderRadius: '4px',
                backgroundColor: '#eee',
                fontSize: '0.9em',
                boxShadow: selected && focused ? '0 0 0 2px #B4D5FF' : 'none',
            }}
        >
      @{element.character}
            {children}
    </span>
    )
}

const editorEmptyValue: Descendant[] = [{
    type: 'paragraph',
    children: [{ text: '' }],
}]

const editorInitialValue: Descendant[] = [
    {
        type: 'paragraph',
        children: [
            {
                text:
                    'This example shows how you might implement a simple @-mentions feature that lets users autocomplete mentioning a user by their username. Which, in this case means Star Wars characters. The mentions are rendered as void inline elements inside the document.',
            },
        ],
    },
    {
        type: 'paragraph',
        children: [
            { text: 'Try mentioning characters, like ' },
            {
                type: 'mention',
                character: 'R2-D2',
                children: [{ text: '' }],
            },
            { text: ' or ' },
            {
                type: 'mention',
                character: 'Mace Windu',
                children: [{ text: '' }],
            },
            { text: '!' },
        ],
    },
]
