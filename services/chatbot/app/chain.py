from typing import Annotated, Literal

from typing_extensions import TypedDict

from langchain_core.messages import HumanMessage
from langgraph.graph import StateGraph, START, END, MessagesState
from langgraph.checkpoint import MemorySaver
from langgraph.prebuilt import ToolNode
from langgraph.graph.message import add_messages

from langchain_anthropic import ChatAnthropic
from langchain_core.runnables import RunnableLambda

import pydantic
assert(pydantic.VERSION.startswith("2."))
from .tools import all as all_tools
from . import utils


# Note: Unused
"""
class State(TypedDict):
    # Messages have the type "list". The `add_messages` function
    # in the annotation defines how this state key should be updated
    # (in this case, it appends messages to the list, rather than overwriting them)
    messages: Annotated[list, add_messages]
"""

def user_input(input: str) -> MessagesState:
    return {"messages": [HumanMessage(content=input)]}

def should_continue(state: MessagesState) -> Literal["tools", "__end__"]:
    messages = state["messages"]
    last_message = messages[-1]
    if last_message.tool_calls:
        return "tools"
    return "__end__"


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
        return {"messages": [response]}


    graph_builder.add_node("agent", call_model)
    graph_builder.add_node("tools", tool_node)
    graph_builder.add_edge(START, "agent")
    graph_builder.add_conditional_edges(
        "agent",
        should_continue,
    )
    graph_builder.add_edge("tools", "agent")

    graph = graph_builder.compile(
        checkpointer = memory
    )
    return RunnableLambda(user_input) | graph
