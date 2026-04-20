namespace PantryPunk.Api.Models.Responses;

public class ErrorResponse
{
    public string Error { get; set; } = null!;
    public string? TraceId { get; set; }
}
