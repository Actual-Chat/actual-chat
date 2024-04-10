"""An example that shows how to create a custom agent executor like Runnable.

At the time of writing, there is a bug in the current AgentExecutor that
prevents it from correctly propagating configuration of the underlying
runnable. While that bug should be fixed, this is an example shows
how to create a more complex custom runnable.

Please see documentation for custom agent streaming here:

https://python.langchain.com/docs/modules/agents/how_to/streaming#stream-tokens

**ATTENTION**
To support streaming individual tokens you will need to manually set the streaming=True
on the LLM and use the stream_log endpoint rather than stream endpoint.
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
        configurable = cast(Dict[str, Any], config.pop("configurable", {}))

        if configurable:
            configured_agent = self.agent.with_config(
                {
                    "configurable": configurable,
                }
            )
        else:
            configured_agent = self.agent

        executor = AgentExecutor(
            agent = configured_agent,
            tools = self.tools,
        ).with_config({"run_name": "agent"})
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
        configurable = cast(Dict[str, Any], config.pop("configurable", {}))

        if configurable:
            configured_agent = self.agent.with_config(
                {
                    "configurable": configurable,
                }
            )
        else:
            configured_agent = self.agent

        executor = AgentExecutor(
            agent = configured_agent,
            tools = self.tools,
        ).with_config({"run_name": "agent"})

        async for output in executor.astream(input, config=config, **kwargs):
            yield output





