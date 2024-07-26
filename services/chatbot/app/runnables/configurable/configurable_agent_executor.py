"""
Bug workaround:
At the time of writing, there is a bug in the current AgentExecutor that
prevents it from correctly propagating configuration of the underlying runnable.

Ref:
https://github.com/langchain-ai/langserve/blob/v0.0.51/examples/configurable_agent_executor/server.py

"""

from typing import Any, AsyncIterator, Dict, List, Optional, cast

from fastapi import FastAPI
from langchain.agents import AgentExecutor, tool
from langchain.agents.format_scratchpad import format_to_openai_functions
from langchain.agents.output_parsers import OpenAIFunctionsAgentOutputParser
from langchain.chat_models import ChatOpenAI
from langchain.embeddings import OpenAIEmbeddings
from langchain.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain.pydantic_v1 import BaseModel
from langchain.tools.render import format_tool_to_openai_function
from langchain.vectorstores import FAISS
from langchain_core.runnables import (
    ConfigurableField,
    ConfigurableFieldSpec,
    Runnable,
    RunnableConfig,
)
from langchain_core.runnables.utils import Input, Output


class RuntimeConfigurableAgentExecutor(Runnable):
    """A custom runnable that will be used by the agent executor."""

    def __init__(self, *, agent, tools):
        """Initialize the runnable."""
        super().__init__()
        self.agent = agent
        self.tools = tools

    def invoke(self, input: Input, config: Optional[RunnableConfig] = None) -> Output:
        executor = AgentExecutor(
            agent = _into_configured_agent(self.agent, config),
            tools = self.tools,
        )
        return executor.invoke(input, config)

    @property
    def config_specs(self) -> List[ConfigurableFieldSpec]:
        return self.agent.config_specs

    async def astream(
        self,
        input: Input,
        config: Optional[RunnableConfig] = None,
        **kwargs: Optional[Any],
    ) -> AsyncIterator[Output]:
        """Stream the agent's output."""
        # Note: creating a separate instance per call since it
        # is being used as a context later in the iterator
        executor = AgentExecutor(
            agent = _into_configured_agent(self.agent, config),
            tools = self.tools,
        )

        async for output in executor.astream(input, config=config, **kwargs):
            yield output

def _into_configured_agent(agent, config):
    configurable = cast(Dict[str, Any], config.pop("configurable", {}))
    if configurable:
        return agent.with_config({
            "configurable": configurable,
        })
    else:
        return agent

