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
    REPLY = "https://local.actual.chat/api/bot/conversation/reply"
    FORWARD_CHAT_LINKS = "https://local.actual.chat/api/bot/conversation/forward-chat-links"
    SEARCH_PUBLIC_CHATS = "https://local.actual.chat/api/bot/search/public-chats"

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
    return _reply(message, config)


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
    # Note:
    # Inital attempt were to save the last call to the search function
    # in the state. However it appeared that many prebuilt classes
    # return MessagesState like dictionary. Therefore the state were lost.
    # Instead it is easier to search in the messages history directly
    last_tool_invocation_id = None
    last_tool_invocation_results = None
    for past_message in state["messages"]:
        tool_calls = past_message.get("tool_calls", None)
        if not tool_calls:
            continue
        for tool_invocation in tool_calls:
            if tool_invocation.get("name", None) != search_in_public_chats.name:
                continue
            last_tool_invocation_id = tool_invocation["id"]

    if not last_tool_invocation_id:
        raise Exception("Can not forward search results since search_in_public_chats tool was not called.")
    for past_message in state["messages"]:
        if past_message.get("tool_call_id", None) != last_tool_invocation_id:
            continue
        last_tool_invocation_results = past_message.get("content", None)
        break

    if not last_tool_invocation_results:
        raise Exception("Can not forward last search result. It could be that the last search_in_public_chats tool call was not successfull or returned an empty result.")

    links = []
    # Note: Apparently tool invocation results are converted into strings.
    # However it seems to be a correctly serialized json object.
    last_tool_invocation_results = json.loads(last_tool_invocation_results)
    for result in last_tool_invocation_results:
        document = result.get("document", {})
        document_entries = document.get("metadata", {}).get("chatEntries", [])
        if not document_entries:
            continue
        first_entry_id = document_entries[0].get("id", None)
        if first_entry_id is None:
            continue
        links.append(first_entry_id)
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
    return

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
        forward_search_results
    ]
