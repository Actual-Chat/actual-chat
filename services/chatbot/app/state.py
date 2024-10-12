from pydantic import BaseModel
from typing import Annotated, Optional, Union
from langchain_core.messages import (
    AnyMessage,
    RemoveMessage,
    MessageLikeRepresentation
)
from langgraph.graph import add_messages

Messages = Union[list[MessageLikeRepresentation], MessageLikeRepresentation]

def reduce_list(left: Messages, right: Messages) -> Messages:
    if isinstance(left, list) and len(left) > 0 and not left[-1].content:
        # Remove empty final message by AI agent from it previous response
        remove_message = RemoveMessage(id=left[-1].id)
        right = right + [remove_message] if isinstance(right, list) else [right, remove_message]

    return add_messages(left, right)

class State(BaseModel):
    messages: Annotated[list[AnyMessage], reduce_list]

    summary: Optional[str] = None
    search_type: Optional[str] = None
    last_seen_msg_id: Optional[str] = None
    last_search_result: Optional[str] = None

    def clear(self):
        self.summary = None
        self.search_type = None
        self.last_search_result = None
        # Do not clear last_seen_msg_id
