from enum import StrEnum, auto
from itertools import takewhile

from typing import Annotated, List, Any, Literal
from langchain_core.tools import tool
from langgraph.prebuilt import InjectedState
from langchain_core.messages import ToolMessage
from langchain_core.runnables.config import RunnableConfig
from langchain_core.language_models.chat_models import BaseChatModel


import requests
import json

import sys
sys.path.insert(1, '/app/services/chatbot')

from app.state import State
from app.tools.resolver import SearchTypeResolver

TOOLS_AUTH_FORWARD_CONTEXT = "forward-auth-context"

class ToolNames(StrEnum):
    ResolveSearchType = auto()

class _Tools(object):
    REPLY = None
    FORWARD_CHAT_LINKS = None
    SEARCH_IN_CHATS = None

    @classmethod
    def init(cls, *, base_url):
        cls.REPLY = base_url + "/api/bot/conversation/reply"
        cls.FORWARD_CHAT_LINKS = base_url + "/api/bot/conversation/forward-chat-links"
        cls.SEARCH_IN_CHATS = base_url + "/api/bot/search/chats"

@tool(parse_docstring=True)
def reply(
    message: str,
    # Note: This is a cool new feature in the tools:
    # it is possible to pass configs now and it works out of the box.
    # Config arg should not be added to docstring,
    # as we don't want it to be included in the function
    # signature attached to the LLM.
    config: RunnableConfig
):
    """Send a message to the user.

    Args:
        message: A message to send.
    """
    _ = _reply(message, config)
    return

@tool(parse_docstring=True)
def search_in_chats(
    text: str,
    search_type: Literal["PUBLIC", "PRIVATE", "GENERAL"],
    config: RunnableConfig
) -> List[Any]:
    """Search in all public chats.

    Args:
        text: Text to search for.
        search_type: Identifies type of the search to run. Possible values are PUBLIC, PRIVATE, or GENERAL

    Returns:
        List: ranked search results.
    """
    # search_type_value = "Public" if search_type=="PUBLIC" else "Private" if search_type=="PRIVATE" else "General"
    search_type_value = 1 if search_type=="PUBLIC" else 2 if search_type=="PRIVATE" else 3
    results = _post(
        _Tools.SEARCH_IN_CHATS,
        {
            "text": text,
            "searchType": search_type_value
        },
        config
    )

    # Note: For some reason if results are formatted into a plain text
    # the agent doesn't want to send relevand search results to the user.
    # return text_results
    return results

def get_last_search_results(state: State) -> List[Any]:
    for message in reversed(state.messages):
        if isinstance(message, ToolMessage) and message.name==search_in_chats.name:
            if message.status=="success":
                state.last_search_result = message.content
            else:
                state.last_search_result = ""
            break

    return json.loads(state.last_search_result) if state.last_search_result else []

@tool(parse_docstring=True)
def forward_search_results(
    comment: str,
    state: Annotated[State, InjectedState],
    config: RunnableConfig
):
    """
    Forward last search results to the user with a comment.
    This method is useful when you find the last search results are relevant
    and they should be sent to the user.

    Args:
        comment: A comment to add along with the search results.
    """
    last_search_results = get_last_search_results(state)
    if not last_search_results:
        raise Exception("Can not forward last search result. It could be that the last search_in_public_chats tool call was not successfull or returned an empty result.")

    links = [link for link in map(lambda result: result.get("link", None), last_search_results) if link is not None]

    _post(
        _Tools.FORWARD_CHAT_LINKS,
        {
            "comment": comment,
            "links": links
        },
        config
    )
    return

def save_tool_results_to_state(state: State) -> State:
    stop_id = state.last_seen_msg_id
    tool_messages: list[ToolMessage] = [
        msg for msg in takewhile(lambda m: m.id != stop_id, reversed(state.messages)) if isinstance(msg, ToolMessage)
    ]

    while tool_messages:
        message = tool_messages.pop()
        SearchTypeResolver.try_update_state(state, message, ToolNames.ResolveSearchType)

    if state.messages:
        state.last_seen_msg_id = state.messages[-1].id

    return state

def _reply(message, config):
    _result = _post(
        _Tools.REPLY,
        {
            "text": message
        },
        config
    )
    return _result

def _post(url, data, config: RunnableConfig):
    if (config is None):
        config = {}
    config = config.get("configurable", {})
    auth_context = config.get(TOOLS_AUTH_FORWARD_CONTEXT, None)
    headers = {
        "Authorization": auth_context
    }

    result = requests.post(
        url,
        json = data,
        headers = headers,
        verify = False # TODO: think again if needed.
    )
    result.raise_for_status()
    if not result.content:
        return {}
    return result.json()


def all(*, classifier_model: BaseChatModel):

    search_type_resolver = SearchTypeResolver(classifier_model, ToolNames.ResolveSearchType)

    @tool(ToolNames.ResolveSearchType)
    def resolve_search_type(state: Annotated[State, InjectedState]) -> str:
        """Call to get the search type."""
        return search_type_resolver.process(state)

    return [
        reply,
        search_in_chats,
        forward_search_results,
        resolve_search_type
    ]
