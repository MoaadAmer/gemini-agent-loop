using Google.GenAI;
using Google.GenAI.Types;
using Type = Google.GenAI.Types.Type;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;

public class AgentOrchestrator
{
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly LocalTools _localTools;

    public AgentOrchestrator(ILogger<AgentOrchestrator> logger, LocalTools tools)
    {
        _logger = logger;
        _localTools = tools;
    }

    public async Task RunAsync()
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

            int iteration = 0;
            int maxIterations = 5;

            _logger.LogInformation("User Question: 'Check what time it is right now, and then check the weather in Tokyo.'");
            _logger.LogInformation("\n--- Starting Agentic Loop ---");
            while (iteration < maxIterations)
            {
                iteration++;
                _logger.LogInformation("\n[Iteration {iteration}] Sending history to Gemini...", iteration);

                var response = await client.Models.GenerateContentAsync(model, history, config);

                if (response.Candidates != null && response.Candidates.Count > 0 && response.Candidates[0].Content != null)
                {
                    var assistantContent = response.Candidates[0].Content;
                    history.Add(assistantContent);
                }

                var functionCalls = response.FunctionCalls;

                if (functionCalls != null && functionCalls.Count > 0)
                {
                    _logger.LogInformation("-> Gemini requested {functionCalls.Count} tool call(s).", functionCalls.Count);
                    var functionResponseParts = new List<Part>();

                    foreach (var call in functionCalls)
                    {
                        _logger.LogInformation("-> Gemini wants to run: {call.Name}", call.Name);
                        string resultOutput = string.Empty;

                        if (call.Name == "GetCurrentTime")
                        {
                            resultOutput = _localTools.GetCurrentTime();
                        }
                        else if (call.Name == "GetWeather")
                        {
                            if (call.Args != null && call.Args.TryGetValue("city", out var cityObj))
                            {
                                string city = cityObj?.ToString() ?? "Unknown";
                                resultOutput = _localTools.GetWeather(city);
                            }
                        }
                        else
                        {
                            resultOutput = $"Error: Tool '{call.Name}' not recognized.";
                        }

                        _logger.LogInformation("-> Tool result obtained: {resultOutput}", resultOutput);

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
                    _logger.LogInformation("-> No more tool calls requested. Agent has reached its goal.");
                    _logger.LogInformation("\n--- Final Structured Output Reached (DOD Met) ---");
                    _logger.LogInformation("\n--- Loop Completed ---");

                    if (string.IsNullOrWhiteSpace(response.Text))
                    {
                        _logger.LogError("Model did not return valid JSON");
                        return;
                    }
                    else
                    {
                        try
                        {
                            var finalResponse = JsonSerializer.Deserialize<FinalAgentResponse>(
                                response.Text,
                                new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                });

                            if (finalResponse == null)
                            {

                                _logger.LogError("Deserialization returned null");
                                return;
                            }


                            if (string.IsNullOrWhiteSpace(finalResponse.ExecutionSummary))
                            {
                                _logger.LogError("Missing execution_summary");
                                return;
                            }

                            if (finalResponse.ToolsUsed == null)
                            {
                                _logger.LogError("Missing tools_used");
                                return;
                            }

                            if (!response.Text.Contains("is_success"))
                            {
                                _logger.LogError("Missing is_success field");
                                return;
                            }

                            _logger.LogInformation("Execution Summary: {summary}", finalResponse.ExecutionSummary);
                            _logger.LogInformation("Tools Used: {tools}", string.Join(", ", finalResponse.ToolsUsed ?? []));
                            _logger.LogInformation("Success: {success}", finalResponse.IsSuccess);

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("JSON parsing failed: {error}", ex.Message);
                            _logger.LogError("Raw response: {raw}", response.Text);
                            return;
                        }
                    }
                    return;

                }
            }


        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
