import {css} from "lit";

import '../../../../nodejs/styles/index.css';

export const messageStyles = css`
    .message-skeleton {
        display: flex;
        flex-direction: row;
        column-gap: 0.25rem;
    }
    .animated-skeleton.message-skeleton {
        animation: pulse 2s infinite;
    }
    .message-avatar-wrapper {
        display: flex;
        flex: none;
        align-items: center;
        justify-content: center;
        width: 2.5rem;
        height: 2.5rem;
        margin: 0.5rem;
    }
    .message-avatar {
        width: 2.25rem;
        height: 2.25rem;
        border-radius: 9999px;
        background-color: var(--background-04);
    }
    .message-skeleton .c-container {
        display: flex;
        flex-direction: column;
        align-items: start;
        justify-content: center;
        row-gap: 0.25rem;
        width: 100%;
    }
    .message-skeleton .title.message {
        height: 1rem;
        opacity: 100%;
        background-color: var(--skeleton);
        border-radius: 0.375rem;
    }
    .message-skeleton .message {
        height: 0.875rem;
        background-color: var(--skeleton);
        border-radius: 0.375rem;
    }
    .message-list {
        display: flex;
        flex-direction: column;
        column-gap: 0.25rem;
        margin-bottom: 0.5rem;
    }
    .animated-skeleton.message-list {
        animation: pulse 2s infinite;
    }
    .message-wrapper {
        display: flex;
        flex-direction: flex-row;
        flex-wrap: wrap;
        align-items: center;
        row-gap: 0.5rem;
        padding: 0.25rem 3.25rem 0.25rem 3.75rem;
    }
    .message {
        display: flex;
        height: 0.875rem;
        background-color: var(--skeleton);
        opacity: 75%;
        border-radius: 0.375rem;
    }
    .message.w-1 {
        width: 10%;
    }
    .message.w-2 {
        width: 20%;
    }
    .message.w-3 {
        width: 30%;
    }
    .message.w-4 {
        width: 40%;
    }
    .message.w-5 {
        width: 50%;
    }
    .message.w-6 {
        width: 60%;
    }
    .message.w-7 {
        width: 70%;
    }
    .message.w-8 {
        width: 80%;
    }
    .message.w-9 {
        width: 90%;
    }
    .message.w-10 {
        width: 100%;
    }

    @keyframes pulse {
      0%, 100% {
        opacity: 1;
      }
      50% {
        opacity: .5;
      }
    }
  `;
