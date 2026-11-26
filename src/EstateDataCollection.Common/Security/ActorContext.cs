namespace EstateDataCollection.Common.Security;

/// <summary>
/// Default scoped implementation of <see cref="IActorContext"/> populated per HTTP invocation
/// by the Public API authentication/actor middleware.
/// </summary>
public sealed class ActorContext : IActorContext
{
    private Dictionary<string, string> _metadata = new(StringComparer.Ordinal);

    public bool IsAuthenticated { get; private set; }
    public string? ActorId { get; private set; }
    public string? ActorName { get; private set; }
    public string? SubscriptionId { get; private set; }
    public string? SubscriptionName { get; private set; }
    public string AuthenticationType { get; private set; } = "Anonymous";

    public IReadOnlyDictionary<string, string> Metadata
    {
        get { return _metadata; }
    }

    public string? AuditActorDisplay
    {
        get
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(ActorName) &&
                !string.IsNullOrWhiteSpace(ActorId) &&
                !string.Equals(ActorName, ActorId, StringComparison.Ordinal))
            {
                return $"{ActorName} ({ActorId})";
            }

            return !string.IsNullOrWhiteSpace(ActorName)
                ? ActorName
                : ActorId;
        }
    }

    public void SetActor(
        string actorId,
        string authenticationType,
        string? actorName = null,
        string? subscriptionId = null,
        string? subscriptionName = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);

        IsAuthenticated = true;
        ActorId = actorId.Trim();
        AuthenticationType = authenticationType.Trim();
        ActorName = string.IsNullOrWhiteSpace(actorName) ? null : actorName.Trim();
        SubscriptionId = string.IsNullOrWhiteSpace(subscriptionId) ? null : subscriptionId.Trim();
        SubscriptionName = string.IsNullOrWhiteSpace(subscriptionName) ? null : subscriptionName.Trim();
        _metadata = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
    }

    public void Reset()
    {
        IsAuthenticated = false;
        ActorId = null;
        ActorName = null;
        SubscriptionId = null;
        SubscriptionName = null;
        AuthenticationType = "Anonymous";
        _metadata = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
