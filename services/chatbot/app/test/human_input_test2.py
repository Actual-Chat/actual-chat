import os
from typing import Literal

from langchain_anthropic import ChatAnthropic

from langchain_core.messages import HumanMessage
from langchain_core.tools import tool
from langchain_core.runnables import RunnableLambda, RunnableConfig

from langgraph.prebuilt import ToolNode
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.checkpoint.memory import MemorySaver


@tool
def get_weather(location: str):
    """Call to get the current weather."""
    if location.lower() in ["sf", "san francisco"]:
        return "It's 60 degrees and foggy."
    elif location.lower() in ["nyc", "new york"]:
        return "It's 90 degrees and sunny."
    else:
        return "It's 75 degrees and cloudy."

@tool
def get_coolest_cities():
    """Get a list of coolest cities"""
    return "nyc, sf"

tools = [get_weather, get_coolest_cities]
tool_node = ToolNode(tools)

claude_api_key = os.getenv("CLAUDE_API_KEY")
model = ChatAnthropic(
    model="claude-3-haiku-20240307",
    api_key = claude_api_key
).bind_tools(tools)


def should_continue(state: MessagesState) -> Literal["tools", "ask_human"]:
    messages = state["messages"]
    last_message = messages[-1]
    if last_message.tool_calls:
        return "tools"
    return "ask_human"

def should_complete(state: MessagesState) -> Literal["agent", END]: # type: ignore
    messages = state["messages"]
    last_message = messages[-1]
    if last_message.content=="exit":
        return END
    return "agent"

def call_model(state: MessagesState):
    messages = state["messages"]
    response = model.invoke(messages)
    return {"messages": [response]}

def start_input(state) -> MessagesState:
    return {"messages": [HumanMessage(content="--Start--")]}

def ask_human(state):
    pass

memory = MemorySaver()
workflow = StateGraph(MessagesState)

# Define the two nodes we will cycle between
workflow.add_node("agent", call_model)
workflow.add_node("tools", tool_node)
workflow.add_node("start_input", start_input)
workflow.add_node("ask_human", ask_human)

workflow.add_edge(START, "start_input")
workflow.add_edge("start_input", "agent")
workflow.add_conditional_edges(
    "agent",
    should_continue,
)
workflow.add_edge("tools", "agent")
workflow.add_conditional_edges(
    "ask_human",
    should_complete,
)

graph = workflow.compile(
    checkpointer = memory,
    interrupt_before=["ask_human"]
)
def invoke_graph(input_text, config: RunnableConfig) -> MessagesState:
    messages = {"messages": [HumanMessage(content=input_text)]}
    if graph.get_state(config).next==("ask_human",):
        graph.update_state(config, messages, as_node="ask_human")
        return graph.invoke(None, config)
    return graph.invoke(messages, config)

app = RunnableLambda(invoke_graph)

#print(app.get_graph().draw_mermaid())

# New code is here

def _print_event(event: dict, _printed: set, max_length=1500):
    if not event:
        return
    messages = event.get("messages")
    if not isinstance(messages, list):
        messages = [messages]
    for message in messages:
        if message.id not in _printed:
            message.pretty_print()
            _printed.add(message.id)

inputs = [
    "Search for the weather in LA",
    "search for the weather in new york",
    "search for the weather in sf now",
    "exit"
]
thread = {"configurable": {"thread_id": "3"}}

_printed = set()

for input in inputs:
    events = app.stream(input, thread)
    for event in events:
        _print_event(event, _printed)
