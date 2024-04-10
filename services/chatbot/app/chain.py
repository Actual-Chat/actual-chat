from typing import List
from inspect import cleandoc
from langchain import hub
from langchain.agents import AgentExecutor, create_xml_agent
from langchain_community.chat_models import ChatAnthropic
from langchain_core.messages import BaseMessage
from langchain_core.runnables.history import RunnableWithMessageHistory
from langchain_core.chat_history import BaseChatMessageHistory
from langchain_core.globals import set_debug
from langchain_core.prompts import ChatPromptTemplate
from langchain_core.runnables.utils import ConfigurableField
from langchain_core.runnables import Runnable, RunnablePassthrough
from langchain.tools.render import render_text_description
from langchain.agents.format_scratchpad import format_xml
from langchain.agents.output_parsers import XMLAgentOutputParser
from app.runnables.configurable import RuntimeConfigurableAgentExecutor

# Note: As of current state (Apr 1, 2024) Langserve has issues working
# with Pydantic models v2. Skipping this for later investigation..
import pydantic
if pydantic.VERSION.startswith("1."):
    from pydantic import BaseModel as V1BaseModel
    from pydantic import Field as V1Field
else:
    assert(pydantic.VERSION.startswith("2."))
    from pydantic.v1 import BaseModel as V1BaseModel
    from pydantic.v1 import Field as V1Field
from .tools import all as all_tools
from . import utils

set_debug(True)

class Input(V1BaseModel):
    input: str

class Output(V1BaseModel):
    output: str


class InMemoryHistory(BaseChatMessageHistory, V1BaseModel):
    """In memory implementation of chat message history."""

    messages: List[BaseMessage] = V1Field(default_factory=list)

    def add_messages(self, messages: List[BaseMessage]) -> None:
        """Add a list of messages to the store"""
        self.messages.extend(messages)

    def clear(self) -> None:
        self.messages = []

# Here we use a global variable to store the chat message history.
# This will make it easier to inspect it to see the underlying results.
store = {}

def get_by_session_id(session_id: str) -> BaseChatMessageHistory:
    if session_id not in store:
        store[session_id] = InMemoryHistory()
    return store[session_id]


def create(*, claude_api_key, prompt):
    tools = all_tools()
    llm = ChatAnthropic(
        model = 'claude-2',
        anthropic_api_key = claude_api_key
    )
    agent_runnable = create_xml_agent(llm, tools, prompt)
    agent_executor = RuntimeConfigurableAgentExecutor(
        agent = agent_runnable,
        tools = tools
    )
    return (
        agent_executor.with_types(
            input_type = Input,
            output_type = Output
        ).with_config(
            {"run_name": "agent"}
        ),
        # TODO: check if workaround implemented in the RunnableConfigurableRuntimeAlternatives class works.
        # Remove this workaround if it does
        agent_runnable.middle[0]
    )

