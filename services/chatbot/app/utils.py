from typing import Dict, Any
from fastapi import Request
from inspect import cleandoc
from langchain_core.prompts import ChatPromptTemplate, BaseChatPromptTemplate, PromptTemplate
from langchain_core.runnables.configurable import RunnableConfigurableAlternatives
from langchain_core.runnables import ConfigurableField

def add_traces(dynamic_prompt):
    def traced(name):
        assign_fn = getattr(dynamic_prompt, name)
        if not callable(assign_fn):
            return
        def assigned(
            *args,
            **kwargs
        ):
            print(f"Using {name}: {str(dict(kwargs))}")
            return assign_fn(*args, **kwargs)
        object.__setattr__(dynamic_prompt, name, assigned)

    for method_name in dir(dynamic_prompt):
        try:
            traced(method_name)
        except Exception as e:
            print(e)
    return dynamic_prompt
