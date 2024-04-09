from langchain_core.runnables.configurable import RunnableConfigurableAlternatives
from typing import List, Mapping, Any

from langchain_core.pydantic_v1 import BaseModel, Field


class RunnableConfigurableRuntimeAlternatives(RunnableConfigurableAlternatives):
    partial_variables: Mapping[str, Any] = Field(default_factory=dict)

    def __init__(self,
        default,
        *args,
        alternatives = {},
        **kwargs
    ):
        super().__init__(
            *args,
            default = default,
            alternatives = alternatives,
            **kwargs
        )

    @property
    def input_variables(self) -> List[str]:
        # Bug workaround: required for create_xml_agent method.
        return self.default.input_variables

    def partial(self, **kwargs):
        # Bug workaround: (Attempt)
        # Use this flag to keep instance passed to the framework intact.
        # Otherwide an internal copy of the instance will be created.
        # This will prohibit further runtime changes.
        self.default = self.default.partial(**kwargs)

        for key in self.alternatives.keys():
            self.alternatives[key] = self.alternatives[key].partial(**kwargs)
        return self


    def set_alternative(self, *, key, prompt):
        if key == self.default_key or key in self.alternatives:
            return
        if len(self.partial_variables) == 0:
            self.alternatives[key] = prompt
        else:
            self.alternatives[key] = prompt.partial(**self.partial_variables)


