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
import { CustomEditor, CustomText, MentionElement, ParagraphElement } from './custom-types';
import { SlateEditorHandle } from './slate-editor-handle';
import { serialize } from './serializer';

export const MentionExample = (handle : SlateEditorHandle, debug : boolean) => {
    const [target, setTarget] = useState<Range | undefined>()
    const [search, setSearch] = useState('')
    const [hasContent, setHasContent] = useState(false)
    const renderElement = useCallback(props => <Element {...props} />, [])
    const editor = useMemo(
        () => withMentions(withReact(withHistory(createEditor() as ReactEditor))),
        []
    )

    const trimLeftSpecial = (str : string) : string => {
        if (str.length===0)
            return str;
        // after first character is typed, char with code 65279 is added in the start of the string
        // so let's remove it
        if (str.charCodeAt(0)===65279)
            return str.substring(1);
        return str;
    }

    handle.getText = () => trimLeftSpecial(serialize(editor));

    const resetEditor = () => {
        if (debug) console.log('reset editor started')

        Transforms.delete(editor, {
            at: {
                anchor: Editor.start(editor, []),
                focus: Editor.end(editor, []),
            },
        });
        editor.history = {
            undos: [],
            redos: []
        };

        if (debug) console.log('reset editor completed')
    }

    handle.clearText = () => {
        if (debug) console.log('clear text')
        resetEditor()
    }

    handle.insertMention = (mention : any) => {
        const { id, name } = mention;
        Transforms.select(editor, target)
        insertMention(editor, id, name)
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
        if (debug) {
            console.log("search: " + search)
            console.log("target: " + (target ? JSON.stringify(target) : 'null'))
        }
        if (target)
            handle.getMention.show(search)
        else
            handle.getMention.close()
    }, [search, target])

    useEffect(() => {
        if (debug) console.log("hasContent: " + hasContent)
        handle.onHasContentChanged(hasContent)
    }, [hasContent])

    const calculateHasContent = (children : Descendant[]) : boolean =>
    {
        if (!children || children.length===0)
            return false;
        if (children.length > 1)
            return true;
        const paragraph = children[0] as ParagraphElement;
        if (!paragraph)
            return true;
        const children2 = paragraph.children;
        if (!children2 || children2.length===0)
            return false;
        if (children2.length > 1)
            return true;
        const text = children2[0] as CustomText;
        if (!text)
            return false;
        if (text.text.length > 1)
            return true;
        return trimLeftSpecial(text.text).length > 0;
    }

    return (
        <Slate
            editor={editor}
            value={editorEmptyValue}
            onChange={() => {
                const { selection, children } = editor

                setHasContent(calculateHasContent(children))

                if (selection && Range.isCollapsed(selection)) {
                    const [start] = Range.edges(selection)
                    const wordBefore = Editor.before(editor, start, { unit: 'word' })
                    const wordBeforeRange = wordBefore && Editor.range(editor, wordBefore, start)
                    const wordBeforeText = wordBeforeRange && Editor.string(editor, wordBeforeRange)
                    if (debug && wordBeforeText) {
                        console.log("wordBeforeText: " + wordBeforeText)
                    }
                    let searchCandidate = undefined
                    let targetCandidate = undefined
                    if (wordBeforeText && wordBeforeText.includes('@')) {
                        if (wordBeforeText.length == 1) {
                            searchCandidate = ''
                            targetCandidate = wordBeforeRange
                        }
                        else {
                            const before1 = wordBefore && Editor.before(editor, start)
                            const before1Range = before1 && Editor.range(editor, before1, start)
                            const before2 = before1 && Editor.before(editor, before1)
                            const beforeTextRange = before2
                                                    ? Editor.range(editor, before2, start)
                                                    :before1Range;
                            const beforeText = beforeTextRange && Editor.string(editor, beforeTextRange)
                            const beforeMatch = beforeText && beforeText.match(/(^|\s)@$/)

                            if (beforeMatch) {
                                searchCandidate = ''
                                targetCandidate = before1Range
                            }
                        }
                    }
                    else {
                        const before1 = wordBefore && Editor.before(editor, wordBefore)
                        const before1Range = before1
                                             ? Editor.range(editor, before1, start)
                                             : wordBefore && Editor.range(editor, wordBefore, start)
                        const before2 = before1 && Editor.before(editor, before1)
                        const beforeTextRange = before2
                                                ? Editor.range(editor, before2, start)
                                                :before1Range;
                        const beforeText = beforeTextRange && Editor.string(editor, beforeTextRange)
                        const beforeMatch = beforeText && beforeText.match(/(^|\s)@(\w*)$/)

                        if (beforeMatch) {
                            searchCandidate = beforeMatch[2]
                            targetCandidate = before1Range
                        }
                    }

                    const after = Editor.after(editor, start)
                    const afterRange = Editor.range(editor, start, after)
                    const afterText = Editor.string(editor, afterRange)
                    const afterMatch = afterText.match(/^(\s|$)/)

                    if (searchCandidate !== undefined && targetCandidate && afterMatch) {
                        setSearch(searchCandidate)
                        setTarget(targetCandidate)
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

const withMentions = (editor : CustomEditor) : CustomEditor  => {
    const { isInline, isVoid } = editor

    editor.isInline = element => {
        return element.type === 'mention' ? true : isInline(element)
    }

    editor.isVoid = element => {
        return element.type === 'mention' ? true : isVoid(element)
    }

    return editor
}

const insertMention = (editor : CustomEditor, id : string, name : string) => {
    const mention: MentionElement = {
        type: 'mention',
        mentionId: id,
        name: name,
        children: [{ text: '' }],
    }
    const space : CustomText = {
        text: ' '
    }
    Transforms.insertNodes(editor, [mention, space])
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
            data-cy={`mention-${element.mentionId}`}
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
        >@{element.name}
      {children}
    </span>
    )
}

const editorEmptyValue: Descendant[] = [{
    type: 'paragraph',
    children: [{ text: '' }],
}]
