using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SubmissionSessionBuilder
{
    private string _schemaName = "edc_payload";
    private int _schemaVersion = 1;
    private int? _urn;
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

    public SubmissionSessionBuilder WithUrn(int urn)
    {
        _urn = urn;
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
        if (_urn.HasValue)
        {
            session.AssignUrn(_urn.Value);
        }

        if (_finalised)
        {
            session.Finalise();
        }

        return session;
    }
}
