import os

from typing import Annotated, Literal, Optional

from langchain_core.language_models.chat_models import BaseChatModel
from langchain_anthropic import ChatAnthropic

from langchain_core.messages import AnyMessage, SystemMessage, ToolMessage, HumanMessage
from langchain_core.tools import tool
from langchain_core.runnables import RunnableLambda, RunnableConfig

from langgraph.prebuilt import ToolNode, InjectedState
from langgraph.graph import StateGraph, add_messages, START, END
from langgraph.checkpoint.memory import MemorySaver

from pydantic import BaseModel

class SearchTypeState(BaseModel):
    messages: Annotated[list[AnyMessage], add_messages]
    # Here we store last detected search type
    search_type: Optional[str] = None

class SearchTypeResolver:
    type_of_search_prompt = '''As an expert in searching for information in chats, you follow a clear process to identify the target search area.
    Depending on your answer, the search process runs through different subsets of chats, so the answer is critical.
    There are three possible search areas:
    * PUBLIC - search in the publicly available chats
    * PRIVATE - search in the chats where the user is a member or owner
    * GENERAL - search in all chats, both PUBLIC and PRIVATE
    There is also one special value UNCERTAIN, when it is unclear from the user's message where to run next search.
    Instructions:
    * If the user says "search all chats" or "search everywhere," the search area is GENERAL
    * If the user requested to reset or start the search over, the search area is GENERAL
    * If the user explicitly mentions "public chats" or similar, the search area is PUBLIC
    * If the user refers to "private chats" or "my chats" or similar, the search area is PRIVATE
    * In all other cases when user's message is unrelated to chats the search area is UNCERTAIN
    Important:
    * Every user message in the list redefines search area unless search area is UNCERTAIN.
    * Return only one word in the output (PUBLIC, PRIVATE, GENERAL or UNCERTAIN).
    '''

    def __init__(self, model: BaseChatModel, tool_node_name: str):
        self.model = model
        self.tool_node_name = tool_node_name

    def process(self, state: SearchTypeState):
        stack = list()
        search_type = state.search_type if state.search_type else "GENERAL"
        for message in reversed(state.messages):
            if isinstance(message, HumanMessage):
                stack.append(message)
            elif isinstance(message, ToolMessage) and message.name==self.tool_node_name and message.status=="success":
                break

        system_message = [SystemMessage(content=self.type_of_search_prompt)]
        while stack:
            message = stack.pop()
            response = self.model.invoke(system_message + [message])
            if response.content in ["PUBLIC", "PRIVATE", "GENERAL"]:
                search_type = response.content

        return search_type
        # system_message = [SystemMessage(content=self.type_of_search_prompt)]
        # messages = [msg for msg in state.messages if isinstance(msg, HumanMessage)]
        # response = self.model.invoke(system_message + messages)
        # return response.content if response and response.content in ["PUBLIC", "PRIVATE"] else "GENERAL"


claude_api_key = os.getenv("CLAUDE_API_KEY")
classifier_model = ChatAnthropic(
    model="claude-3-haiku-20240307",
    api_key = claude_api_key
)

search_type_resolver = SearchTypeResolver(classifier_model, "resolve_search_type")

@tool
def resolve_search_type(state: Annotated[SearchTypeState, InjectedState]) -> str:
    """Call to get the search type."""
    return search_type_resolver.process(state)

tools = [resolve_search_type]
tool_node = ToolNode(tools)
model = ChatAnthropic(
    model="claude-3-haiku-20240307",
    api_key = claude_api_key
).bind_tools(tools)


def call_model(state: SearchTypeState):
    messages = state.messages
    response = model.invoke(messages)
    return {"messages": [response]}

def first_message(state) -> SearchTypeState:
    return state

def ask_human(state):
    pass

def should_continue(state: SearchTypeState) -> Literal["tools", "ask_human"]:
    messages = state.messages
    last_message = messages[-1]
    if last_message.tool_calls:
        return "tools"
    return "ask_human"

memory = MemorySaver()
workflow = StateGraph(SearchTypeState)

# Define the two nodes we will cycle between
workflow.add_node("first_message", first_message)
workflow.add_node("agent", call_model)
workflow.add_node("tools", tool_node)
workflow.add_node("ask_human", ask_human)

workflow.add_edge(START, "first_message")
workflow.add_edge("first_message", "agent")
workflow.add_conditional_edges(
    "agent",
    should_continue,
)
workflow.add_edge("tools", "agent")
workflow.add_edge("ask_human", "agent")
graph = workflow.compile(
    checkpointer = memory,
    interrupt_before=["ask_human"]
)

def invoke_graph(input_text, config: RunnableConfig) -> SearchTypeState:
    messages = {"messages": [HumanMessage(content=input_text)]}
    if graph.get_state(config).next==("ask_human",):
        graph.update_state(config, messages, as_node="ask_human")
        return graph.invoke(None, config)
    return graph.invoke(messages, config)

app = RunnableLambda(invoke_graph)


inputs = [
    "Search in public chats",
    "search in all chats",
    "London is the capital of the Great Britain",
    "search in my chats",
    "plese start over",
    "search in public chats"
]
thread = {"configurable": {"thread_id": "3"}}


_printed = set()

for input in inputs:
    events = app.stream(input, thread)
    for event in events:
        if not event:
            continue
        messages = event.get("messages")
        for message in messages:
            if message.id not in _printed:
                message.pretty_print()
                _printed.add(message.id)
