# Structured Output Implementation for Ollama with Semantic Kernel

## Overview
This document describes the implementation of structured JSON outputs in the localRAG project, ensuring that LLM responses conform to a predefined JSON schema. This is particularly important for the RewriteAsk step, where we need reliable JSON array responses.

## Changes Made

### 1. Updated StepRewriteAsk.cs
**Location**: `/Process/Steps/StepRewriteAsk.cs`

#### Key Modifications:

1. **Added OpenAI Connector Import**
   ```csharp
   using Microsoft.SemanticKernel.Connectors.OpenAI;
   ```

2. **Simplified Input Processing**
   - Changed from processing entire chat history to only rewriting the last user question
   - Replaced: `var messages = Helpers.ChatHistoryToString(chatHist, userInput);`
   - With: `var messages = userInput;`
   - Added comment explaining the change

3. **Implemented JSON Schema Definition**
   ```csharp
   var jsonSchema = new
   {
       type = "array",
       items = new
       {
           type = "object",
           properties = new
           {
               StandaloneQuestion = new { type = "string" },
               Score = new { type = "number" }
           },
           required = new[] { "StandaloneQuestion", "Score" }
       }
   };
   ```

4. **Added Execution Settings with Structured Output Support**
   ```csharp
   var executionSettings = new OpenAIPromptExecutionSettings
   {
       Temperature = 0.0,  // Lower temperature for more deterministic output
       ResponseFormat = "json_object"  // For OpenAI-compatible endpoints
   };
   ```

5. **Ollama-Specific Format Parameter**
   - Detects Ollama usage via environment variable
   - Sets the `format` parameter in ExtensionData for Ollama's API
   ```csharp
   bool useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA")?.ToLower() == "true";
   if (useOllama)
   {
       executionSettings.ExtensionData = new Dictionary<string, object>
       {
           ["format"] = jsonSchema
       };
   }
   ```

6. **Updated Kernel Invocation**
   ```csharp
   var userask = await kernel.InvokeAsync<string>(
       rewriteUserAskPrompt, 
       new KernelArguments(executionSettings) 
       { 
           ["question"] = messages 
       });
   ```

## How It Works

### For Ollama (OpenAI-Compatible API)
When running with `--ollama` flag:
1. The `USE_OLLAMA` environment variable is set to "true"
2. The code detects this and adds the JSON schema to `ExtensionData["format"]`
3. Semantic Kernel's OpenAI connector passes this to Ollama's `/v1/chat/completions` endpoint
4. Ollama enforces the schema and returns structured JSON matching the format

### For Azure OpenAI
When running without `--ollama`:
1. Uses standard `ResponseFormat = "json_object"`
2. Azure OpenAI's JSON mode ensures valid JSON output
3. The prompt template guides the model to follow the schema structure

## Benefits

1. **Reliability**: Eliminates malformed responses (HTML, plain text, or invalid JSON)
2. **Determinism**: Lower temperature (0.0) + schema enforcement = consistent output
3. **Robustness**: Fallback mechanisms still in place if schema validation fails
4. **Compatibility**: Works with both Ollama and Azure OpenAI backends

## Testing

To test the structured output implementation:

```bash
# With Ollama
dotnet run -- --ollama

# With Ollama and fresh import
dotnet run -- --ollama --import

# With Azure OpenAI (default)
dotnet run
```

## Schema Details

The expected response format:
```json
[
  {
    "StandaloneQuestion": "What is the capital of Germany?",
    "Score": 10
  },
  {
    "StandaloneQuestion": "Berlin as Germany's capital city?",
    "Score": 8
  }
]
```

## References

- [Ollama Structured Outputs Documentation](https://docs.ollama.com/capabilities/structured-outputs)
- [Semantic Kernel OpenAI Connector](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/)
- [JSON Schema Specification](https://json-schema.org/)

## Future Enhancements

1. Apply structured outputs to other plugins (IntentsPlugin, HallucinationCheckPlugin)
2. Add Pydantic/Zod-style model validation classes
3. Implement retry logic with schema adjustment on validation failures
4. Add metrics/logging for schema compliance rates
