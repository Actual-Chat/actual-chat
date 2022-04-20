import React from "react"

const e = React.createElement;
export class LikeButton extends React.Component {
    constructor(props) {
        super(props);
        this.state = {
            liked: false
        };
    }
    render() {
        if (this.state.liked) {
            return 'You liked this.';
        }
        return e(
            'button', {
                onClick: () => this.setState({
                    liked: true
                })
            },
            'Like'
        );
    }
}
