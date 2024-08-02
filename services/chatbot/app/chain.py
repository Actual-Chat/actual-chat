from typing import Annotated, Literal

from typing_extensions import TypedDict

from langchain_core.messages import HumanMessage
from langgraph.graph import StateGraph, START, END

# Note:
# It is very hard to extend MessagesState. To do so it would
# require rewriting many prebuild classes as many of them
# return MessagesState-like dictionaries (hard to modify behavior)
from langgraph.graph import MessagesState

from langgraph.checkpoint import MemorySaver
from langchain_core.runnables.config import RunnableConfig
from langgraph.prebuilt import ToolNode
from langgraph.graph.message import add_messages

from langchain_anthropic import ChatAnthropic
from langchain_core.runnables import RunnableLambda
from langchain_core.tools import tool

import pydantic
assert(pydantic.VERSION.startswith("2."))
from .tools import all as all_tools
from .tools import _reply as reply
from . import utils
from langfuse.decorators import langfuse_context, observe


def user_input(input: str) -> MessagesState:
    return {"messages": [HumanMessage(content=input)]}

def should_continue(state: MessagesState) -> Literal["tools", "final_answer"]:
    messages = state["messages"]
    last_message = messages[-1]
    if last_message.tool_calls:
        return "tools"
    return "final_answer"

@observe()
def final_answer(state: MessagesState, config: RunnableConfig):
    """Sends a final answer to the user.
    """
    Ok = {"messages":[]}
    messages = state["messages"]
    last_message = messages[-1]

    def _try_add_answer(content):
        if content is None:
            return
        if isinstance(content, list):
            for line in content:
                _try_add_answer(text)
            return
        if isinstance(content, str):
            reply(content, config)
            return
        langfuse_context.update_current_observation(
            level="WARNING",
            status_message=f"Unexpected content type: {str(type(content))}"
        )
        return

    _try_add_answer(last_message.content)

    return Ok


def create(*, claude_api_key, prompt):
    memory = MemorySaver()
    tools = all_tools()
    graph_builder = StateGraph(MessagesState)
    llm = ChatAnthropic(
        model="claude-3-haiku-20240307",
        api_key = claude_api_key
    ).bind_tools(tools)

    tool_node = ToolNode(tools)

    def call_model(state: MessagesState):
        messages = state["messages"]
        response = llm.invoke(messages)
        return {
            "messages": [response]
        }

    graph_builder.add_node("agent", call_model)
    graph_builder.add_node("tools", tool_node)
    graph_builder.add_node("final_answer", final_answer)
    graph_builder.add_edge(START, "agent")
    graph_builder.add_conditional_edges(
        "agent",
        should_continue,
    )
    graph_builder.add_edge("tools", "agent")
    graph_builder.add_edge("final_answer", END)

    graph = graph_builder.compile(
        checkpointer = memory
    )
    return RunnableLambda(user_input) | graph
