using System.Text.Json.Serialization;

namespace PetOverlay;

public class PromptRequest
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "yesno";

    [JsonPropertyName("options")]
    public string[]? Options { get; set; }

    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 300;
}

public class PromptResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "cancelled";

    [JsonPropertyName("answer")]
    public string? Answer { get; set; }
}
