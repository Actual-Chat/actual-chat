using ActualChat.Module;
using ActualChat.Users.Module;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.RecaptchaEnterprise.V1;
using Grpc.Core;

namespace ActualChat.Users;

public class Captcha(UsersSettings settings, CoreServerSettings serverSettings, ILogger<Captcha> log) : ICaptcha, IDisposable
{
    private readonly RecaptchaEnterpriseServiceClient _client = RecaptchaEnterpriseServiceClient.Create();
    private readonly ProjectName _projectName = new (serverSettings.GoogleProjectId);
    private UsersSettings Settings { get; } = settings;
    private ILogger<Captcha> Log { get; } = log;

    public virtual async Task<RecaptchaValidationResult> Validate(string token, string action, CancellationToken cancellationToken)
    {
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

    public void Dispose()
    { }
}
