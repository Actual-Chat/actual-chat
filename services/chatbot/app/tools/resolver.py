from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import SystemMessage, ToolMessage, HumanMessage

from app.state import State

class SearchTypeResolver:
    type_of_search_prompt = '''As an expert in searching for information in chats, you follow a clear process to identify the target search area.
    Depending on your answer, the search process runs through different subsets of chats, so the answer is critical.
    There are three possible search areas:
    * PUBLIC - search in the publicly available chats
    * PRIVATE - search in the chats where the user is a member or owner
    * GENERAL - search in all chats, both PUBLIC and PRIVATE
    There is also one special value UNCERTAIN, when it is unclear from the user's message where to run next search.
    Instructions:
    * If the user says "search all chats" or "search everywhere," the search area is GENERAL
    * If the user requested to reset or start the search over, the search area is GENERAL
    * If the user explicitly mentions "public chats" or similar, the search area is PUBLIC
    * If the user refers to "private chats" or "my chats" or similar, the search area is PRIVATE
    * In all other cases when user's message is unrelated to chats the search area is UNCERTAIN
    Important:
    * Every user message in the list redefines search area unless search area is UNCERTAIN.
    * Return only one word in the output (PUBLIC, PRIVATE, GENERAL or UNCERTAIN).
    '''

    def __init__(self, model: BaseChatModel, tool_name: str):
        self.model = model
        self.tool_name = tool_name

    def process(self, state: State):
        stack = list()
        search_type = state.search_type if state.search_type else "GENERAL"
        for message in reversed(state.messages):
            if isinstance(message, HumanMessage):
                stack.append(message)
            elif isinstance(message, ToolMessage) and message.name==self.tool_name and message.status=="success":
                break

        system_message = [SystemMessage(content=self.type_of_search_prompt)]
        while stack:
            message = stack.pop()
            response = self.model.invoke(system_message + [message])
            if response.content in ["PUBLIC", "PRIVATE", "GENERAL"]:
                search_type = response.content

        return search_type

    @staticmethod
    def try_update_state(state: State, message: ToolMessage, tool_name: str):
        if message.name==tool_name and message.status=="success":
            state.search_type = message.content

