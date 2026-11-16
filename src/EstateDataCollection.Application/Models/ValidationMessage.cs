namespace EstateDataCollection.Application.Models;

public record ValidationMessage(Type? EngineType, string? Identifier, string Message)
{
    public static implicit operator ValidationMessage(string message)
    {
        return new ValidationMessage(null, null, message);
    }
}
