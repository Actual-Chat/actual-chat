#!/usr/bin/env python

from fastapi import FastAPI
from fastapi.responses import RedirectResponse
from fastapi import Request
from langserve import add_routes
from inspect import cleandoc
from typing import Dict, Any
from langchain_core.runnables import ConfigurableField
from langchain_core.runnables.configurable import RunnableConfigurableAlternatives
from langchain_core.prompts import ChatPromptTemplate

import jwt
import os
import logging
logger = logging.getLogger(__name__)

from . import chain
from . import prompts
from . import utils
from . import tools

from langfuse import Langfuse

try:
    if os.getenv("LANGFUSE_HOST"):
        langfuse = Langfuse()
        langfuse.auth_check()
    else:
        langfuse = None
except:
    langfuse = None

# prompts.init(langfuse)

tools._Tools.init(
    base_url = os.getenv("BOT_TOOLS_BASE_URL")
)

app = FastAPI(
    title="Chatbot Service",
    # TODO: automate or streamline
    version="1.0",
    description=cleandoc("""
        Chat bot service responsible to chat with users and help finding answers on their questions.
    """),
)

# Note: Copied from https://github.com/langchain-ai/langserve/issues/311
def _add_tracing(
    config: Dict[str, Any],
    request: Request
) -> Dict[str, Any]:
    """
    Config modifier function to add a tracing callback

    :param config: config dict
    :param request: HTTP request
    :return: updated config
    """

    if "callbacks" not in config:
        config["callbacks"] = []

    # Note: temporary solution to make it working
    if "conversation_id" not in config:
        config["conversation_id"] = str(request.client)
    if "user_id" not in config:
        config["user_id"] = str(request.client)
    # endof temporary solution

    conversation_id = config["conversation_id"]
    user_id = config["user_id"]

    metadata = {
        "conversation_id": conversation_id,
        "user_id": user_id
    }
    if langfuse is not None:
        trace = langfuse.trace(
            id=conversation_id,
            metadata=metadata,
        )
        trace_handler = trace.getNewHandler()

        config["callbacks"].extend([
            # langfuse_handler
            trace_handler
        ])
    return config


assert(tools.TOOLS_AUTH_FORWARD_CONTEXT is not None)

def _add_tools_auth_context(
    config: Dict[str, Any],
    request: Request
) -> Dict[str, Any]:
    configurable = config.get("configurable", {})
    configurable[tools.TOOLS_AUTH_FORWARD_CONTEXT] = request.headers.get("Authorization", None)
    config["configurable"] = configurable
    return config

def _extract_thread_id(
    config: Dict[str, Any],
    request: Request
) -> Dict[str, Any]:
    configurable = config.get("configurable", {})
    jwt_bearer_token = request.headers.get("Authorization", None)
    assert(jwt_bearer_token is not None, "Authorization header must be set")
    assert(jwt_bearer_token.startswith("Bearer "), "Sanity check")
    jwt_token = jwt_bearer_token.replace("Bearer ", "", 1)

    claims = jwt.decode(
        jwt_token,
        options = {
            # TODO: Validate JWT token
            "verify_signature": False
        }
    )
    conversation_id = claims.get("ConversationId", None)
    assert(conversation_id is not None, "ConversationId must be set")
    configurable["thread_id"] = conversation_id
    config["configurable"] = configurable
    return config



@app.get("/")
async def redirect_root_to_docs():
    return RedirectResponse("/docs")


# dynamic_prompt = prompts.create_dynamic_prompt(langfuse)

the_chain = chain.create(
    claude_api_key = os.getenv("CLAUDE_API_KEY"),
#    prompt = dynamic_prompt
)
# Inject real prompt here.
# _set_prompt = prompts.set_per_request(langfuse, dynamic_prompt = dynamic_prompt)
_set_prompt = None

def _per_request_config(config, request):
    config = _add_tracing(config, request)
    config = _add_tools_auth_context(config, request)
    config = _extract_thread_id(config, request)
    if _set_prompt is not None:
        config = _set_prompt(config, request)
    return config


# Edit this to add the chain you want to add
add_routes(
    app,
    the_chain,
    per_req_config_modifier = _per_request_config,
    config_keys = ["configurable"]
)


if __name__ == "__main__":
    # Local dev mode.
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8081)
