# tags.json Creation and Purpose

**Related documentation:**

- [README](README.md)
- [Solution Overview](solution.md)
- [Solution Flowcharts](solution_flowchart.md)
- [Main Process Flow](Process_main.md)
- [Datasource Maintenance Process](process_datasource_maintenance.md)

## Purpose

`tags.json` is a key component for enhancing Retrieval-Augmented Generation (RAG) search with intent recognition. It maps user questions and intents to tags, which are then used to efficiently find relevant documents or content chunks in the database. When a user asks a question, the system can look up the corresponding tag(s) in `tags.json` and use them to retrieve related information.

## Automatic Creation

If `tags.json` is missing from the project directory, the application will automatically generate it at startup. The process works as follows:

1. **Detection:** On startup, the application checks if `tags.json` exists in the working directory.
2. **Generation:** If not found, the application extracts tags and related questions from the imported documents (using the logic in `LongtermMemoryHelper.LoadAndStorePdfFromPathAsync`).
3. **Intent Mapping:** The system uses a prompt-based plugin (e.g., `IntentsPlugin`) to generate a mapping of tags to user questions/intents.
4. **File Creation:** The resulting mapping is saved as `tags.json` in the project directory.

## Example Structure

```json
{
  "Financial Docs": {
    "Kontoauszüge": [
      "Wie kann ich auf meine Kontoauszüge zugreifen?",
      "Kontoauszug"
    ]
  },
  ...
}
```

## Usage

- When a user asks a question, the system checks `tags.json` for matching or related intents.
- The corresponding tag(s) are used to search the document database for relevant content.
- This enables more accurate and intent-driven retrieval in RAG workflows.

## Regeneration

To force regeneration, simply delete `tags.json` and restart the application.

---

_This file documents the creation and role of `tags.json` in the RAG search workflow._
