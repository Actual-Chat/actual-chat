from typing import Annotated
from typing import List, Any
from langchain_core.tools import tool
from langgraph.graph import MessagesState
from langgraph.prebuilt import InjectedState
from langchain_core.runnables.config import RunnableConfig
from datetime import date as _date
from datetime import datetime
import requests
import json

TOOLS_AUTH_FORWARD_CONTEXT = "forward-auth-context"

class _Tools(object):
    REPLY = None
    FORWARD_CHAT_LINKS = None
    SEARCH_PUBLIC_CHATS = None
    LIST_CHATS = None
    READ_CHAT_LAST_MESSAGES = None

    @classmethod
    def init(cls, *, base_url):
        cls.REPLY = base_url + "/api/bot/conversation/reply"
        cls.FORWARD_CHAT_LINKS = base_url + "/api/bot/conversation/forward-chat-links"
        cls.SEARCH_PUBLIC_CHATS = base_url + "/api/bot/search/public-chats"
        cls.LIST_CHATS = base_url + "/api/bot/chat/list"
        cls.READ_CHAT_LAST_MESSAGES = base_url + "/api/bot/chat/read-last-messages"

@tool(parse_docstring=True)
def list_my_chats(
    config: RunnableConfig
) -> List[Any]:
    """Lists chats this bot can have access to.
    This tool is used together with read_chat tool to read chat updates in real time.

    Args:

    Returns:
        List: chat identifiers
    """
    results = _post(
        _Tools.LIST_CHATS,
        {},
        config
    )

    return results

@tool(parse_docstring=True)
def read_chat(
    chat_id: str,
    limit_messages_count: int,
    config: RunnableConfig
) -> List[Any]:
    """Reads last messages in a chat.

    Args:
        chat_id: chat identifier.
        limit_messages_count: limits the number of messages returned.

    Returns:
        List: Messages in the chat.
    """
    results = _post(
        _Tools.READ_CHAT_LAST_MESSAGES,
        {
            "ChatId": chat_id,
            "Limit": limit_messages_count
        },
        config
    )

    return results

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
def search_in_public_chats(
    text: str,
    config: RunnableConfig
) -> List[Any]:
    """Search in all public chats.

    Args:
        text: Text to search for.

    Returns:
        List: ranked search results.
    """
    results = _post(
        _Tools.SEARCH_PUBLIC_CHATS,
        {
            "text": text
        },
        config
    )

    # Note: For some reason if results are formatted into a plain text
    # the agent doesn't want to send relevand search results to the user.
    # return text_results
    return results

def filter_last_search_in_public_chats_results(state):
    # Note:
    # Messages in the state have different types.
    # Some of them are Dict type and some objects.
    # They also have different properties
    def _read(message, key):
        try:
            return getattr(message, key)
        except:
            pass
        try:
            return message[key]
        except:
            return None
    last_tool_invocation_id = None
    last_tool_invocation_results = state.get("last_search_result", [])
    for past_message in state["messages"]:
        tool_calls = _read(past_message, "tool_calls")
        if not tool_calls:
            continue
        for tool_invocation in tool_calls:
            tool_name = _read(tool_invocation, "name")
            if tool_name != search_in_public_chats.name:
                continue
            last_tool_invocation_id = tool_invocation["id"]
    if not last_tool_invocation_id:
        return last_tool_invocation_results
    for past_message in state["messages"]:
        tool_call_id = _read(past_message, "tool_call_id")
        if tool_call_id != last_tool_invocation_id:
            continue
        last_tool_invocation_results = _read(past_message, "content")
        break
    # Note: Apparently tool invocation results are serialized into strings.
    # And it seems to be a correctly serialized json object.
    if last_tool_invocation_results is not None:
        last_tool_invocation_results = json.loads(last_tool_invocation_results)
    else:
        last_tool_invocation_results = []
    return last_tool_invocation_results

@tool(parse_docstring=True)
def forward_search_results(
    comment: str,
    state: Annotated[MessagesState, InjectedState],
    config: RunnableConfig
):
    """
    Forward last search results to the user with a comment.
    This method is useful when you find the last search results are relevant
    and they should be sent to the user.

    Args:
        comment: A comment to add along with the search results.
    """
    last_tool_invocation_results = filter_last_search_in_public_chats_results(state)
    if not last_tool_invocation_results:
        raise Exception("Can not forward last search result. It could be that the last search_in_public_chats tool call was not successfull or returned an empty result.")

    links = []
    for result in last_tool_invocation_results:
        link = result.get("link", None)
        if link is None:
            continue
        links.append(link)

    _post(
        _Tools.FORWARD_CHAT_LINKS,
        {
            "comment": comment,
            "links": links
        },
        config
    )
    return

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


def all():
    return [
        reply,
        search_in_public_chats,
        forward_search_results,
        list_my_chats,
        read_chat
    ]
