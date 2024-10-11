import os
import uuid
from langchain_core.messages import ToolMessage, HumanMessage
from langchain_anthropic import ChatAnthropic
import pytest

from app.state import State
from app.tools.resolver import SearchTypeResolver

@pytest.fixture(scope="module")
def classifier_model():
    claude_api_key = os.getenv("CLAUDE_API_KEY")
    return ChatAnthropic(
        model="claude-3-haiku-20240307",
        api_key = claude_api_key
    )


RESOLVE_TOOL = "search_type_resolve_tool"

USER_INPUTS = [
    "Search in public chats",
    "search in all chats",
    "London is the capital of the Great Britain",
    "search in my chats",
    "please start over",
    "search in public chats"
]

OUTPUTS_1 = ["PUBLIC", "GENERAL", "GENERAL", "PRIVATE", "GENERAL", "PUBLIC"]
OUTPUTS_2 = ["GENERAL", "PRIVATE", "PUBLIC"]
OUTPUTS_3 = ["GENERAL", "PUBLIC"]

@pytest.mark.parametrize("batch_size, expected_types", [(1, OUTPUTS_1), (2, OUTPUTS_2), (3, OUTPUTS_3)])
def test_resolved_types(classifier_model, batch_size, expected_types):
    resolver = SearchTypeResolver(classifier_model, RESOLVE_TOOL)
    state = State(messages = [])
    count = 0
    expected_iter = iter(expected_types)
    for text in USER_INPUTS:
        state.messages.append(HumanMessage(content=text))
        count += 1
        if (count % batch_size) == 0:
            result = resolver.process(state)
            assert result == next(expected_iter)
            state.messages.append(ToolMessage(result, name=RESOLVE_TOOL, status="success", tool_call_id=str(uuid.uuid1())))

