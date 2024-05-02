using System;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace ActualChat.MLSearch.Bot.Tools;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class BotToolsAttribute : Attribute ;