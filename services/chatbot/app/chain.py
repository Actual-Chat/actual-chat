from typing import Literal

import os

from langchain_core.messages import (
    HumanMessage,
    SystemMessage,
    RemoveMessage,
    get_buffer_string
)
from langgraph.graph import StateGraph, START, END

from langgraph.checkpoint.memory import MemorySaver
from langchain_core.runnables.config import RunnableConfig
from langchain_core.messages import ToolMessage
from langgraph.prebuilt import ToolNode

from langchain_anthropic import ChatAnthropic
from langchain_core.runnables import RunnableLambda

import pydantic
assert(pydantic.VERSION.startswith("2."))

from langfuse.decorators import langfuse_context, observe

from .tools import (
    all as all_tools,
    _reply as call_reply,
    filter_last_search_results
)
from .state import State

MAX_MESSAGES_TO_TRIGGER_SUMMARIZATION = int(os.getenv(
    "BOT_MESSAGES_COUNT_TO_TRIGGER_SUMMARIZATION",
    default = 1000
))

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
    messages = state.messages
    if len(messages) > 0:
        last_message = messages[-1]
        _try_add_answer(last_message.content)

    return Ok

# Fake node to interrupt and wait for human feedback
def ask_human(state):
    pass

def update_state(state: State) -> State:
    stop_msg_id = state.last_seen_msg_id
    for message in reversed(state.messages):
        if message.id == stop_msg_id:
            break
        if isinstance(message, ToolMessage) and message.name=="resolve_search_type" and message.status=="success":
            state.search_type = message.content

    if state.messages:
        state.last_seen_msg_id = state.messages[-1].id

    return state

def create(*,
    claude_api_key,
#    prompt = None
):
    memory = MemorySaver()
    llm_no_tools = ChatAnthropic(
        model="claude-3-haiku-20240307",
        api_key = claude_api_key
    )

    tools = all_tools(classifier_model = llm_no_tools)
    llm = ChatAnthropic(
        model="claude-3-haiku-20240307",
        api_key = claude_api_key
    ).bind_tools(tools)

    tool_node = ToolNode(tools)

    def call_model(state: State):
        # Note:
        # Using guide at:
        # https://langchain-ai.github.io/langgraph/how-tos/memory/add-summary-conversation-history/
        # If a summary exists, we add this in as a system message
        summary = state.summary or ""
        if summary:
            system_message = f"Summary of conversation earlier: {summary}"
            messages = [SystemMessage(content=system_message)] + state.messages
        else:
            messages = state.messages
        response = llm.invoke(messages)
        return {
            "messages": [response]
        }

    def summarize(state: State):
        summary = state.summary or ""
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
                    state.messages
                )
            ),
            HumanMessage(content=summary_message)
        ]
        response = llm_no_tools.invoke(messages)
        # We now need to delete messages that we no longer want to show up
        # Note: It deletes ALL messages and keeps the summary.
        # Otherwise it requires to keep pairs of tools invocations and their results.
        # If pairs are not kept together it fails on the next llm invocation.
        delete_messages = [RemoveMessage(id=m.id) for m in state.messages]
        return {
            "summary": response.content,
            "messages": delete_messages,
            "last_search_result": filter_last_search_results(state)
        }

    def tools_or_final_answer(state: State) -> Literal["tools", "final_answer"]:
        last_message = state.messages[-1]
        return "tools" if last_message.tool_calls else "final_answer"

    def summarize_or_ask_human(state: State) -> Literal["summarize", "ask_human"]:
        should_summarize = len(state.messages) >= MAX_MESSAGES_TO_TRIGGER_SUMMARIZATION
        return "summarize" if should_summarize else "ask_human"

    graph_builder = StateGraph(State)

    graph_builder.add_node("agent", call_model)
    graph_builder.add_node("tools", tool_node)
    graph_builder.add_node("update_state", update_state)
    graph_builder.add_node("summarize", summarize)
    graph_builder.add_node("final_answer", final_answer)
    graph_builder.add_node("ask_human", ask_human)

    graph_builder.add_edge(START, "agent")
    graph_builder.add_conditional_edges(
        "agent",
        tools_or_final_answer,
    )
    graph_builder.add_conditional_edges(
        "final_answer",
        summarize_or_ask_human
    )
    graph_builder.add_edge("summarize", "ask_human")
    graph_builder.add_edge("tools", "update_state")
    graph_builder.add_edge("update_state", "agent")
    graph_builder.add_edge("ask_human", "agent")


    graph = graph_builder.compile(
        checkpointer = memory,
        interrupt_before=["ask_human"]
    )

    def invoke_graph(input_text, config: RunnableConfig) -> State:
        messages = {"messages": [HumanMessage(content=input_text)]}
        if graph.get_state(config).next==("ask_human",):
            # Update state & resume execution after human input
            graph.update_state(config, messages, as_node="ask_human")
            return graph.invoke(None, config)
        # Invoke graph from the start
        return graph.invoke(messages, config)

    return RunnableLambda(invoke_graph)
