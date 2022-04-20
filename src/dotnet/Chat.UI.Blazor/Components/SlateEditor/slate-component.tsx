// Import React dependencies.
import React, { useState } from 'react'
// Import the Slate editor factory.
import { createEditor } from 'slate'

// Import the Slate components and React plugin.
import { Slate, Editable, withReact } from 'slate-react'

const initialValue = [
    {
        type: 'paragraph',
        children: [{ text: 'A line of text in a paragraph.' }],
    },
]

export const SlateComponent = () => {
    const [editor] = useState(() => withReact(createEditor()))

    return (
        <Slate editor={editor} value={initialValue}>
            <Editable />
        </Slate>
    )
}
