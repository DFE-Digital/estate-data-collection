using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

internal static class ValidationEngineTraceMapper
{
    public static IReadOnlyList<SchemaEngineTraceRecord>? ToDomainRecords(
        IReadOnlyList<ValidationEngineTrace>? traces)
    {
        if (traces is not { Count: > 0 })
        {
            return null;
        }

        return traces
            .Select(t => new SchemaEngineTraceRecord(
                EngineId: t.Descriptor.EngineId,
                DisplayName: t.Descriptor.DisplayName,
                Kind: t.Descriptor.Kind,
                Description: t.Descriptor.Description,
                Outcome: t.Outcome,
                ItemsEvaluated: t.ItemsEvaluated,
                ViolationCount: t.ViolationCount,
                DurationMs: t.DurationMs,
                Summary: t.Summary))
            .ToArray();
    }

    public static IReadOnlyList<ValidationEngineTrace> ToApplicationTraces(
        IReadOnlyList<SchemaEngineTraceRecord>? records)
    {
        if (records is not { Count: > 0 })
        {
            return [];
        }

        return records
            .Select(r => new ValidationEngineTrace(
                Descriptor: new ValidationEngineDescriptor(
                    EngineId: r.EngineId,
                    DisplayName: r.DisplayName,
                    Kind: r.Kind,
                    Description: r.Description),
                Outcome: r.Outcome,
                ItemsEvaluated: r.ItemsEvaluated,
                ViolationCount: r.ViolationCount,
                DurationMs: r.DurationMs,
                Summary: r.Summary))
            .ToArray();
    }
}
