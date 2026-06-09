using System.Text.Json.Serialization;

public class FinalAgentResponse
{
    [JsonPropertyName("execution_summary")]
    public string ExecutionSummary { get; set; } = string.Empty;

    [JsonPropertyName("tools_used")]
    public List<string> ToolsUsed { get; set; } = new();

    [JsonPropertyName("is_success")]
    public bool IsSuccess { get; set; }
}
