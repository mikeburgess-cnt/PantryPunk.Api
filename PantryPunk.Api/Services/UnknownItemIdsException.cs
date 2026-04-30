namespace PantryPunk.Api.Services;

public class UnknownItemIdsException : Exception
{
    public UnknownItemIdsException(string message) : base(message) { }
}
