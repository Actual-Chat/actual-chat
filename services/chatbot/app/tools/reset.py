from langchain_core.messages import ToolMessage

from app.state import State


class ResetHandler:
    @staticmethod
    def try_update_state(state: State, message: ToolMessage, tool_name: str):
        if message.name == tool_name and message.status == "success":
            state.clear()
