namespace HemenIlanVer.Application.Exceptions;

public sealed class InvalidListingPromptException(string reason)
    : Exception(reason)
{
    public string Reason { get; } = reason;
}
