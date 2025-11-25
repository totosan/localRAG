# Reranking Quick Reference Card

## 🎯 What You Added

**Location:** `Utilities/Reranker.cs`

**Two methods:**
1. `RerankAsync()` - Semantic similarity reranking using embeddings
2. `ReRankByKeywordOverlap()` - Simple keyword-based reranking

## 🔌 Integration Point

**File:** `Utilities/LongtermMemoryHelper.cs`  
**Method:** `GetLongTermMemory()`

Added after both retrieval paths:
```csharp
// After SearchAsync or AskAsync collects documents...
if (documents.Count > 1)
{
    bool useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA")?.ToLower() == "true";
    var embeddingGenerator = Helpers.GetEmbeddingGenerator(useAzure: !useOllama);
    documents = await Reranker.RerankAsync(query, documents, embeddingGenerator, topK: -1);
}
```

## 🏃 How to Run

```bash
# Build
dotnet build

# Run with reranking (automatic)
dotnet run -- --ollama

# First time? Import documents first
dotnet run -- --ollama --import
```

## 📊 What You'll See

Console output shows reranking in action:
```
[Reranker] Starting rerank for 3 documents
[Reranker] Doc: insurance-policy.pdf (part 2) | Original: 0.450 | Rerank: 0.892 | Blended: 0.759
[Reranker] Doc: terms-conditions.pdf (part 1) | Original: 0.620 | Rerank: 0.715 | Blended: 0.687
[Reranker] Reranking complete. Returning top 3 results
[Reranker] ✓ Reranking changed document order for better relevance!
```

## 🎤 Key Demo Points

1. **Show the scores changing** - Original vs. Rerank vs. Blended
2. **Show document reordering** - Position changes for better relevance  
3. **Explain blending** - 70% rerank + 30% original = best of both
4. **Compare with/without** - Comment out reranking to show difference

## 🎓 Teaching Points

- **Cosine similarity** = measures semantic closeness
- **Blending** = combines retrieval and reranking signals
- **Transparent** = all scores visible for learning
- **Production-ready concept** = same approach used in real RAG systems

## 📝 Files Modified

1. ✅ `Utilities/Reranker.cs` - NEW (reranking logic)
2. ✅ `Utilities/Helpers.cs` - Added `GetEmbeddingGenerator()`
3. ✅ `Utilities/LongtermMemoryHelper.cs` - Integrated reranking
4. ✅ `RERANKING_DEMO.md` - Full demo guide
5. ✅ `.github/copilot-instructions.md` - Updated docs

## 💡 Tips

- **Tune blending:** Change `0.7f * similarity + 0.3f * doc.Score` to adjust weights
- **Limit results:** Set `topK: 5` to return only top 5 after reranking
- **Try keyword fallback:** Swap to `ReRankByKeywordOverlap()` for comparison
- **Benchmark:** Time with/without reranking to show the ~100-200ms cost

---
Ready to present! 🚀
