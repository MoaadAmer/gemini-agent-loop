using Google.GenAI;
using Google.GenAI.Types;
using Type = Google.GenAI.Types.Type;
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

public class LocalTools
{
    // private readonly ILogger<LocalTools> _logger;

    // public LocalTools(ILogger<LocalTools> logger)
    // {
    //     _logger = logger;
    // }
    public string GetWeather(string city)
    {

        Console.WriteLine($"   [TOOL EXECUTION] -> Running GetWeather() for '{city}'...");
        city = city.ToLower();
        return city switch
        {
            "tokyo" => "Sunny, 26°C",
            "london" => "Rainy, 14°C",
            _ => "Cloudy, 20°C",
        };
    }
    public string GetCurrentTime()
    {
        Console.WriteLine("   [TOOL EXECUTION] -> Running GetCurrentTime()...");
        return DateTime.Now.ToString("HH:mm:ss");
    }

}

class Program
{
    static async Task Main()
    {
        try
        {
            var client = new Client();
            var model = "gemini-3.5-flash";

            var history = new List<Content>
            {
                new() {
                    Role="user",
                    Parts = [new() {
                        Text = """
                                You are a helpful assistant.
                                You must use tools sequentially to gather facts.
                                Once you have all info, provide your final response matching the requested JSON schema.
                               """
                         }
                     ]
                },
                new() {
                    Role = "user",
                    Parts = [new() { Text = "Check what time it is right now, and then check the weather in Tokyo." }]
                }
            };
            var schema = new Schema
            {
                Type = Type.Object,
                Properties = new Dictionary<string, Schema>
                    {
                         { "execution_summary", new Schema { Type = Type.String, Description = "The final answered response summarizing all gathered facts." } },
                         { "tools_used", new Schema {
                                        Type = Type.Array,
                                        Items = new Schema { Type = Type.String },
                                        Description = "List of names of the tools that were utilized during execution loop cycles."
                                      }
                        },
                        { "is_success", new Schema { Type = Type.Boolean, Description = "True if the user's requirement was completely resolved." } }
                    },
                Required = ["execution_summary", "tools_used", "is_success"]
            };


            var config = new GenerateContentConfig
            {
                Tools =
                [
                    new Tool
                    {
                        FunctionDeclarations = new List<FunctionDeclaration>
                        {
                            new() {
                                Name = "GetCurrentTime",
                                Description = "Gets the current system time."
                            },
                            new() {
                                Name = "GetWeather",
                                Description = "Gets the current weather for a specific city.",
                                Parameters = new Schema
                                {
                                    Type = Type.Object,
                                    Properties = new Dictionary<string, Schema>
                                    {
                                        { "city", new Schema { Type = Type.String, Description = "The name of the city, e.g., Tokyo, London" } }
                                    },
                                    Required = ["city"]
                                }
                            }
                        }
                    }
                ],
                ResponseMimeType = "application/json",
                ResponseSchema = schema,
                Temperature = 0.0f
            };

            bool isRunning = true;
            int iteration = 0;
            int maxIterations = 5;
            var tools = new LocalTools();

            Console.WriteLine("User Question: 'Check what time it is right now, and then check the weather in Tokyo.'");
            Console.WriteLine("\n--- Starting Agentic Loop ---");
            while (isRunning && iteration < maxIterations)
            {
                iteration++;
                Console.WriteLine($"\n[Iteration {iteration}] Sending history to Gemini...");

                var response = await client.Models.GenerateContentAsync(model, history, config);

                if (response.Candidates != null && response.Candidates.Count > 0 && response.Candidates[0].Content != null)
                {
                    var assistantContent = response.Candidates[0].Content;
                    history.Add(assistantContent);
                }

                var functionCalls = response.FunctionCalls;

                if (functionCalls != null && functionCalls.Count > 0)
                {
                    Console.WriteLine($" -> Gemini requested {functionCalls.Count} tool call(s).");
                    var functionResponseParts = new List<Part>();

                    foreach (var call in functionCalls)
                    {
                        Console.WriteLine($" -> Gemini wants to run: {call.Name}");
                        string resultOutput = string.Empty;

                        if (call.Name == "GetCurrentTime")
                        {
                            resultOutput = tools.GetCurrentTime();
                        }
                        else if (call.Name == "GetWeather")
                        {
                            if (call.Args != null && call.Args.TryGetValue("city", out var cityObj))
                            {
                                string city = cityObj?.ToString() ?? "Unknown";
                                resultOutput = tools.GetWeather(city);
                            }
                        }
                        else
                        {
                            resultOutput = $"Error: Tool '{call.Name}' not recognized.";
                        }

                        Console.WriteLine($" -> Tool result obtained: {resultOutput}");

                        functionResponseParts.Add(Part.FromFunctionResponse(call.Name, new Dictionary<string, object>
                        {
                            { "result", resultOutput }
                        }));
                    }

                    history.Add(new Content
                    {
                        Role = "function",
                        Parts = functionResponseParts
                    });
                }
                else
                {
                    Console.WriteLine(" -> No more tool calls requested. Agent has reached its goal.");
                    Console.WriteLine("\n--- Final Structured Output Reached (DOD Met) ---");

                    Console.WriteLine(response.Text);
                    isRunning = false;
                }
            }
            Console.WriteLine("\n--- Loop Completed ---");


        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

    }

}