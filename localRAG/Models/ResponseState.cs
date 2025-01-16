using Microsoft.SemanticKernel.ChatCompletion;

namespace localRAG.Models;

public class ResponseState
{
    internal ChatHistory ChatMessages { get; } = new();
}