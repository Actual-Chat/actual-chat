using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Users.Module;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.RecaptchaEnterprise.V1;
using Grpc.Core;

namespace ActualChat.Users;

public class Captcha : ICaptcha
{
    private readonly Lazy<RecaptchaEnterpriseServiceClient> _client =
        new(RecaptchaEnterpriseServiceClient.Create);
    private readonly ProjectName _projectName;
    private readonly string _siteKey;
    private readonly bool _isAvailable;

    private UrlMapper UrlMapper { get; }
    private ILogger<Captcha> Log { get; }

    public static bool IsAvailable(HostInfo hostInfo, string? siteKey)
        => hostInfo.IsProductionInstance && !siteKey.IsNullOrEmpty();

    public Captcha(IServiceProvider services)
    {
        UrlMapper = services.UrlMapper();
        Log = services.LogFor<Captcha>();

        var hostInfo = services.HostInfo();
        var coreServerSettings = services.GetRequiredService<CoreServerSettings>();
        var userSettings = services.GetRequiredService<UsersSettings>();
        _projectName = new ProjectName(coreServerSettings.GoogleProjectId);
        _siteKey = userSettings.GoogleRecaptchaSiteKey;
        _isAvailable = IsAvailable(hostInfo, _siteKey);
    }

    public virtual async Task<RecaptchaValidationResult> Validate(string token, string action, CancellationToken cancellationToken)
    {
        // Skip CAPTCHA validation on non-production hosts
        if (!_isAvailable)
            return new RecaptchaValidationResult(token != "fake-fail", null, 1.0f);

        var request = new CreateAssessmentRequest {
            Assessment = new Assessment {
                Event = new Event {
                    SiteKey = _siteKey,
                    Token = token,
                    ExpectedAction = action,
                },
            },
            ParentAsProjectName = _projectName
        };
        try {
            var response = await _client.Value.CreateAssessmentAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.TokenProperties.Valid == false)
                return new RecaptchaValidationResult(false, response.TokenProperties.InvalidReason.ToString());

            var score = response.RiskAnalysis.Score;
            return new RecaptchaValidationResult(score >= Constants.Recaptcha.ValidScore, null, score);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied) {
            return new RecaptchaValidationResult(false, "reCAPTCHA is not configured.");
        }
        catch (Exception e) {
            Log.LogWarning(e, "Error validating reCAPTCHA token");
            return new RecaptchaValidationResult(false, "reCAPTCHA validation failed.");
        }
    }
}
