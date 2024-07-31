from typing import List, Any
from langchain_core.tools import tool
from langchain_core.runnables.config import RunnableConfig
from datetime import date as _date
from datetime import datetime
import requests

TOOLS_AUTH_FORWARD_CONTEXT = "forward-auth-context"

class _Tools(object):
    REPLY = "https://local.actual.chat/api/bot/conversation/reply"
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
    if (config is None):
        config = {}
    config = config.get("configurable", {})
    auth_context = config.get(TOOLS_AUTH_FORWARD_CONTEXT, None)
    headers = {
        "Authorization": auth_context
    }

    result = requests.post(
        _Tools.REPLY,
        json = {
            "text": message
        },
        headers = headers,
        verify = False # TODO: think again if needed.
    )
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
    if (config is None):
        config = {}
    config = config.get("configurable", {})
    auth_context = config.get(TOOLS_AUTH_FORWARD_CONTEXT, None)
    headers = {
        "Authorization": auth_context
    }

    result = requests.post(
        _Tools.SEARCH_PUBLIC_CHATS,
        json = {
            "text": text
        },
        headers = headers,
        verify = False # TODO: think again if needed.
    )
    return result.content


def all():
    return [
        reply,
        search_in_public_chats
    ]
