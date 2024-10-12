import os
from app import chain

def test_create_chain():
    the_chain = chain.create(
        claude_api_key = os.getenv("CLAUDE_API_KEY"),
    )
    assert the_chain is not None


# import sys
# sys.path.insert(1, '/app/services/chatbot')
#print(the_chain.get_graph(xray=True).draw_mermaid())
#print(the_chain.get_graph().draw_mermaid())
#the_chain.invoke({"aggregate": []})
