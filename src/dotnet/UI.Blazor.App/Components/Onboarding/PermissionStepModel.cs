using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class PermissionStepModel(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;
    public IReadOnlyList<PermissionRow> Rows { get; private set; } = [];
    public bool SkipEverything => Rows.All(r => !r.IsVisible);

    public static async Task<PermissionStepModel> New(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        // Other permissions are requested contextually: camera on video start,
        // contacts in contact-related UIs, location on "Share live location".
        var permissionsUI = services.GetRequiredService<PermissionsUI>();
        var rows = new List<PermissionRow>();
        foreach (var permission in permissionsUI.Permissions) {
            if (!permission.IsInOnboarding)
                continue;

            var isGranted = await permission.Check(cancellationToken).ConfigureAwait(false);
            rows.Add(new PermissionRow(permissionsUI, permission) { IsVisible = !isGranted });
        }

        var m = new PermissionStepModel(services);
        m.Rows = rows;
        return m;
    }

    public void MarkCompleted()
    {
        var onboardingUI = Services.GetRequiredService<OnboardingUI>();
        onboardingUI.UpdateLocalSettings(onboardingUI.LocalSettings.Value with {
            IsPermissionsStepCompleted = true,
        });
    }
}

public sealed class PermissionRow(PermissionsUI permissionsUI, PermissionDef definition)
{
    public string Title { get; } = definition.Title;
    public string Rationale { get; } = definition.Rationale;
    public string Icon { get; } = definition.Icon;
    public bool IsVisible { get; init; }
    public bool IsGranted { get; set; }

    public Task<bool> Request(CancellationToken cancellationToken = default)
        => permissionsUI.Request(definition, false, cancellationToken);
}
