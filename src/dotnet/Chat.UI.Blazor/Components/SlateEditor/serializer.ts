// created by example from https://docs.slatejs.org/concepts/10-serializing
import { Text } from 'slate';

export const serialize = node => {
    if (Text.isText(node)) {
        return node.text
    }

    let children : string = ''
    let i = 0;
    for (const child of node.children) {
        const child = node.children[i];
        const isLast = i === node.children.length - 1;
        const s = serialize(child)
        children += s;
        if (child.type == 'paragraph' && !isLast)
            children += '\n'
        i++
    }

    switch (node.type) {
        case 'mention':
            return `@${node.mentionId}`
        case 'paragraph':
            return children
        default:
            return children
    }
}
