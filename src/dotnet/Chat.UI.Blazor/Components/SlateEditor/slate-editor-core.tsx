import React, { useMemo, useCallback, useEffect, useState } from 'react'
import { Editor, Transforms, Range, createEditor, Descendant } from 'slate';
import { withHistory } from 'slate-history'
import {
    Slate,
    Editable,
    ReactEditor,
    withReact,
} from 'slate-react'

import { CustomEditor, CustomText, MentionElement, ParagraphElement } from './custom-types';
import { SlateEditorHandle } from './slate-editor-handle';
import { serialize } from './serializer';

export const createSlateEditorCore = (handle : SlateEditorHandle, debug : boolean) => {
    const [target, setTarget] = useState<Range | undefined>()
    const [placeholder, setPlaceholder] = useState('')
    const [hasContent, setHasContent] = useState(false)
    const renderElement = useCallback(props => <Element {...props} />, [])
    const editor = useMemo(
        () => withMentions(withReact(withHistory(createEditor() as ReactEditor))),
        []
    )

    handle.getText = () => trimLeftSpecial(serialize(editor));

    handle.setMarkup = nodes => {
        for (let node of nodes) {
            switch (node.type) {
                case 'mention':
                    handle.insertMention(node.content, node.displayContent);
                    break;
                case 'paragraph':
                    editor.insertText(node.content);
                    break;
                default:
                    throw new Error(`Unexpected markup node type '${node.type}'`);
            }
        }
    };

    handle.moveCursorToEnd = () => {
        Transforms.deselect(editor);
        Transforms.select(editor, Editor.end(editor, []));
    };

    handle.setPlaceholder = setPlaceholder;

    handle.clearText = () => {
        if (debug) console.log('clear text');
        resetEditor(editor);
    }

    handle.insertMention = (id: string, name: string) => {
        Transforms.select(editor, target);
        insertMention(editor, id, name);
        setTarget(null);
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
                            handle.clearText()
                        }
                        break
                    case 'Escape':
                        if (!event.shiftKey) {
                            event.preventDefault();
                            handle.onCancel();
                        }
                        break;
                    case 'ArrowUp':
                        if (!event.shiftKey) {
                            event.preventDefault();
                            const text = handle.getText();
                            if(!text)
                                handle.onEditLastMessage();
                        }
                        break;
                }
            }
        },
        [target]
    )

    useEffect(() => {
        const search = target ? Editor.string(editor, target).substring(1) : ''
        if (debug) {
            console.log("search: " + search)
            console.log("target: " + (target ? JSON.stringify(target) : 'null'))
        }
        if (target)
            handle.getMention.show(search)
        else
            handle.getMention.close()
    }, [target])

    useEffect(() => {
        if (debug) console.log("hasContent: " + hasContent)
        handle.onHasContentChanged(hasContent)
    }, [hasContent])

    useEffect(() => {
        handle.onRendered();
    }, [])

    return (
        <Slate
            editor={editor}
            value={editorEmptyValue}
            onChange={() => {
                const hasContent1 = calculateHasContent(editor.children)
                setHasContent(hasContent1)
                const target1 = calculateMentionTarget(editor, debug)
                setTarget(target1)
            }}
        >
            <Editable
                renderElement={renderElement}
                onKeyDown={onKeyDown}
                placeholder={placeholder}
            />
        </Slate>
    )
}

export interface MarkupNode {
    type: 'mention' | 'paragraph';
    content: string;
    displayContent: string;
}

const trimLeftSpecial = (str : string) : string => {
    if (str.length===0)
        return str;
    // after first character is typed, char with code 65279 is added in the start of the string
    // so let's remove it
    if (str.charCodeAt(0)===65279)
        return str.substring(1);
    return str;
}

const resetEditor = (editor: CustomEditor) => {
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
}

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

const calculateMentionTarget = (editor : CustomEditor, debug : boolean) : Range => {
    const { selection } = editor
    if (selection && Range.isCollapsed(selection)) {
        const [cursorPosition] = Range.edges(selection)
        // get a range that ends at cursor position and starts at nearest word boundary
        const wordBefore = Editor.before(editor, cursorPosition, { unit: 'word' })
        const wordBeforeRange = wordBefore && Editor.range(editor, wordBefore, cursorPosition)
        const wordBeforeText = wordBeforeRange && Editor.string(editor, wordBeforeRange)

        let targetCandidate: Range | undefined = undefined

        if (wordBeforeText) {
            if (debug) console.log("wordBeforeText: " + wordBeforeText)

            if (wordBeforeText && wordBeforeText.endsWith('@')) {
                // cursor is right after '@' character
                // check if there is a space or nothing before it
                const beforeMatch = wordBeforeText.match(/(^|\s)@$/)

                if (beforeMatch) {
                    const before1 = Editor.before(editor, cursorPosition)
                    targetCandidate = Editor.range(editor, before1, cursorPosition)
                }
            } else {
                // try to take 2 character before word start boundary
                const before1 = Editor.before(editor, wordBefore)
                const before2 = before1 && Editor.before(editor, before1)
                const before = before2 || before1 || wordBefore;

                const beforeTextRange = Editor.range(editor, before, cursorPosition)
                const beforeText = beforeTextRange && Editor.string(editor, beforeTextRange)
                const beforeMatch = beforeText && beforeText.match(/(^|\s)@(\w*)$/)

                if (beforeMatch) {
                    targetCandidate = Editor.range(editor, before1, cursorPosition)
                }
            }
        }

        if (targetCandidate) {
            // check if there is a space or nothing after cursor
            const after = Editor.after(editor, cursorPosition)
            const afterRange = Editor.range(editor, cursorPosition, after)
            const afterText = Editor.string(editor, afterRange)
            const afterMatch = afterText.match(/^(\s|$)/)

            if (afterMatch) {
                return targetCandidate
            }
        }
    }

    return null
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
    return (
        <span
            {...attributes}
            contentEditable={false}
            className="mention"
            data-cy={`mention-${element.mentionId}`}
        >@{element.name}
      {children}
    </span>
    )
}

const editorEmptyValue: Descendant[] = [{
    type: 'paragraph',
    children: [{ text: '' }],
}]
