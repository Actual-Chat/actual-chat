using ActualChat.Contacts;

namespace ActualChat.Testing.Host;

public static class ExternalContactOperations
{
    public static async Task<ExternalContactFull[]> SaveExternalContacts(this IWebTester tester, params IEnumerable<ExternalContactFull> externalContacts)
    {
        var changes = externalContacts.Select(x =>
            new ExternalContactChange(
                x.Id,
                x.Version > 0 ? x.Version : null,
                x.Version > 0 ? Change.Update(x) : Change.Create(x)));
        var results = await tester.Commander.Call(new ExternalContacts_BulkChange {
            Session = tester.Session,
            Changes = changes.ToArray(),
        });
        var errors = results.Select(x => x.Error).SkipNullItems().ToList();
        if (errors.Count > 0)
            throw new AggregateException("Failed to create/update external contacts", errors);

        return results.Select(x => x.Value!).ToArray();
    }

    public static async Task<ExternalContactFull[]> DeleteExternalContacts(this IWebTester tester, params IEnumerable<ExternalContactId> externalContactIds)
    {
        var changes = externalContactIds.Select(x =>
            new ExternalContactChange(x, null, Change.Remove<ExternalContactFull>()));
        var results = await tester.Commander.Call(new ExternalContacts_BulkChange {
            Session = tester.Session,
            Changes = changes.ToArray(),
        });
        var errors = results.Select(x => x.Error).SkipNullItems().ToList();
        if (errors.Count > 0)
            throw new AggregateException("Failed to delete external contacts", errors);

        return results.Select(x => x.Value!).ToArray();
    }
}
