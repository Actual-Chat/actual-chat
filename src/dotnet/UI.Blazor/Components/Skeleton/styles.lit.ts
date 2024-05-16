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
        background-color: var(--skeleton);
    }
    .message-skeleton.header-skeleton .message-avatar {
        width: 2.5rem;
        height: 2.5rem;
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
        height: 0.75rem;
        opacity: 100%;
        background-color: var(--skeleton);
        border-radius: 0.375rem;
    }
    .message-skeleton.header-skeleton .title.message {
        width: 20rem;
    }
    .message-skeleton .message {
        height: 0.75rem;
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
    .string-skeleton-wrapper {
        display: flex;
        width: 100%;
    }
    .string-skeleton-wrapper.system-string {
        justify-content: center;
        align-items: center;
        margin: 1px 0;
    }
    .string-skeleton {
        background-color: var(--skeleton);
        border-radius: 0.5rem;
        animation: pulse 2s infinite;
    }
    .header-skeleton,
    .animated-skeleton.round-skeleton {
        animation: pulse 2s infinite;
    }
    .round-skeleton {
        flex: none;
        background-color: var(--skeleton);
        border-radius: 9999px;
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

    .round-skeleton.radius-8 {
        width: 2rem;
        height: 2rem;
    }
    .round-skeleton.radius-10 {
        width: 2.5rem;
        height: 2.5rem;
    }
    .round-skeleton.radius-12 {
        width: 3rem;
        height: 3rem;
    }
    .round-skeleton.radius-16 {
        width: 4rem;
        height: 4rem;
    }

    .message.h-1 {
        height: 0.25rem;
    }
    .message.h-2 {
        height: 0.5rem;
    }
    .message.h-3 {
        height: 0.75rem;
    }
    .message.h-4 {
        height: 1rem;
    }
    .message.h-5 {
        height: 1.25rem;
    }
    .message.h-6 {
        height: 1.5rem;
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
