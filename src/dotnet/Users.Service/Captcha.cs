using ActualChat.Module;
using ActualChat.Users.Module;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.RecaptchaEnterprise.V1;
using Grpc.Core;

namespace ActualChat.Users;

public class Captcha(UsersSettings settings, CoreServerSettings serverSettings, UrlMapper urlMapper, ILogger<Captcha> log) : ICaptcha
{
    private readonly RecaptchaEnterpriseServiceClient _client = RecaptchaEnterpriseServiceClient.Create();
    private readonly ProjectName _projectName = new (serverSettings.GoogleProjectId);
    private UsersSettings Settings { get; } = settings;
    private UrlMapper UrlMapper { get; } = urlMapper;
    private ILogger<Captcha> Log { get; } = log;

    public virtual async Task<RecaptchaValidationResult> Validate(string token, string action, CancellationToken cancellationToken)
    {
        // Skip CAPTCHA validation on local development (localhost, local.voxt.ai)
        if (IsLocalDevBypassAllowed())
            return new RecaptchaValidationResult(true, null, 1.0f);

        if (Settings.GoogleRecaptchaSiteKey.IsNullOrEmpty())
            return new RecaptchaValidationResult(false, "reCAPTCHA is not configured.");

        var request = new CreateAssessmentRequest {
            Assessment = new Assessment {
                Event = new Event {
                    SiteKey = Settings.GoogleRecaptchaSiteKey,
                    Token = token,
                    ExpectedAction = action
                },
            },
            ParentAsProjectName = _projectName
        };
        try {
            var response = await _client.CreateAssessmentAsync(request, cancellationToken).ConfigureAwait(false);
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

    private bool IsLocalDevBypassAllowed()
    {
        // Check if bypass is explicitly enabled via environment variable
        var bypassEnvVar = Environment.GetEnvironmentVariable("ActualChat_CaptchaBypassEnabled");
        if (!bool.TryParse(bypassEnvVar, out var bypassEnabled) || !bypassEnabled)
            return false;

        var host = UrlMapper.BaseUri.Host;
        // Bypass CAPTCHA on local development hosts (not on prod or dev domains)
        return !Constants.Hosts.AllProd.Contains(host)
            && !Constants.Hosts.AllDev.Contains(host);
    }
}
