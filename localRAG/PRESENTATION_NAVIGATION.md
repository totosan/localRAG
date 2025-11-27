# 🎤 RAG Presentation Navigation Guide

## Quick Reference Map

| Slide | Topic | Primary File | Lines | Breakpoint | Region |
|-------|-------|-------------|--------|------------|--------|
| 4 | Normalisierung | [CustomPdfDecoder.cs](Decoder/CustomPdfDecoder.cs#L254-L294) | 254-294 | 279 | ✅ |
| 5 | Chunking | [LongtermMemoryHelper.cs](Utilities/LongtermMemoryHelper.cs#L476-L530) | 476-530 | 490 | ✅ |
| 6 | Keyword-Extraktion | [KeywordExtractor.cs](Utilities/KeywordExtractor.cs#L114-L137) | 114-137 | 137 | ✅ |
| 7 | Dense Retriever | [LongtermMemoryHelper.cs](Utilities/LongtermMemoryHelper.cs#L365-L395) | 365-395 | 384 | ✅ |
| 8 | Hybrid Retrieval | [StepLookupKernelMemory.cs](Process/Steps/StepLookupKernelMemory.cs#L110-L125) | 110-125 | 115 | ✅ |
| 9 | Reranking | [Reranker.cs](Utilities/Reranker.cs#L44-L115) | 44-115 | 79 | ✅ |
| 10 | Halluzination | [ResponseStepWithHalluCheck.cs](Process/Steps/ResponseStepWithHalluCheck.cs#L72-L115) | 72-115 | 103 | ✅ |
| 11 | Live-Demo | [Program.cs](Program.cs) | Full flow | - | - |

---

## Slides 1-3: Einführung (No Code)

**Slide 1**: RAG-tastisch - Der neue Standard für intelligente Suche  
**Slide 2**: Warum klassische Suche oft versagt  
**Slide 3**: Die Lösung - Was ist RAG?

*Theory and motivation only - no code demos for these slides.*

---

## Slide 4: Dokumenten-Normalisierung

### 📁 File Location
**Path**: [Decoder/CustomPdfDecoder.cs](Decoder/CustomPdfDecoder.cs#L254-L294)  
**Lines**: 254-294 (NormalizeText method)  
**Region**: `#region Slide 4: Dokumenten-Normalisierung`

### 🎯 Demo Setup
**Breakpoint**: Line 279 (inside `NormalizeText`)  
**How to navigate**: 
- Press `Ctrl+G` → type `CustomPdfDecoder.cs:279`
- OR: Press `Ctrl+Shift+O` → search "NormalizeText"
- OR: Expand `Slide 4` region in outline view

### 👀 Watch Variables
```csharp
text          // Input: "Das ist ein Beispiel-Text! Mit Satzzeichen."
normalized    // After each step transformation
words         // Array after stopword filtering
```

### 🎬 Demo Flow
1. Set breakpoint at line 279
2. Run: `dotnet run -- --ollama --import`
3. When breakpoint hits, show `text` variable (original)
4. Press F10 to step through each normalization step:
   - Line 279: Lowercase → `"das ist ein beispiel-text! mit satzzeichen."`
   - Line 282: Remove punctuation → `"das ist ein beispiel text mit satzzeichen"`
   - Line 285-288: Remove stopwords → `["beispiel", "text", "satzzeichen"]`
   - Line 291: Join and trim → `"beispiel text satzzeichen"`

### 💬 Talking Points
> "Before indexing, we run a complete normalization pipeline: **lowercase → remove punctuation → filter stopwords → clean whitespace**. This is the textbook RAG preprocessing approach."

> "Pure C# implementation - **no external NLP libraries**! 40+ stopwords in German and English. This normalization happens **once during import**, ensuring consistent retrieval."

### ✨ Key Highlight
- Show stopwords dictionary (lines 19-28): Bilingual support
- Emphasize: "This is **index-time** normalization - happens once, not per query"
- Example: Queries like "Azure Service" or "azure-service" now match the same chunks!

---

## Slide 5: Chunking-Strategien

### 📁 File Location
**Path**: [Utilities/LongtermMemoryHelper.cs](Utilities/LongtermMemoryHelper.cs#L476-L530)  
**Lines**: 476-530 (GetAdjacentChunksInMemoriesAsync)  
**Region**: `#region Slide 5: Chunking-Strategien - Adjacent Chunks (Overlap)`

### 🎯 Demo Setup
**Breakpoint**: Line 490 (inside adjacent chunks loop)  
**How to navigate**:
- Press `Ctrl+G` → type `LongtermMemoryHelper.cs:490`
- OR: Search for "GetAdjacentChunksInMemoriesAsync"

### 👀 Watch Variables
```csharp
memories.Results          // Initial matched chunks
previousPartitionNumber   // Chunk N-1
nextPartitionNumber       // Chunk N+1
allsearchResults          // Final list with adjacent chunks
```

### 🎬 Demo Flow

**⚠️ IMPORTANT**: This breakpoint only hits during **QUERY mode**, not during import!

#### Preparation (if documents not imported yet):
```bash
dotnet run -- --ollama --import
```

#### Demo Steps:
1. Use launch config: **"RAG Demo - Slide 5 (Chunking - Query Mode)"** (runs `--ollama` without `--import`)
2. Set breakpoint at line 490
3. Ask a question: "What is RAG?" or "Explain Azure microservices"
4. Breakpoint hits → show how code fetches partition N-1 and N+1
5. Explain: "This is our **overlap strategy** - we fetch chunks before/after to preserve context"

### 💬 Talking Points
> "⚠️ **Key point**: Chunks are **created during import** by Kernel Memory pipeline (internally), but we **observe them during retrieval** when reconstructing context."

> "We don't just return the matching chunk. We fetch **adjacent chunks** (before/after) to preserve context boundaries."

> "Think of it like reading a highlighted passage in a book - you need the sentences before and after to understand the full context. That's what this overlap strategy does."

### 🔍 Chunking Strategies Comparison
Show this visual during your talk (see [demo-rag-document.txt](demo-rag-document.txt) for example text):

```
Fixed-Size (Simple):
[Chunk 1: 512 tokens] | [Chunk 2: 512 tokens] | [Chunk 3: 512 tokens]
❌ Problem: May cut sentences mid-way

Semantic (Page-based):
[Chunk 1: Page 1] | [Chunk 2: Page 2] | [Chunk 3: Page 3]
✅ Better: Natural boundaries

Overlap (Our approach):
Query matches → Chunk 5
We return: [Chunk 4] [Chunk 5] [Chunk 6]
✅ Best: Context preservation!
```

**Demo Document**: Use [demo-rag-document.txt](demo-rag-document.txt) (3000+ words about RAG) to show live chunking examples during your presentation.

---

## Slide 6: Keyword-Extraktion

### 📁 File Location
**Path**: [Utilities/KeywordExtractor.cs](Utilities/KeywordExtractor.cs#L114-L137)  
**Lines**: 114-137 (ExtractKeywords method)  
**Region**: `#region Slide 6: Keyword-Extraktion`

### 🎯 Demo Setup
**Breakpoint**: Line 137 (after `return keywords.Take(maxKeywords).ToList();`)  
**How to navigate**:
- Press `Ctrl+G` → type `KeywordExtractor.cs:137`
- OR: Collapse all regions (`Ctrl+K, Ctrl+0`), expand Slide 6 region

### 👀 Watch Variables
```csharp
extractedKeywords  // Combined results from all 4 methods
singleWords        // TF-IDF results
technicalTerms     // Detected technical vocabulary
phrases            // RAKE phrase extraction
namedEntities      // Capitalized terms
```

### 🎬 Demo Flow - Show All 4 Methods

**Launch Config**: Use **"RAG Demo - Slide 6 (Keywords - Import Mode)"** to see keyword extraction during document import at line 173 in `GenerateTagsHandler.cs`

#### **Method 1: TF-IDF Frequency Analysis**
- **Region**: `#region 6.1: Method 1 - TF-IDF Frequency Analysis`
- **Lines**: 75-99
- **Demo**: Step into `ExtractFrequentWords`, show word frequency counting
- **Explain**: "Finds most common non-stopword terms"

#### **Method 2: Technical Term Detection**
- **Region**: `#region 6.2: Method 2 - Technical Term Detection`
- **Lines**: 103-111
- **Demo**: Show `TechnicalIndicators` dictionary (lines 32-38)
- **Explain**: "Predefined vocabulary matching - finds 'api', 'docker', 'kubernetes', etc."

#### **Method 3: RAKE Phrase Extraction**
- **Region**: `#region 6.3: Method 3 - RAKE Phrase Extraction`
- **Lines**: 118-162
- **Demo**: Show phrase detection between stopwords
- **Explain**: "Extracts multi-word key phrases like 'service mesh' or 'container orchestration'"

#### **Method 4: Named Entity Recognition**
- **Region**: `#region 6.4: Method 4 - Named Entity Recognition`
- **Lines**: 166-190
- **Demo**: Show capitalized sequence detection
- **Explain**: "Pattern-based NER - finds 'Azure', 'Kubernetes', proper nouns"

### 💬 Talking Points
> "**Zero dependencies** on external NLP libraries! Pure C# regex + frequency analysis. Fast, deterministic, and debuggable - perfect for learning RAG fundamentals."

> "We combine 4 complementary methods: frequency (TF-IDF), domain vocabulary, phrases (RAKE), and named entities. This **hybrid keyword extraction** catches different types of important terms."

### 🎭 Live Demo Script
1. Show input text: *"Azure Kubernetes Service provides container orchestration with high availability."*
2. Step through extraction:
   - TF-IDF: `["kubernetes", "service", "container", "orchestration"]`
   - Technical: `["azure", "kubernetes", "api"]`
   - Phrases: `["kubernetes service", "container orchestration", "high availability"]`
   - Entities: `["Azure", "Kubernetes", "Service"]`
3. Final result: Combined, deduplicated list

---

## Slide 7: Dense Retriever

### 📁 File Location
**Path**: [Utilities/LongtermMemoryHelper.cs](Utilities/LongtermMemoryHelper.cs#L365-L395)  
**Lines**: 365-395 (GetLongTermMemory method)  
**Region**: `#region Slide 7: Dense Retriever + Slide 8: Hybrid Retrieval`

### 🎯 Demo Setup
**Breakpoint**: Line 384 (at `SearchAsync` call)  
**How to navigate**:
- Press `Ctrl+G` → type `LongtermMemoryHelper.cs:384`

### 👀 Watch Variables
```csharp
query               // User's search query
minRelevance        // Threshold (0.4 = 40% similarity)
filters             // Applied filters (may be empty for pure semantic)
memories            // Search results with relevance scores
```

### 🎬 Demo Flow
1. Set breakpoint at line 384
2. Run query: "Azure microservices architecture"
3. Show `SearchAsync` parameters:
   - `query`: The user's question
   - `minRelevance: 0.4`: Only return chunks with >40% similarity
   - `limit: 3`: Top 3 results
4. After call completes, show `memories.Results` - sorted by relevance score

### 💬 Talking Points
> "The **dense retriever** uses embeddings - numeric vectors that capture semantic meaning. Vector DB computes similarity between query embedding and all document embeddings."

> "Think of it like this: Each word/phrase becomes a point in high-dimensional space. Similar concepts cluster together. The search finds nearest neighbors."

### 📊 Show This Concept
```
Query: "container orchestration"
Embedding: [0.23, -0.15, 0.89, ..., 0.42]  (1536 dimensions)

Document chunks also have embeddings:
Chunk A: [0.21, -0.18, 0.91, ..., 0.45]  → Similarity: 0.92 ✅
Chunk B: [0.82, 0.35, -0.12, ..., 0.18]  → Similarity: 0.34 ❌
Chunk C: [0.19, -0.14, 0.88, ..., 0.41]  → Similarity: 0.89 ✅

Returns: Chunk A, Chunk C (most similar)
```

---

## Slide 8: Hybrid Retrieval

### 📁 Primary File
**Path**: [Process/Steps/StepLookupKernelMemory.cs](Process/Steps/StepLookupKernelMemory.cs#L110-L125)  
**Lines**: 110-125 (keyword filter extraction)

### 📁 Secondary File
**Path**: [Utilities/LongtermMemoryHelper.cs](Utilities/LongtermMemoryHelper.cs#L365-L390)  
**Lines**: 365-390 (filter application)

### 🎯 Demo Setup
**Breakpoint**: Line 115 in `StepLookupKernelMemory.cs`  
**How to navigate**:
- Press `Ctrl+G` → type `StepLookupKernelMemory.cs:115`

### 👀 Watch Variables
```csharp
standaloneQuestion  // User's query
keywordFilters      // Extracted keywords from query
filters             // MemoryFilter objects for hybrid search
memories            // Results after hybrid filtering
```

### 🎬 Demo Flow

**Launch Config**: Use **"RAG Demo - Slides 7-10"** (query mode without `--import`)

#### Step 1: Keyword Extraction from Query
- **File**: `StepLookupKernelMemory.cs` lines 110-125
- Show how keywords are extracted from user query:
  ```csharp
  var keywordFilters = KeywordExtractor
      .ExtractKeywords(standaloneQuestion, maxKeywords: 6)
      .Concat(KeywordExtractor.ExtractNamedEntities(standaloneQuestion, maxEntities: 4))
      .Distinct()
      .ToList();
  ```

#### Step 2: Filter Construction
- **File**: `LongtermMemoryHelper.cs` lines 340-350
- Show how keywords become filters:
  ```csharp
  if (keywordFilters?.Count > 0)
  {
      filters.Add(MemoryFilters.ByTag("keywords", keywordFilters));
  }
  ```

#### Step 3: Hybrid Search Execution
- Show `SearchAsync` call with filters:
  ```csharp
  memories = await memory.SearchAsync(
      query,             // Semantic search via embeddings
      minRelevance: 0.4,
      limit: 3,
      filters: filters   // Keyword filtering applied
  );
  ```

### 💬 Talking Points
> "**Hybrid retrieval** combines the best of both worlds: semantic understanding (vector embeddings) + exact matching (keyword filters)."

> "This is **metadata-enhanced retrieval** - functionally equivalent to BM25+vector fusion but optimized for debugging and education."

> "For production scale, you'd swap to `.WithAzureAISearchMemoryDb()` which provides native BM25 inverted indexes. But this approach is perfect for understanding the concept!"

### 📊 Hybrid Search Formula
```
Hybrid Search = Semantic Search (embeddings) + Keyword Filters (metadata)

Example Query: "Azure microservices patterns"

Step 1 - Semantic Search (embeddings):
→ Finds conceptually related content
→ Returns: 100 potentially relevant chunks

Step 2 - Keyword Filtering (metadata):
→ Filter by keywords: ["azure", "microservices", "patterns"]
→ Only keep chunks tagged with these keywords
→ Returns: 10 highly relevant chunks

Result: High recall + High precision! 🎯
```

---

## Slide 9: Reranking

### 📁 Primary File
**Path**: [Utilities/Reranker.cs](Utilities/Reranker.cs#L44-L115)  
**Lines**: 44-115 (RerankAsync method)  
**Region**: `#region 9.1: Semantic Reranking (Embedding-based)`

### 📁 Application Points
**Path**: [Utilities/LongtermMemoryHelper.cs](Utilities/LongtermMemoryHelper.cs#L413-L422)  
- Lines 413-422: Application point #1 (SearchAsync path)
- Lines 446-455: Application point #2 (AskAsync path)

### 🎯 Demo Setup
**Breakpoint**: Line 79 in `Reranker.cs` (inside reranking loop)  
**How to navigate**:
- Press `Ctrl+G` → type `Reranker.cs:79`
- OR: Expand `#region Slide 9: Reranking Implementation`

### 👀 Watch Variables
```csharp
query               // User's search query
queryEmbedding      // Query as embedding vector
doc                 // Current document being scored
docEmbedding        // Document as embedding vector
similarity          // Cosine similarity (0-1)
doc.Score           // Original retrieval score
blendedScore        // Final score (70% rerank + 30% original)
rerankedDocs        // List before sorting
sorted              // List after reranking
```

### 🎬 Demo Flow

#### Setup
1. Launch config: **"RAG Demo - Slides 7-10"**
2. Set breakpoint at `Reranker.cs` line 79
3. Ask query: "Azure microservices architecture"

#### Step-by-Step Demo
1. **Show Initial Order** (before reranking)
   - Inspect `documents` variable
   - Note original relevance scores from vector search
   - Example: `[Doc A: 0.78, Doc B: 0.82, Doc C: 0.75]`

2. **Step Through Reranking Loop** (F10 repeatedly)
   - Line 73: Generate document embedding
   - Line 78: Compute cosine similarity
   - Watch `similarity` variable - this is the reranking score
   - Line 81: Blend scores (70% new, 30% original)
   - Show calculation: `blendedScore = (0.7 * similarity) + (0.3 * doc.Score)`

3. **Show Reordered Results**
   - After loop, inspect `sorted` variable
   - New order based on blended scores
   - Example: `[Doc C: 0.89, Doc A: 0.85, Doc B: 0.72]`
   - **Doc C moved to #1!**

4. **Console Output Shows Effect**
   - Look for: `"[Reranker] ✓ Reranking changed document order for better relevance!"`
   - This confirms reranking had impact

### 💬 Talking Points

#### Opening
> "Vector DB gives us initial results - fast **recall**. Reranker improves **precision**. This is the **two-stage retrieval** pattern used in production RAG systems."

#### During Demo
> "See how we compute similarity between query and each document? We're using the **same embedding model** - this gives us true semantic similarity, not just keyword overlap."

> "We blend the scores - **70% reranking + 30% original**. Why? The original score has value - it tells us the document was in the right neighborhood. We combine both signals for best results."

#### Result
> "Look at the console - **Doc #3 moved to #1** after reranking! It had higher semantic similarity to our query, even though vector search initially ranked it lower. This is production-grade RAG!"

### 🎭 Alternative Demo: Keyword-based Reranking

If embedding-based reranking is too slow for live demo, show the simpler approach:

**File**: `Reranker.cs` lines 117-175 (`ReRankByKeywordOverlap`)  
**Concept**: Count keyword matches instead of computing embeddings

```csharp
Query: "Azure microservices patterns"
Keywords: ["azure", "microservices", "patterns"]

Doc A: Contains 2/3 keywords → keywordScore = 0.67
Doc B: Contains 3/3 keywords → keywordScore = 1.0
Doc C: Contains 1/3 keywords → keywordScore = 0.33

→ Doc B wins!
```

### 📊 Reranking Visualization
```
TWO-STAGE RETRIEVAL PIPELINE
============================

Stage 1: Vector Search (Dense Retriever)
📊 Goal: High Recall
   Input: Query embedding
   Output: Top 100 similar chunks
   Speed: ~200ms

Stage 2: Reranking (Semantic Similarity)
🎯 Goal: High Precision
   Input: Query + Top 100 chunks
   Process: Compute similarity for each
   Output: Top 10 most relevant chunks
   Speed: ~500ms

Total: ~700ms for high-quality results! ✅
```

### ✨ Key Insights
1. **Why rerank?** Vector search optimizes for speed (approximate nearest neighbors). Reranking optimizes for accuracy.
2. **Blended scoring**: Combines retrieval signal + reranking signal = best of both worlds
3. **Production ready**: This exact pattern is used by OpenAI, Anthropic, Google in their RAG systems
4. **Observable**: Console logs make debugging easy - perfect for learning!

---

## Slide 10: Halluzinations-Prävention

### 📁 File Location
**Path**: [Process/Steps/ResponseStepWithHalluCheck.cs](Process/Steps/ResponseStepWithHalluCheck.cs#L72-L115)  
**Lines**: 72-115 (hallucination check logic)  
**Region**: `#region Slide 10: Hallucination Check Logic`

### 🎯 Demo Setup
**Breakpoint**: Line 103 (after LLM check completes)  
**How to navigate**:
- Press `Ctrl+G` → type `ResponseStepWithHalluCheck.cs:103`

### 👀 Watch Variables
```csharp
contextContent      // Retrieved context from documents
response.Content    // Generated answer from LLM
checkResult         // LLM's verdict: "Score: YES" or "Score: NO"
isGrounded          // Boolean: Is answer grounded in context?
```

### 🎬 Demo Flow

#### Setup
1. Launch config: **"RAG Demo - Slides 7-10"**
2. Set breakpoint at line 103
3. Ask a question that triggers RAG retrieval

#### Step-by-Step Demo

**Step 1: Show Context Extraction**
- Line 79: Extract context from last user message
- Show `contextContent` - this is what was retrieved from documents
- Example: *"Azure Kubernetes Service provides container orchestration..."*

**Step 2: LLM Fact-Checker Call**
- Lines 88-98: Separate GPT-3.5 model invoked
- Show prompt in `Plugins/Prompts/HalucinationCheckPlugin/`
- Prompt asks: "Is the answer supported by the context?"

**Step 3: Check Result**
- Line 103: Inspect `checkResult` variable
- Look for: `"Score: YES"` (grounded) or `"Score: NO"` (hallucination)
- Show `isGrounded` boolean derived from check

**Step 4: Warning Application**
- Lines 112-115: If NOT grounded, prepend warning
- Show modified response: `"[Warning: This answer is not based on the retrieved documents.]\n" + response.Content`

### 💬 Talking Points

#### Opening
> "The biggest risk in RAG? The LLM might **fabricate facts** that sound plausible but aren't in your documents. We call this **hallucination**."

#### During Demo
> "Our solution: Use a **second LLM as a fact-checker**. It compares the generated answer against the retrieved context and scores YES (grounded) or NO (hallucination)."

> "If the check fails, we add a clear warning to the user. **Transparency over false confidence** - this is production best practice."

#### Fallback Mechanism
> "We also have a fallback - simple **keyword overlap** check. If the LLM fact-checker fails (API error, timeout), we still have a safety net."

Show code at line 92:
```csharp
// Fallback to simple keyword overlap
isGrounded = HallucinationCheckPlugin.IsGrounded(
    response.Content, 
    new List<string> { contextContent }, 
    minOverlap: 3
);
```

### 🎭 Example Scenarios

#### Scenario 1: Good Answer (Grounded)
```
Context: "Azure Functions supports C#, JavaScript, Python, and Java."
Question: "What languages does Azure Functions support?"
Answer: "Azure Functions supports C#, JavaScript, Python, and Java."
Check Result: "Score: YES" ✅
```

#### Scenario 2: Hallucination Detected
```
Context: "Azure Functions supports C#, JavaScript, Python, and Java."
Question: "What languages does Azure Functions support?"
Answer: "Azure Functions supports C#, JavaScript, Python, Java, Ruby, and Go."
Check Result: "Score: NO" ❌
Warning Added: "[Warning: This answer is not based on the retrieved documents.] 
               Azure Functions supports C#, JavaScript, Python, Java, Ruby, and Go."
```

### 📊 Hallucination Prevention Pipeline
```
ANSWER GENERATION WITH FACT-CHECKING
====================================

Step 1: Generate Answer
   LLM: GPT-4 / Ollama
   Input: User question + Retrieved context
   Output: Generated answer
   
Step 2: Fact-Check (LLM-based)
   LLM: GPT-3.5 (separate model)
   Input: Context + Generated answer
   Process: "Is answer supported by context?"
   Output: YES / NO score
   
Step 3: Fallback Check (Keyword-based)
   Process: Count overlapping words
   Threshold: Minimum 3 matches
   Output: true / false
   
Step 4: Warning (if needed)
   If NOT grounded:
   → Prepend "[Warning: ...]"
   → User gets transparency! ✅
```

### ✨ Key Insights
1. **Two-model approach**: Generator (GPT-4) + Fact-checker (GPT-3.5) = Robust system
2. **Graceful degradation**: LLM check fails → keyword fallback kicks in
3. **Transparency**: We warn users rather than silently serving hallucinations
4. **Production pattern**: Same technique used by major RAG systems (ChatGPT, Claude, etc.)

---

## Slide 11: Live-Demo in C# / .NET

### 🎯 Full Pipeline Demonstration

**Goal**: Show the complete RAG flow from start to finish

### 🎬 Demo Sequence

#### 1. Document Import & Normalization
```bash
# Terminal
dotnet run -- --ollama --import
```

**Watch Console Output**:
- Document processing progress bars
- Normalization: Original → cleaned text
- Keyword extraction: TF-IDF + RAKE + NER
- Embedding generation
- Vector storage in `tmp-data/`

#### 2. Show Generated Artifacts
```bash
# Tags file (document categories + keywords)
cat tags.json | jq .

# Vector database structure
ls -lh tmp-data/default/

# Trace logs (detailed pipeline execution)
tail -f trace/import-*.log
```

#### 3. Interactive Query Session
```bash
# Start interactive mode
dotnet run -- --ollama
```

**Ask Sample Questions**:
1. "What are Azure microservices best practices?"
2. "How do I deploy containers to Kubernetes?"
3. "Explain service mesh architecture"

**Watch Process Flow**:
- ChatUserInputStep: Receives question
- StepRewriteAsk: Rewrites for better retrieval
- StepLookupKernelMemory: Retrieves context (hybrid search)
- Reranking: Improves relevance
- ResponseStepWithHalluCheck: Generates answer + fact-checks
- Output: Final answer with sources

#### 4. Show Process Graph (Mermaid Diagram)
```bash
# Generate visual representation
# (If you have ProcessVisualization.cs)
```

### 💬 Talking Points

> "This is the **entire RAG pipeline** running locally. Everything we discussed - normalization, chunking, keyword extraction, hybrid search, reranking, hallucination prevention - it's all here, working together."

> "Notice the **console logs** - every step is visible. Perfect for debugging and understanding what's happening under the hood."

> "**Performance**: From question to answer in ~2-3 seconds with Ollama locally. With Azure OpenAI, it's even faster."

### 📊 Pipeline Visualization
```
USER QUESTION
    ↓
┌───────────────────────────┐
│ 1. Rewrite Question       │  StepRewriteAsk
│    (Better retrieval)     │  Lines: 28-100
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ 2. Extract Keywords       │  StepLookupKernelMemory
│    (For hybrid search)    │  Lines: 110-125
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ 3. Vector Search          │  LongtermMemoryHelper
│    (Semantic similarity)  │  Line: 350
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ 4. Apply Keyword Filters  │  (Hybrid retrieval)
│    (Metadata filtering)   │  Lines: 340-350
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ 5. Rerank Results         │  Reranker.RerankAsync
│    (Improve precision)    │  Lines: 28-88
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ 6. Generate Answer        │  ResponseStepWithHalluCheck
│    (LLM with context)     │  Lines: 20-48
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ 7. Fact-Check             │  (Hallucination prevention)
│    (Grounding verification)│  Lines: 70-100
└───────────┬───────────────┘
            ↓
       ANSWER + SOURCES
```

---

## Slide 12: Herausforderungen & Lösungen

### Talking Points Only (No Code Demo)

**Challenge 1: Skalierung**
- Index Sharding: Separate "intent" and "default" indexes
- Caching: `tags.json` prevents re-indexing
- Batch Processing: Pipeline processes documents efficiently

**Challenge 2: Kosten**
- Local Models: Ollama support (free!)
- Batch Processing: One LLM call per document (tagging)
- Selective LLM Use: Keywords use regex (no API calls)

**Challenge 3: Datensicherheit**
- Local Storage: `tmp-data/` directory (no cloud upload)
- On-Premises: Ollama keeps data local
- Access Controls: Ready for Azure RBAC integration

---

## Slide 13: Key Takeaways & Fazit

### Summary Points (No Code)

✅ **RAG = More than Search** - It's an answer-generating pipeline  
✅ **Preprocessing is 50% of Quality** - Normalization + chunking + keywords  
✅ **Hybrid Retrieval = Gold Standard** - Semantic + keyword filtering  
✅ **Two-Stage Retrieval** - Fast recall (vector DB) + precise rerank  
✅ **Hallucination Prevention** - LLM fact-checking + transparency  
✅ **Start Small, Optimize Iteratively** - Build complexity gradually

---

## Slide 14: Q&A / Danke

Presentation complete! 🎉

---

## 🛠️ VS Code Navigation Tips

### Keyboard Shortcuts
- **Ctrl+G**: Go to line
- **Ctrl+Shift+O**: Go to symbol
- **Ctrl+K, Ctrl+0**: Collapse all regions
- **Ctrl+K, Ctrl+J**: Expand all regions
- **F12**: Go to definition
- **Alt+F12**: Peek definition
- **Shift+F12**: Find all references

### Region Navigation
All demo code is marked with regions:
```csharp
#region Slide 4: Dokumenten-Normalisierung
// Your code here
#endregion
```

Use VS Code's **Outline View** (Ctrl+Shift+E) to see all regions and jump directly.

### Breakpoint Management
Create a **breakpoint list** before your presentation:
1. CustomPdfDecoder.cs:279
2. KeywordExtractor.cs:137
3. LongtermMemoryHelper.cs:384
4. LongtermMemoryHelper.cs:490
5. StepLookupKernelMemory.cs:115
6. Reranker.cs:79
7. ResponseStepWithHalluCheck.cs:103

Save as launch configuration in `.vscode/launch.json` (see next section)

---

## 📁 Quick File Reference

All key files at a glance:

```
localRAG/
├── Decoder/
│   └── [CustomPdfDecoder.cs](Decoder/CustomPdfDecoder.cs)          → Slide 4 (Normalization)
├── Utilities/
│   ├── [KeywordExtractor.cs](Utilities/KeywordExtractor.cs)          → Slide 6 (Keywords)
│   ├── [LongtermMemoryHelper.cs](Utilities/LongtermMemoryHelper.cs)      → Slides 5, 7, 8 (Chunking, Retrieval)
│   └── [Reranker.cs](Utilities/Reranker.cs)                  → Slide 9 (Reranking)
├── Process/Steps/
│   ├── [StepLookupKernelMemory.cs](Process/Steps/StepLookupKernelMemory.cs)    → Slide 8 (Hybrid Search)
│   └── [ResponseStepWithHalluCheck.cs](Process/Steps/ResponseStepWithHalluCheck.cs) → Slide 10 (Hallucination)
└── [Program.cs](Program.cs)                       → Slide 11 (Full Pipeline)
```

---

## 🎯 Pre-Presentation Checklist

- [ ] Build project: `dotnet build` (ensure no errors)
- [ ] Test import: `dotnet run -- --ollama --import` (verify documents process)
- [ ] Test query: `dotnet run -- --ollama` (verify full pipeline)
- [ ] Open navigation file (this file!) on second monitor
- [ ] Collapse all regions in VS Code: `Ctrl+K, Ctrl+0`
- [ ] Set all 7 breakpoints (listed above)
- [ ] Have `tags.json` open in a tab (show generated tags)
- [ ] Have trace log open: `tail -f trace/*.log`
- [ ] Test one full demo flow before presentation

---

**Good luck with your presentation!** 🚀  
**Remember**: The code is already excellent - now you have perfect navigation to show it off!
