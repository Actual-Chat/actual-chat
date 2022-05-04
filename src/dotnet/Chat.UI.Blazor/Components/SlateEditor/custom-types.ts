import {
    Text,
    createEditor,
    Node,
    Element,
    Editor,
    Descendant,
    BaseEditor,
} from 'slate'
import { ReactEditor } from 'slate-react'
import { HistoryEditor } from 'slate-history'

export type MentionElement = {
    type: 'mention'
    mentionId: string
    name: string
    children: Text[]
}

export type ParagraphElement = {
    type: 'paragraph'
    align?: string
    children: Descendant[]
}

type CustomElement =
    | MentionElement
    | ParagraphElement

export type CustomText = {
    bold?: boolean
    italic?: boolean
    code?: boolean
    text: string
}

export type EmptyText = {
    text: string
}

export type CustomEditor = BaseEditor & ReactEditor & HistoryEditor

declare module 'slate' {
    interface CustomTypes {
        Editor: CustomEditor
        Element: CustomElement
        Text: CustomText | EmptyText
    }
}
