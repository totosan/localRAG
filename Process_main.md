# Main Process Flow: User Input as a Process Step

**Related documentation:**

- [README](README.md)
- [Solution Overview](solution.md)
- [Solution Flowcharts](solution_flowchart.md)
- [Datasource Maintenance Process](process_datasource_maintenance.md)
- [Tag Generation & Usage](CREATE_TAG.md)

This document describes the main workflow of the localRAG application, focusing on how user input is handled as a process step. The flow is orchestrated using a process builder pattern, with each step representing a distinct part of the Retrieval Augmented Generation (RAG) pipeline.

## Overview

- The process is defined and started in `Program.cs`.
- The workflow is constructed in `ProcessSearch.cs` using a `ProcessBuilder`.
- Each step in the process corresponds to a specific function, such as rewriting the user's question, routing, searching memory, and rendering responses.
- **New:** If no context is found for a user question, the system uses a self-critique LLM prompt to fact-check the answer and warn about possible hallucinations.

## Main Steps in the Process

1. **Start Process**

   - The process is initiated, typically in response to a user action or system event.

2. **Get User's Chat Input**

   - The user provides input, which is captured as a process step (`ChatUserInputStep`).
   - The system may loop here for additional input or exit based on user actions.

3. **Rewrite User's Ask**

   - The user's input is rewritten or clarified for better processing (`RewriteAskStep`).

4. **Routing**

   - The rewritten input is routed to determine if a RAG search is needed or if a direct chat response suffices (`RoutingStep`).

5. **RAG Search (if needed)**

   - If a RAG search is required, the system:
     - Determines the intent of the ask (`LookupKernelmemoriesStep`)
     - Retrieves relevant memory data

6. **Get Chat Response**

   - Based on the routing decision, the system generates a response using either retrieved memory or chat history (`ResponseStep`).
   - **New:** If no context chunks are found, the system runs a self-critique LLM prompt to check if the answer is factual and verifiable. If not, a warning is added to the response.

7. **Render Response**

   - The final response is rendered and presented to the user (`RenderResponsesStep`).

8. **Loop or Exit**
   - The process may loop back to get more user input or exit if the conversation is complete.

## Process Diagram (Textual)

```
[Start Process]
     |
     v
[Get User's Chat Input]
     |
     v
[Rewrite User's Ask]
     |
     v
[Routing]
  /     \
 v       \
[RAG]     `--------->[No RAG]
  |                     |
  v                     v
[Get Memory Data]   [Get Chat Response]
  |                     |
  v                     |
[Get Chat Response]     |
     |                  /
     v                 /
[Self-Critique LLM Fact-Check?]  # <--- New step for no-context answers
     |
     v
[Render Response]<----´
     |
     v
[Loop or Exit]
```

## References

- **Process Definition:** See `ProcessSearch.cs` for the process builder and step wiring.
- **Process Start:** See `Program.cs` for process instantiation and execution.
- **Pipeline Handlers:** See `PipelineHandler/` for custom handlers used in tagging and memory operations.

---

This document provides a high-level overview of the main process flow, focusing on user input as a process step and its journey through the RAG pipeline.
