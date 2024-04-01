from typing import List
from inspect import cleandoc
from langchain import hub
from langchain.agents import AgentExecutor, create_xml_agent
from langchain_community.chat_models import ChatAnthropic
from langchain_core.messages import BaseMessage
from langchain_core.runnables.history import RunnableWithMessageHistory
from langchain_core.chat_history import BaseChatMessageHistory
from langchain_core.globals import set_debug


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


def create(*, claude_api_key):
    tools = all_tools()
    llm = ChatAnthropic(
        model = 'claude-2',
        anthropic_api_key = claude_api_key
    )
    prompt = cleandoc("""
        For the given objective, come up with a simple step by step plan.
        Use tools provided if neccessary.
        You have the following list of tools:
        {tools}

        This plan should involve individual tasks, that if executed correctly will yield the correct answer.
        Do not add any superfluous steps.
        The result of the final step should be the final answer. Make sure that each step has all the information
        needed - do not skip steps.

        Previous Conversation:
        {chat_history}

        Input: {input}
        Thoughts: {agent_scratchpad}
    """)
    prompt = hub.pull("hwchase17/xml-agent-convo")
    print(f"PROMPT:\n---\n{prompt}")
    agent_runnable = create_xml_agent(llm, tools, prompt)
    agent_executor = AgentExecutor(agent=agent_runnable, tools=tools, verbose=True)
    """
    return RunnableWithMessageHistory(
        agent_executor,
        get_by_session_id,
        input_messages_key="input",
        history_messages_key="chat_history",
    ).with_types(input_type=Input, output_type=Output)
    """
    return agent_executor.with_types(input_type=Input, output_type=Output).with_config(
        {"run_name": "agent"}
    )

