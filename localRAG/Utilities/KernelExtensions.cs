// Copyright (c) Microsoft. All rights reserved.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;


namespace localRAG.Utilities;

/// <summary>
/// Convenience extensions for agent based process patterns.
/// </summary>
internal static class KernelExtensions
{
    /// <summary>
    /// Return chat history from a singleton <see cref="IChatHistoryProvider"/>.
    /// </summary>
    public static IChatHistoryProvider GetHistory(this Kernel kernel) =>
        kernel.Services.GetRequiredService<IChatHistoryProvider>();

}
