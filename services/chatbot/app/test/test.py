#!/usr/bin/env python

import sys
sys.path.insert(1, '/app/services/chatbot')

import os
from app import chain

the_chain = chain.create(
    claude_api_key = os.getenv("CLAUDE_API_KEY"),
#    prompt = dynamic_prompt
)

#print(the_chain.get_graph(xray=True).draw_mermaid())
print(the_chain.get_graph().draw_mermaid())
#the_chain.invoke({"aggregate": []})
