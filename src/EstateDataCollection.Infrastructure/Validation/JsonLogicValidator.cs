using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Validation;

/// <summary>
/// Backwards-compatible alias for <see cref="QualityRuleCoordinator"/>.
/// </summary>
public class JsonLogicValidator : QualityRuleCoordinator
{
    public JsonLogicValidator(
        IEnumerable<IQualityRuleEngine> engines,
        IEnumerable<IRuleContextEnricher>? contextEnrichers,
        IOptions<ValidationOptions>? options = null,
        QualityRuleCoordinatorInstrumentation? instrumentation = null)
        : base(engines, contextEnrichers, options, instrumentation)
    {
    }

    public JsonLogicValidator(
        IEnumerable<IQualityRuleEngine> engines,
        IOptions<ValidationOptions>? options = null,
        JsonLogicValidatorInstrumentation? instrumentation = null)
        : base(engines, contextEnrichers: null, options, instrumentation: null)
    {
        _ = instrumentation;
    }

    public JsonLogicValidator(JsonLogicValidatorInstrumentation? instrumentation = null)
        : base([], contextEnrichers: null, options: null, instrumentation: null)
    {
        _ = instrumentation ?? new JsonLogicValidatorInstrumentation(NullLogger<JsonLogicValidator>.Instance);
    }
}
