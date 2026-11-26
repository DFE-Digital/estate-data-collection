using EstateDataCollection.Common.Messages;

namespace EstateDataCollection.Common.Web.Extensions;

public sealed class RequestBodyTooLargeException(long maxSizeBytes)
    : InvalidOperationException(ErrorMessages.RequestBodyTooLarge(maxSizeBytes))
{
    public long MaxSizeBytes { get; } = maxSizeBytes;
    public int MaxSizeMb { get; } = Math.Max(1, (int)Math.Ceiling(maxSizeBytes / (1024d * 1024d)));
}