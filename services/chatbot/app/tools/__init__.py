from langchain_core.tools import tool
from langchain_core.runnables.config import RunnableConfig
from datetime import date as _date
from datetime import datetime
import requests

TOOLS_AUTH_FORWARD_CONTEXT = "forward-auth-context"

class _Tools(object):
    REPLY = "https://local.actual.chat/api/bot/conversation/reply"

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


def all():
    return [reply]
