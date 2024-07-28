from langchain.tools import BaseTool, StructuredTool, tool

from datetime import date as _date
from datetime import datetime
# import jwt
import requests
import os
from cryptography.hazmat.primitives import serialization

TOOLS_AUTH_FORWARD_CONTEXT = "forward-auth-context"

class _Tools(object):
    REPLY = "https://local.actual.chat/api/bot/conversation/reply"

def all(config):
    if (config is None):
        config = {}
    auth_context = config.get(TOOLS_AUTH_FORWARD_CONTEXT, None)
    headers = {
        "Authorization": auth_context
    }

    @tool
    def reply(message:str):
        """Send a message to the user."""
        result = requests.post(
            _Tools.REPLY,
            json = {
                "text": message
            },
            headers = headers,
            verify = False # TODO: think again if needed.
        )
        return

    return [reply]
