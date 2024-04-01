from langchain.tools import BaseTool, StructuredTool, tool

from datetime import date as _date
from datetime import datetime


@tool
def today(format:str) -> datetime:
    """Returns current date in a format specified"""
    return _date.today()


def all():
    return [today]
