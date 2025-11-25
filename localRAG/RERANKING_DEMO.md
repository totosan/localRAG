# Reranking in RAG - Demo Guide

## What is Reranking?

Reranking is a crucial optimization step in RAG (Retrieval-Augmented Generation) that **improves the relevance ordering** of retrieved documents. While initial retrieval uses fast vector similarity search, reranking applies more sophisticated scoring to ensure the most relevant chunks appear first.

## Why Reranking Matters

**The Problem:** Vector similarity alone isn't perfect. Sometimes:
- Documents with high embedding similarity don't actually answer the question well
- The best answer is buried in position 3-5 instead of position 1
- Semantic nuances get missed by pure embedding distance

**The Solution:** Reranking re-scores documents using:
- Cross-encoder models (heavy but accurate)
- **Or our approach:** Embedding-based semantic similarity with blended scoring

## Our Implementation (Simple but Effective)

Located in: `Utilities/Reranker.cs`

### Two Reranking Strategies

#### 1. **Semantic Reranking** (Primary - Uses Embeddings)
```csharp
await Reranker.RerankAsync(query, documents, embeddingGenerator, topK: -1)
```

**How it works:**
1. Generate embedding for user query
2. Generate embeddings for each retrieved document chunk
3. Compute cosine similarity between query and each chunk
4. Blend reranking score with original retrieval score (70% rerank / 30% original)
5. Re-sort documents by blended score

**Demo talking points:**
- Shows actual similarity computation in real-time
- Logs show: `Original: 0.623 | Rerank: 0.847 | Blended: 0.780`
- Visible improvement when document order changes
- Works with both Azure OpenAI and Ollama embeddings

#### 2. **Keyword-based Reranking** (Simpler fallback)
```csharp
Reranker.ReRankByKeywordOverlap(query, documents, topK: -1)
```

**How it works:**
1. Extract keywords from query using existing `KeywordExtractor`
2. Count keyword matches in each document
3. Blend keyword score with original retrieval score (60% keyword / 40% original)
4. Re-sort by blended score

**Demo talking points:**
- Faster than semantic (no embedding generation needed)
- Good for demonstrating the reranking concept
- Shows how hybrid approaches combine signals

## Integration Points

Reranking is automatically applied in `LongtermMemoryHelper.GetLongTermMemory()`:

```csharp
// After retrieval via SearchAsync
if (documents.Count > 1)
{
    bool useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA")?.ToLower() == "true";
    var embeddingGenerator = Helpers.GetEmbeddingGenerator(useAzure: !useOllama);
    documents = await Reranker.RerankAsync(query, documents, embeddingGenerator, topK: -1);
}
```

**Applied to BOTH retrieval paths:**
- ✅ SearchAsync (raw chunks with filters)
- ✅ AskAsync (KM-generated answers)

## What Gets Logged

When reranking runs, you'll see console output like:
```
[Reranker] Starting rerank for 3 documents
[Reranker] Doc: insurance-policy.pdf (part 2) | Original: 0.450 | Rerank: 0.892 | Blended: 0.759
[Reranker] Doc: insurance-policy.pdf (part 5) | Original: 0.620 | Rerank: 0.715 | Blended: 0.687
[Reranker] Doc: terms-conditions.pdf (part 1) | Original: 0.580 | Rerank: 0.623 | Blended: 0.610
[Reranker] Reranking complete. Returning top 3 results
[Reranker] ✓ Reranking changed document order for better relevance!
```

This makes it **perfect for live demos** - the audience can see exactly how scores change!

## Demo Script for Your Talk

### Setup (Before Demo)
1. Run with existing data: `dotnet run -- --ollama`
2. The reranking will automatically activate on any retrieval

### Live Demo Flow

**Step 1: Show the Problem**
```
"Let me ask a question about [topic in your documents]"
→ Point out: "Notice the initial retrieval scores from vector search"
```

**Step 2: Show Reranking in Action**
```
[Watch console output as question is processed]
→ "See how reranking recalculates relevance?"
→ "Document #2 moved to #1 because the semantic similarity is actually higher"
→ "The blended score combines retrieval confidence with reranking precision"
```

**Step 3: Explain the Impact**
```
"Before reranking: We might send less relevant chunks to the LLM"
"After reranking: The best chunks go first, improving answer quality"
```

### Key Talking Points

1. **Cost vs Quality Trade-off**
   - Reranking adds ~100-200ms per query (embedding generation)
   - But dramatically improves answer relevance
   - Blended scoring preserves retrieval signal while boosting precision

2. **Why Blending Matters**
   - Pure reranking can over-correct
   - Original retrieval score captures index-time signals (BM25, vector distance)
   - Blending (70/30 or 60/40) gives best of both worlds

3. **Production Considerations**
   - For scale: Use dedicated reranking models (Cohere, cross-encoders)
   - For learning: This embedding approach is transparent and debuggable
   - For talks: The logging makes it visually clear what's happening

## Code Highlights to Show

### The Cosine Similarity Computation
```csharp
private static float CosineSimilarity(ReadOnlyMemory<float> vectorA, ReadOnlyMemory<float> vectorB)
{
    // Show the math: dot product / (magnitude_a * magnitude_b)
    // This is the core of semantic similarity!
}
```

### The Blending Strategy
```csharp
// 70% reranking, 30% original retrieval - tunable!
float blendedScore = (0.7f * similarity) + (0.3f * doc.Score);
```

## Testing Reranking

Run with: `dotnet run -- --ollama`

Try queries like:
- "What are the payment terms?" (should surface financial chunks first)
- "How do I cancel?" (should prioritize termination/cancellation chunks)
- "What coverage do I have?" (should rank coverage sections highest)

Compare answers with/without reranking by temporarily commenting out the reranking calls to show the difference!

## Advanced: Toggle Reranking On/Off

To demonstrate the value, you can add an environment variable:
```csharp
bool enableReranking = Helpers.EnvVarOrDefault("ENABLE_RERANKING", "true") == "true";

if (enableReranking && documents.Count > 1)
{
    // ... reranking code ...
}
```

Then show side-by-side:
- `ENABLE_RERANKING=false dotnet run -- --ollama` → baseline
- `ENABLE_RERANKING=true dotnet run -- --ollama` → with reranking

## Architecture Diagram (Draw this on slides)

```
User Query
    ↓
[1] Initial Retrieval (Vector Search)
    → Returns top-K documents with scores
    ↓
[2] Reranking (Semantic Similarity)
    → Re-scores each doc against query
    → Blends scores
    → Re-sorts results
    ↓
[3] LLM Context (Top chunks sent to GPT)
    → Better context = Better answers
    ↓
Final Answer
```

## Summary for Audience

**"Reranking is like having a second opinion:"**
- First opinion (retrieval): "These documents *might* be relevant"
- Second opinion (reranking): "Actually, *this one* is the best match"
- Combined wisdom (blending): "Let's use both signals for confidence"

**Why this implementation rocks for demos:**
- ✅ Real-time visible scoring
- ✅ Simple to understand (cosine similarity)
- ✅ Works with existing infrastructure (KM embeddings)
- ✅ Production-ready concept (used by major RAG systems)
- ✅ Easy to extend (swap in cross-encoders later)

---

Good luck with your talk! 🎤 The visual logging and simple math make this a great teaching example.
