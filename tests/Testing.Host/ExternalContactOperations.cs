using ActualChat.Contacts;

namespace ActualChat.Testing.Host;

public static class ExternalContactOperations
{
    public static async Task<ApiArray<ExternalContactFull>> SaveExternalContacts(this IWebTester tester, params IEnumerable<ExternalContactFull> externalContacts)
    {
        var changes = externalContacts.Select(x => new ExternalContactChange(x.Id, null, Change.Create(x)));
        var results = await tester.Commander.Call(new ExternalContacts_BulkChange(tester.Session, changes.ToApiArray()));
        var errors = results.Select(x => x.Error).SkipNullItems().ToList();
        if (errors.Count > 0)
            throw new AggregateException("Failed to create external contacts", errors);

        return results.Select(x => x.Value!).ToApiArray();
    }
}
