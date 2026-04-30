namespace PantryPunk.Api.Services;

public class EmptyListException : Exception
{
    public EmptyListException(string message) : base(message) { }
}
