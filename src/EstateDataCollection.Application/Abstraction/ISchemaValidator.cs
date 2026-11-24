using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using Json.Schema;

namespace EstateDataCollection.Application.Abstraction;

public interface ISchemaValidator : IInfrastructure
{
    ValidationEngineDescriptor Descriptor
    {
        get
        {
            return new ValidationEngineDescriptor(
                "json-schema",
                "JSON Schema Validator",
                ValidationEngineKinds.StructuralSchema,
                "Structural JSON Schema validator.");
        }
    }

    Task<Result<SchemaValidationResult>> ValidateAsync(string payload, JsonSchema schema,
        CancellationToken cancellationToken = default);
}
