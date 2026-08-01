using System.Net;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Decision returned by <see cref="ByokFailureHandler"/> when a BYOK credential failure occurs.
/// Specifies the action the orchestrator should take next and an optional suggested provider.
/// </summary>
public class ByokFailureDecision
{
    /// <summary>Recommended next action.</summary>
    public string Action { get; set; } = Actions.Stop;

    /// <summary>Machine-readable reason for the decision (mapped from the failure).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Suggested provider ID for fallback or retry, if applicable.</summary>
    public string? SuggestedProviderId { get; set; }

    /// <summary>Action constants for BYOK failure decisions.</summary>
    public static class Actions
    {
        /// <summary>Retry the request with a different credential for the same provider.</summary>
        public const string RetryWithDifferentCredential = "retry_with_different_credential";

        /// <summary>Fall back to a local on-device provider.</summary>
        public const string FallbackToLocal = "fallback_to_local";

        /// <summary>Fall back to a platform-managed credential/provider.</summary>
        public const string FallbackToPlatform = "fallback_to_platform";

        /// <summary>Stop the pipeline; no further fallback attempts.</summary>
        public const string Stop = "stop";
    }

    /// <summary>Reason constants mapped from HTTP status codes.</summary>
    public static class Reasons
    {
        /// <summary>Credential is invalid (HTTP 401).</summary>
        public const string CredentialInvalid = "credential_invalid";

        /// <summary>Credential has been revoked or lacks permission (HTTP 403).</summary>
        public const string CredentialRevoked = "credential_revoked";

        /// <summary>Provider rate limit exceeded (HTTP 429).</summary>
        public const string RateLimited = "rate_limited";

        /// <summary>Insufficient balance or quota on the provider account (HTTP 402).</summary>
        public const string InsufficientBalance = "insufficient_balance";

        /// <summary>Network or connectivity error.</summary>
        public const string NetworkError = "network_error";

        /// <summary>Unknown or unclassified error.</summary>
        public const string Unknown = "unknown_error";
    }
}

/// <summary>
/// Handles BYOK (Bring Your Own Key) credential failures by mapping HTTP status codes
/// to actionable decisions (retry, fallback, or stop).
/// </summary>
public class ByokFailureHandler
{
    private readonly ILogger<ByokFailureHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ByokFailureHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public ByokFailureHandler(ILogger<ByokFailureHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes a BYOK credential failure and returns a decision on how to proceed.
    /// Maps HTTP status codes as follows:
    /// <list type="table">
    /// <item><term>401</term><description>credential_invalid → retry_with_different_credential</description></item>
    /// <item><term>403</term><description>credential_revoked → retry_with_different_credential</description></item>
    /// <item><term>429</term><description>rate_limited → fallback_to_platform</description></item>
    /// <item><term>402</term><description>insufficient_balance → fallback_to_platform</description></item>
    /// </list>
    /// </summary>
    /// <param name="providerId">The provider that failed.</param>
    /// <param name="credentialMode">The credential mode in use (USER_BYOK, TENANT_BYOK, PLATFORM_MANAGED).</param>
    /// <param name="failure">The exception that caused the failure.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ByokFailureDecision"/> with the recommended next action.</returns>
    public Task<ByokFailureDecision> HandleFailureAsync(
        string providerId, string credentialMode, Exception failure, CancellationToken ct)
    {
        var (reason, action) = ClassifyFailure(failure, credentialMode);

        var decision = new ByokFailureDecision
        {
            Action = action,
            Reason = reason
        };

        // For fallback decisions, suggest a local or platform provider.
        if (action == ByokFailureDecision.Actions.FallbackToLocal)
        {
            decision.SuggestedProviderId = "whisper-cpp";
        }
        else if (action == ByokFailureDecision.Actions.FallbackToPlatform)
        {
            decision.SuggestedProviderId = "memorix-cloud";
        }

        _logger.LogWarning(
            "BYOK failure handled: provider={ProviderId}, credentialMode={CredentialMode}, " +
            "reason={Reason}, action={Action}, suggestedProvider={SuggestedProvider}",
            providerId, credentialMode, decision.Reason, decision.Action,
            decision.SuggestedProviderId);

        return Task.FromResult(decision);
    }

    /// <summary>
    /// Maps an exception to a (reason, action) tuple based on HTTP status codes
    /// and credential mode.
    /// </summary>
    private (string reason, string action) ClassifyFailure(
        Exception failure, string credentialMode)
    {
        // Extract HTTP status code from HttpRequestException.
        HttpStatusCode? statusCode = null;

        if (failure is HttpRequestException httpEx)
        {
            statusCode = httpEx.StatusCode;
        }

        // Also check for inner HttpRequestException (e.g. wrapped by a provider adapter).
        if (statusCode == null && failure.InnerException is HttpRequestException innerHttpEx)
        {
            statusCode = innerHttpEx.StatusCode;
        }

        if (statusCode.HasValue)
        {
            var code = (int)statusCode.Value;

            return code switch
            {
                401 => (ByokFailureDecision.Reasons.CredentialInvalid,
                        ByokFailureDecision.Actions.RetryWithDifferentCredential),

                403 => (ByokFailureDecision.Reasons.CredentialRevoked,
                        ByokFailureDecision.Actions.RetryWithDifferentCredential),

                429 => (ByokFailureDecision.Reasons.RateLimited,
                        DecideFallback(credentialMode)),

                402 => (ByokFailureDecision.Reasons.InsufficientBalance,
                        DecideFallback(credentialMode)),

                _ => (ByokFailureDecision.Reasons.Unknown,
                      ByokFailureDecision.Actions.Stop),
            };
        }

        // Network or timeout errors: attempt fallback if credential mode is BYOK.
        if (failure is TimeoutException || failure is TaskCanceledException)
        {
            return (ByokFailureDecision.Reasons.NetworkError,
                    DecideFallback(credentialMode));
        }

        // Unclassified exception: stop to be safe.
        return (ByokFailureDecision.Reasons.Unknown,
                ByokFailureDecision.Actions.Stop);
    }

    /// <summary>
    /// Determines the appropriate fallback action based on the current credential mode.
    /// If already using PLATFORM_MANAGED credentials, there is nowhere to fall back to.
    /// </summary>
    private static string DecideFallback(string credentialMode)
    {
        // If the failure occurred with platform-managed credentials, we cannot
        // fall back further; stop the pipeline.
        if (string.Equals(credentialMode, "PLATFORM_MANAGED",
            StringComparison.OrdinalIgnoreCase))
        {
            return ByokFailureDecision.Actions.Stop;
        }

        // For BYOK failures, fall back to platform-managed credentials.
        return ByokFailureDecision.Actions.FallbackToPlatform;
    }
}
