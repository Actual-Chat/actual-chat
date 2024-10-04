from pydantic import BaseModel
from typing import Annotated, Optional
from langchain_core.messages import AnyMessage
from langgraph.graph import add_messages

class State(BaseModel):
    messages: Annotated[list[AnyMessage], add_messages]

    summary: Optional[str] = None
    search_type: Optional[str] = None
    last_seen_msg_id: Optional[str] = None
    last_search_result: Optional[str] = None
