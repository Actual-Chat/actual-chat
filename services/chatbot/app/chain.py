from typing import Annotated, Literal

from typing_extensions import TypedDict

import os

from langchain_core.messages import (
    HumanMessage,
    SystemMessage,
    RemoveMessage,
    AIMessage,
    filter_messages,
    trim_messages,
    get_buffer_string
)
from langgraph.graph import StateGraph, START, END

from langgraph.checkpoint.memory import MemorySaver
from langchain_core.runnables.config import RunnableConfig
from langgraph.prebuilt import ToolNode
from langgraph.graph.message import add_messages

from langchain_anthropic import ChatAnthropic
from langchain_core.runnables import RunnableLambda
from langchain_core.tools import tool

import pydantic
assert(pydantic.VERSION.startswith("2."))

from langfuse.decorators import langfuse_context, observe

from .tools import (
    all as all_tools,
    _reply as call_reply,
    reply as reply_tool,
    forward_search_results as forward_search_results_tool,
    filter_last_search_in_public_chats_results
)
from . import utils
from .state import State

MAX_MESSAGES_TO_TRIGGER_SUMMARIZATION = int(os.getenv(
    "BOT_MESSAGES_COUNT_TO_TRIGGER_SUMMARIZATION",
    default = 1000
))

def user_input(input: str) -> State:
    return {"messages": [HumanMessage(content=input)]}

def should_continue(state: State) -> Literal["tools", "final_answer"]:
    messages = state["messages"]
    last_message = messages[-1]
    if last_message.tool_calls:
        return "tools"
    return "final_answer"

def should_summarize(state: State) -> Literal["summarize_conversation", "human_input"]:
    if len(state["messages"]) < MAX_MESSAGES_TO_TRIGGER_SUMMARIZATION:
        # Nothing to do
        return "human_input"
    else:
        return "summarize_conversation"

@observe()
def final_answer(state: State, config: RunnableConfig):
    """Sends a final answer to the user.
    """
    def _try_add_answer(content):
        if content is None:
            return
        if isinstance(content, list):
            for line in content:
                _try_add_answer(line)
            return
        if isinstance(content, str):
            call_reply(content, config)
            return
        langfuse_context.update_current_observation(
            level="WARNING",
            status_message=f"Unexpected content type: {str(type(content))}"
        )
        return
    Ok = {"messages":[]}
    messages = state["messages"]
    if len(messages) > 0:
        last_message = messages[-1]
        _try_add_answer(last_message.content)

    return Ok

# Fake node to interrupt and wait for human feedback
def human_input(state):
    pass

def create(*,
    claude_api_key,
#    prompt = None
):
    memory = MemorySaver()
    tools = all_tools()
    graph_builder = StateGraph(State)
    llm = ChatAnthropic(
        model="claude-3-haiku-20240307",
        api_key = claude_api_key
    ).bind_tools(tools)
    llm_no_tools = ChatAnthropic(
        model="claude-3-haiku-20240307",
        api_key = claude_api_key
    )

    tool_node = ToolNode(tools)

    def call_model(state: State):
        # Note:
        # Using guide at:
        # https://langchain-ai.github.io/langgraph/how-tos/memory/add-summary-conversation-history/
        # If a summary exists, we add this in as a system message
        summary = state.get("summary", "")
        if summary:
            system_message = f"Summary of conversation earlier: {summary}"
            messages = [SystemMessage(content=system_message)] + state["messages"]
        else:
            messages = state["messages"]
        response = llm.invoke(messages)
        return {
            "messages": [response]
        }

    def summarize_conversation(state: State):
        summary = state.get("summary", "")
        if summary:
            # If a summary already exists, we use a different system prompt
            # to summarize it than if one didn't
            summary_message = (
                f"This is summary of the conversation to date: {summary}\n\n"
                "Extend the summary by taking into account the new messages above:"
            )
        else:
            summary_message = "Create a summary of the conversation above:"

        messages = [
            SystemMessage(
                content = get_buffer_string(
                    state["messages"]
                )
            ),
            HumanMessage(content=summary_message)
        ]
        response = llm_no_tools.invoke(messages)
        # We now need to delete messages that we no longer want to show up
        # Note: It deletes ALL messages and keeps the summary.
        # Otherwise it requires to keep pairs of tools invocations and their results.
        # If pairs are not kept together it fails on the next llm invocation.
        delete_messages = [RemoveMessage(id=m.id) for m in state["messages"][:]]
        return {
            "summary": response.content,
            "messages": delete_messages,
            "last_search_result": filter_last_search_in_public_chats_results(state)
        }


    graph_builder.add_node("agent", call_model)
    graph_builder.add_node("tools", tool_node)
    graph_builder.add_node("summarize_conversation", summarize_conversation)
    graph_builder.add_node("final_answer", final_answer)
    graph_builder.add_node("human_input", human_input)

    graph_builder.add_edge(START, "agent")
    graph_builder.add_conditional_edges(
        "agent",
        should_continue,
    )
    graph_builder.add_conditional_edges(
        "final_answer",
        should_summarize
    )
    graph_builder.add_edge("summarize_conversation", "human_input")
    graph_builder.add_edge("tools", "agent")
    graph_builder.add_edge("human_input", "agent")


    graph = graph_builder.compile(
        checkpointer = memory,
        interrupt_before=["human_input"]
    )
    return RunnableLambda(user_input) | graph
