from langgraph.graph import MessagesState
from typing import List, Dict, Any, Annotated

def _update(left, right):
    if right is None or len(right) == 0:
        return left
    else:
        return right


class State(MessagesState):
    # This allows us to keep conversation length under control
    summary: str
    # Tools utilitary state
    last_search_result: Annotated[List[Any], _update]

