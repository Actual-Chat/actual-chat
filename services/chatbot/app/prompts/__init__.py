from typing import Dict, Any
from fastapi import Request
from inspect import cleandoc

class PER_REQUEST_CONFIG_KEY:
    MAIN_PROMPT = "main_prompt"

class _LANGFUSE_PROMPT_KEY:
    MAIN = "main"


def init(langfuse):
    langfuse.create_prompt(
        name = _LANGFUSE_PROMPT_KEY.MAIN,
        prompt = cleandoc("""
            For the given objective, come up with a simple step by step plan.
            Use tools provided if neccessary.
            You have the following list of tools:
            {tools}

            In order to use a tool, you can use <tool></tool> and <tool_input></tool_input> tags.
            You will then get back a response in the form <observation></observation>

            This plan should involve individual tasks, that if executed correctly
            will yield the correct answer.
            Do not add any superfluous steps.
            When you are done, respond with a final answer between <final_answer></final_answer>
            Make sure that each step has all the information needed - do not skip steps.

            Previous conversation was:
            {chat_history}

            Objective: {input}

            Thoughts: {agent_scratchpad}
        """),
        config = {},
        is_active=True
    )


def set_per_request(langfuse):
    def add_per_request(
        config: Dict[str, Any],
        request: Request
    ) -> Dict[str, Any]:
        if "configurable" not in config:
            config["configurable"] = {}
        prompt = langfuse.get_prompt(_LANGFUSE_PROMPT_KEY.MAIN)
        config["configurable"][PER_REQUEST_CONFIG_KEY.MAIN_PROMPT] = prompt
        return config

    return add_per_request

