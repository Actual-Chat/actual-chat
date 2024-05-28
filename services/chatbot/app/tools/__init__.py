from langchain.tools import BaseTool, StructuredTool, tool

from datetime import date as _date
from datetime import datetime
import jwt
import requests
import os
from cryptography.hazmat.primitives import serialization


@tool
def today(format:str) -> datetime:
    """Returns current date in a format specified"""
    # return _date.today()

    dirname = os.path.dirname(__file__)
    file_path = os.path.join(dirname,"../../sample.ecdsa")
    with open(file_path, 'rb') as file:
        private_key_OpenSSH = file.read()
    private_key = serialization.load_ssh_private_key(private_key_OpenSSH, password=b"sample")
    payload = {

        "format": format
    }
    encoded = jwt.encode(payload, private_key, algorithm="ES256")
    endpoint = "https://local.actual.chat/api/bot/sample-tool/today"
    result = requests.post(
        endpoint,
        data = encoded,
        # TODO: investigate
        verify = False
    )
    print(result)
    return result.json()

def all():
    return [today]
