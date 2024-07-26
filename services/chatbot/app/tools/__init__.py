from langchain.tools import BaseTool, StructuredTool, tool

from datetime import date as _date
from datetime import datetime
# import jwt
import requests
import os
from cryptography.hazmat.primitives import serialization


@tool
def reply(message:str):
    """Send a message to the user."""
    pass

def all():
    return [reply]
