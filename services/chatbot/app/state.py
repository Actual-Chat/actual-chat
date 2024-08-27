from langgraph.graph import MessagesState
from typing import List, Dict

class State(MessagesState):
    # This allows us to keep conversation length under control
    summary: str
    # Tools utilitary state
    #last_search_result: List[Dict]

