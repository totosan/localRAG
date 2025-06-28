using Microsoft.SemanticKernel;

namespace localRAG.Process.Steps
{
#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class RenderResponsesStep : KernelProcessStep
    {
        public static class Functions
        {
            public const string RenderResponses = nameof(RenderResponses);
        }

        [KernelFunction(Functions.RenderResponses)]
        public async Task RenderResponsesAsync(KernelProcessStepContext context, string? responseToRender, Kernel _kernel)
        {
            Console.WriteLine("[DEBUG] Step: RenderResponsesStep - RenderResponsesAsync called");
            if (responseToRender != null)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine($"ASSISTANT: {responseToRender}");
                System.Console.ResetColor();
            }
        }
    }
}
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.