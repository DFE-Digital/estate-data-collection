using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SubmissionSessionBuilder
{
    private string _schemaName = "edc_payload";
    private int _schemaVersion = 1;
    private string? _identifierType;
    private string? _identifierValue;
    private bool _finalised;

    public SubmissionSessionBuilder WithSchema(string schemaName, int version = 1)
    {
        _schemaName = schemaName;
        _schemaVersion = version;
        return this;
    }

    public SubmissionSessionBuilder WithSchemaName(string schemaName)
    {
        _schemaName = schemaName;
        return this;
    }

    public SubmissionSessionBuilder WithVersion(int version)
    {
        _schemaVersion = version;
        return this;
    }

    public SubmissionSessionBuilder WithIdentifier(string type, string value)
    {
        _identifierType = type;
        _identifierValue = value;
        return this;
    }

    public SubmissionSessionBuilder Finalised()
    {
        _finalised = true;
        return this;
    }

    public SubmissionSession Build()
    {
        var session = SubmissionSession.Create(_schemaName, _schemaVersion);
        if (!string.IsNullOrWhiteSpace(_identifierValue))
        {
            session.AssignIdentifier(_identifierType, _identifierValue);
        }

        if (_finalised)
        {
            session.Finalise();
        }

        return session;
    }
}
