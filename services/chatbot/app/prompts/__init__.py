from typing import Dict, Any
from fastapi import Request
from inspect import cleandoc
from langchain_core.prompts import ChatPromptTemplate, BaseChatPromptTemplate, PromptTemplate
from langchain_core.runnables.configurable import RunnableConfigurableAlternatives
from langchain_core.runnables import ConfigurableField

class _LANGFUSE_PROMPT_KEY:
    MAIN = "main"


def init(langfuse):
#    from langchain import hub
#    prompt = hub.pull("hwchase17/xml-agent-convo")
#    assert False, str(prompt)

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


def _into_key(prompt):
    return "ver-" + str(prompt.version)

def set_per_request(langfuse, dynamic_prompt):
    def add_per_request(
        config: Dict[str, Any],
        request: Request
    ) -> Dict[str, Any]:
        if "configurable" not in config:
            config["configurable"] = {}
        # if "metadata" not in config:
        #     config["metadata"] = {}
        prompt = langfuse.get_prompt(_LANGFUSE_PROMPT_KEY.MAIN, cache_ttl_seconds=30)
        prompt_id = _into_key(prompt)
        if hasattr(dynamic_prompt, 'alternatives'):
            if prompt_id not in dynamic_prompt.alternatives:
                dynamic_prompt.alternatives[prompt_id] = ChatPromptTemplate.from_template(
                    prompt.get_langchain_prompt(),
                    partial_variables={"chat_history": []},
                )
        config["configurable"]["prompt"] = prompt_id
        return config

    return add_per_request


def create_dynamic_prompt(langfuse):
    default_prompt = langfuse.get_prompt(_LANGFUSE_PROMPT_KEY.MAIN, cache_ttl_seconds=30)
    prompt_id = _into_key(default_prompt)
    default_langchain_prompt = ChatPromptTemplate.from_template(
        default_prompt.get_langchain_prompt(),
        partial_variables={"chat_history": []},
    )
    dynamic_prompt = default_langchain_prompt.configurable_alternatives(
        which=ConfigurableField(id='prompt'),
        default_key = _into_key(default_prompt),
        prefix_keys=False,
    )

    # Bug workaround: example of RunnableConfigurableAlternatives doesn't work with create_xml_agent
    # Monkey patching configurable to match required prompt fields.
    dynamic_prompt.__dict__['input_variables'] = default_langchain_prompt.input_variables
    dynamic_prompt.__dict__['partial'] = default_langchain_prompt.partial

    return dynamic_prompt
