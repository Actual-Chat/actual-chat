import * as React from 'react'

export class Square extends React.Component<{value:string},{}> {
    render() {
        return (
            <button className="square" onClick={() => console.log('клик')}>
                {this.props.value}
            </button>
        );
    }
}
