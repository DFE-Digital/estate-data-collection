using EstateDataCollection.Common.Extensions;

namespace EstateDataCollection.Common.Results;

public sealed record Error
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public string Code { get; }
    public string Description { get; }
    public ErrorType Type { get; }
    public IReadOnlyList<ValidationError> ValidationErrors { get; }

    private Error(
        string code,
        string? description,
        ErrorType type,
        IReadOnlyList<ValidationError>? validationErrors = null)
    {
        Code = code;
        Description = description ?? string.Empty;
        Type = type;
        ValidationErrors = validationErrors ?? [];
    }

    public static Error Failure(string code, string? description = null)
    {
        return new Error(code, description, ErrorType.Failure);
    }

    public static Error Failure(ErrorCodes code, object? args = null)
    {
        return new Error(code.GetName(), code.GetMessage(args), ErrorType.Failure);
    }

    public static Error NotFound(string code, string? description = null)
    {
        return new Error(code, description, ErrorType.NotFound);
    }

    public static Error NotFound(ErrorCodes code, object? args = null)
    {
        return new Error(code.GetName(), code.GetMessage(args), ErrorType.NotFound);
    }

    public static Error Conflict(string code, string? description = null)
    {
        return new Error(code, description, ErrorType.Conflict);
    }

    public static Error Conflict(ErrorCodes code, object? args = null)
    {
        return new Error(code.GetName(), code.GetMessage(args), ErrorType.Conflict);
    }

    public static Error Unauthorized(string code, string? description = null)
    {
        return new Error(code, description, ErrorType.Unauthorized);
    }

    public static Error Unauthorized(ErrorCodes code, object? args = null)
    {
        return new Error(code.GetName(), code.GetMessage(args), ErrorType.Unauthorized);
    }

    public static Error Forbidden(string code, string? description = null)
    {
        return new Error(code, description, ErrorType.Forbidden);
    }

    public static Error Forbidden(ErrorCodes code, object? args = null)
    {
        return new Error(code.GetName(), code.GetMessage(args), ErrorType.Forbidden);
    }

    public static Error Unexpected(string code, string? description = null)
    {
        return new Error(code, description, ErrorType.Unexpected);
    }

    public static Error Unexpected(ErrorCodes code, object? args = null)
    {
        return new Error(code.GetName(), code.GetMessage(args), ErrorType.Unexpected);
    }

    public static Error Validation(string code, string? description = null,
        IReadOnlyList<ValidationError>? errors = null)
    {
        return new Error(code, description, ErrorType.Validation, errors);
    }

    public static Error Validation(ErrorCodes code, object? args = null, IReadOnlyList<ValidationError>? errors = null)
    {
        return new Error(code.GetName(), code.GetMessage(args), ErrorType.Validation, errors);
    }
}