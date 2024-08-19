
Notes: 
The Authentication in the current state is not working as intended. 
Therefore a workaround is implemented. It is known that it is not the best solution,
yet it works and allows us to move forward.
Several problems noticed:
1. Authentication Handler can not be added for a single controller. 
   It adds to all external call - no matter what controller is being used.
2. Authentication middleware can not be inserted in a separate service. 
   So far only the AppService being first adds middleware. Meaning that
   other services (like MLServiceModulde) can't add it's own authentication.
   Example error:
    System.InvalidOperationException: Endpoint ActualChat.MLSearch.Bot.Tools.ConversationToolsController.Reply
    (ActualChat.MLSearch.Service) contains authorization metadata, but a middleware 
    was not found that supports authorization.
    Configure your application startup by adding app.UseAuthorization() 
    in the application startup code. If there are calls to app.UseRouting() 
    and app.UseEndpoints(...), the call to app.UseAuthorization() must go between them.