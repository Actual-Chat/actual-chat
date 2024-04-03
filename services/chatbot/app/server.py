#!/usr/bin/env python

from fastapi import FastAPI
from fastapi.responses import RedirectResponse
from fastapi import Request
from langserve import add_routes
from inspect import cleandoc
from typing import Dict, Any

import os
import logging
logger = logging.getLogger(__name__)

from . import chain
from . import prompts

from langfuse import Langfuse
#from langfuse.callback import CallbackHandler

langfuse = Langfuse()

langfuse.auth_check()

prompts.init(langfuse)

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

    trace = langfuse.trace(
        id=conversation_id,
        metadata=metadata,
    )
    trace_handler = trace.getNewHandler()

    config["callbacks"].extend([
#        langfuse_handler
        trace_handler
    ])
    return config

@app.get("/")
async def redirect_root_to_docs():
    return RedirectResponse("/docs")


# Edit this to add the chain you want to add
add_routes(
    app,
    chain.create(
        claude_api_key = os.getenv("CLAUDE_API_KEY"),
        main_prompt_config_key = prompts.PER_REQUEST_CONFIG_KEY.MAIN_PROMPT,
        prompt = langfuse.get_prompt(prompts._LANGFUSE_PROMPT_KEY.MAIN)
    ),
    per_req_config_modifier = [
        _add_tracing,
        prompts.set_per_request(langfuse)
    ],
    config_keys = ["configurable", "conversation_id", "user_id"]
)


if __name__ == "__main__":
    # Local dev mode.
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8081)
