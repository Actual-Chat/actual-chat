#!/usr/bin/env python

from fastapi import FastAPI
from fastapi.responses import RedirectResponse
from langserve import add_routes
from inspect import cleandoc

import os

from . import chain


app = FastAPI(
    title="Chatbot Service",
    # TODO: automate or streamline
    version="1.0",
    description=cleandoc("""
        Chat bot service responsible to chat with users and help finding answers on their questions.
    """),
)


@app.get("/")
async def redirect_root_to_docs():
    return RedirectResponse("/docs")


# Edit this to add the chain you want to add
add_routes(
    app,
    chain.create(
        claude_api_key = os.getenv("CLAUDE_API_KEY")
    ),
)


if __name__ == "__main__":
    # Local dev mode.
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8081)
