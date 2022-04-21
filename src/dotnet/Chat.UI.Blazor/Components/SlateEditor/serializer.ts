// created by example from https://docs.slatejs.org/concepts/10-serializing
import { Text } from 'slate'

export const serialize = node => {
    if (Text.isText(node)) {
        return node.text
    }

    const children = node.children.map(n => serialize(n)).join('')

    switch (node.type) {
        case 'mention':
            return `<@${node.character}>`
        case 'paragraph':
            return (children + '\n')
        default:
            return children
    }
}
