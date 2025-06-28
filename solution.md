# Solution Overview: localRAG

**Related documentation:**

- [README](README.md)
- [Solution Flowcharts](solution_flowchart.md)
- [Main Process Flow](Process_main.md)
- [Datasource Maintenance Process](process_datasource_maintenance.md)
- [Tag Generation & Usage](CREATE_TAG.md)

## Introduction

`localRAG` is a C#/.NET-based software solution designed to provide Retrieval-Augmented Generation (RAG) capabilities using local documents. The system leverages advanced AI, semantic search, and memory management to answer user questions by retrieving and reasoning over local document content. It is architected for extensibility, maintainability, and integration with modern AI and cloud technologies.

---

## Technologies Used

### 1. **.NET (C#)**

- **Purpose:** Core application logic, orchestration, and integration.
- **Why:** Provides a robust, scalable, and high-performance environment for building enterprise-grade applications.

### 2. **Microsoft Semantic Kernel**

- **Purpose:** Orchestrates AI workflows, prompt engineering, and plugin management.
- **Features Used:**
  - Semantic function orchestration
  - Plugin import and management (e.g., memory, datetime)
  - Prompt execution and chaining
- **Why:** Enables flexible, modular AI workflows and seamless integration with LLMs and memory connectors.

### 3. **Azure OpenAI (GPT Models)**

- **Purpose:** Natural language understanding and generation for answering user queries and generating tag questions/intents.
- **Features Used:**
  - Chat completion
  - Prompt execution with custom system prompts
- **Why:** Provides state-of-the-art language capabilities for concise, context-aware responses.

### 4. **Kernel Memory (Microsoft.KernelMemory)**

- **Purpose:** Manages long-term memory, document ingestion, and semantic search over local documents.
- **Features Used:**
  - Memory connectors (serverless, Azure integration)
  - Handlers for tag generation and management
- **Why:** Enables efficient retrieval and augmentation of knowledge from local sources.

### 5. **MongoDB**

- **Purpose:** Data persistence for document metadata, tags, and possibly chat history.
- **Why:** Flexible, scalable NoSQL database suitable for storing semi-structured data.

### 6. **Tesseract OCR**

- **Purpose:** Optical Character Recognition for extracting text from scanned documents or images.
- **Why:** Allows ingestion of a wider range of document formats, including PDFs with images.

### 7. **Azure Cognitive Search (Azure.Search.Documents)**

- **Purpose:** (Optional/Pluggable) Advanced search capabilities over indexed document content.
- **Why:** Enhances semantic search and retrieval performance, especially for large document sets.

### 8. **ClosedXML, DocumentFormat.OpenXml**

- **Purpose:** Reading and writing Excel and OpenXML documents.
- **Why:** Supports ingestion and processing of spreadsheet-based knowledge sources.

### 9. **Docker Compose**

- **Purpose:** Container orchestration for local development and deployment.
- **Why:** Simplifies environment setup and dependency management.

### 10. **Other Libraries**

- **HtmlAgilityPack:** HTML parsing and manipulation.
- **AWSSDK:** (Present, but not actively used in main flow) AWS integration capabilities.
- **Google.Protobuf:** Protocol Buffers serialization.
- **Elastic.Clients.Elasticsearch:** (Present, for potential future use) Elasticsearch integration.

---

## Key Features & Workflow

1. **Document Ingestion & Tagging**

   - Imports documents from a specified path (`IMPORT_PATH`).
   - Uses OCR (Tesseract) and file parsers to extract text.
   - Generates and updates `tags.json` with tags and related questions/intents using AI.

2. **Semantic Memory & Search**

   - Loads documents into Kernel Memory for semantic search and retrieval.
   - Supports tag-based and full-text search over ingested content.

3. **AI-Powered Q&A (RAG)**

   - Uses Semantic Kernel to orchestrate prompts and plugins.
   - Calls Azure OpenAI GPT models to answer user questions, referencing local document knowledge.
   - System prompt enforces concise, source-cited answers.

4. **Process Workflow Engine**
   - Defines a process for handling user input, rewriting queries, routing, and generating responses.
   - Supports extensible step-based workflows (e.g., chat input, RAG search, datasource maintenance).
5. **Plugin System**

   - Imports custom plugins (e.g., memory, datetime, prompt-based plugins for hallucination checks, intent detection).
   - Enables modular extension of system capabilities.

6. **Logging & Debugging**

   - Uses Microsoft.Extensions.Logging for structured logging.
   - Debug flags for kernel, memory, and GPT-3.5 flows.

7. **Extensibility**
   - Modular architecture allows for easy addition of new document types, plugins, and process steps.
   - Pluggable memory connectors and search backends (Azure, Elastic, etc.).

---

## Why These Technologies?

- **Semantic Kernel & Azure OpenAI:** Provide a powerful, flexible foundation for orchestrating AI-driven workflows and leveraging state-of-the-art language models.
- **Kernel Memory:** Ensures that local document knowledge is efficiently indexed, retrieved, and augmented for user queries.
- **MongoDB & NoSQL:** Allow for flexible, schema-less storage of tags, metadata, and chat history.
- **Tesseract & Document Parsers:** Enable ingestion of a wide variety of document formats, maximizing the system's utility.
- **Plugin System:** Supports rapid prototyping and extension of AI capabilities without modifying core logic.
- **Docker Compose:** Ensures consistent, reproducible development and deployment environments.

---

## Summary

`localRAG` is a modern, extensible RAG solution that combines the power of Microsoft Semantic Kernel, Azure OpenAI, and advanced memory/search technologies to deliver concise, source-cited answers from local documents. Its modular design, rich plugin system, and support for diverse document types make it suitable for enterprise knowledge management, document Q&A, and AI-powered search applications.
