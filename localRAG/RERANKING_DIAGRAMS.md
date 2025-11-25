# Reranking Architecture Diagram

## Before Reranking (Original Flow)

```
┌─────────────────┐
│   User Query    │
│ "How to cancel?"│
└────────┬────────┘
         │
         ▼
┌─────────────────────────┐
│   Vector Search         │
│   (Embedding Similarity)│
└────────┬────────────────┘
         │
         │  Returns documents with scores:
         │  Doc A: 0.65 (rank #1)
         │  Doc B: 0.58 (rank #2)
         │  Doc C: 0.45 (rank #3)
         │
         ▼
┌─────────────────────────┐
│   LLM Context           │
│   (Top chunks sent)     │
└────────┬────────────────┘
         │
         ▼
┌─────────────────┐
│  Final Answer   │
└─────────────────┘
```

**Problem:** Doc A might not actually be the best answer!  
Vector similarity ≠ Perfect relevance

---

## After Reranking (Enhanced Flow)

```
┌─────────────────┐
│   User Query    │
│ "How to cancel?"│
└────────┬────────┘
         │
         ▼
┌─────────────────────────┐
│   Vector Search         │
│   (Embedding Similarity)│
└────────┬────────────────┘
         │
         │  Initial results:
         │  Doc A: 0.65 (rank #1)
         │  Doc B: 0.58 (rank #2)  ← Actually most relevant!
         │  Doc C: 0.45 (rank #3)
         │
         ▼
┌─────────────────────────┐
│   🔄 RERANKING          │
│   (Semantic Similarity) │
└────────┬────────────────┘
         │
         │  Process:
         │  1. Generate query embedding
         │  2. Generate doc embeddings
         │  3. Compute cosine similarity
         │  4. Blend scores (70% rerank + 30% original)
         │  5. Re-sort by blended score
         │
         │  After reranking:
         │  Doc B: 0.82 (rank #1) ← NOW FIRST! ✓
         │  Doc A: 0.71 (rank #2)
         │  Doc C: 0.53 (rank #3)
         │
         ▼
┌─────────────────────────┐
│   LLM Context           │
│   (Best chunks first!)  │
└────────┬────────────────┘
         │
         ▼
┌─────────────────┐
│  Better Answer  │
└─────────────────┘
```

**Solution:** Reranking finds the truly best match!  
Better context → Better answers

---

## Detailed Reranking Process

```
┌──────────────────────────────────────────────────────────┐
│                    RERANKING ENGINE                      │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  Input: query + [doc1, doc2, doc3] + embeddingGen       │
│                                                          │
│  ┌────────────────────────────────────────────┐         │
│  │ Step 1: Generate Query Embedding           │         │
│  │   query → embeddingGen → [0.1, 0.5, ...]  │         │
│  └────────────────────────────────────────────┘         │
│                    │                                     │
│  ┌─────────────────▼──────────────────────────┐         │
│  │ Step 2: Generate Document Embeddings       │         │
│  │   doc1 → embeddingGen → [0.2, 0.4, ...]   │         │
│  │   doc2 → embeddingGen → [0.1, 0.6, ...]   │         │
│  │   doc3 → embeddingGen → [0.3, 0.2, ...]   │         │
│  └────────────────────────────────────────────┘         │
│                    │                                     │
│  ┌─────────────────▼──────────────────────────┐         │
│  │ Step 3: Compute Cosine Similarity          │         │
│  │   similarity = dot(query, doc) /           │         │
│  │                (|query| × |doc|)           │         │
│  │                                            │         │
│  │   query ↔ doc1 → similarity = 0.72        │         │
│  │   query ↔ doc2 → similarity = 0.89 ← HIGH │         │
│  │   query ↔ doc3 → similarity = 0.51        │         │
│  └────────────────────────────────────────────┘         │
│                    │                                     │
│  ┌─────────────────▼──────────────────────────┐         │
│  │ Step 4: Blend Scores                       │         │
│  │   blended = (0.7 × rerank) + (0.3 × orig) │         │
│  │                                            │         │
│  │   doc1: (0.7×0.72)+(0.3×0.65) = 0.699     │         │
│  │   doc2: (0.7×0.89)+(0.3×0.58) = 0.797 ✓   │         │
│  │   doc3: (0.7×0.51)+(0.3×0.45) = 0.492     │         │
│  └────────────────────────────────────────────┘         │
│                    │                                     │
│  ┌─────────────────▼──────────────────────────┐         │
│  │ Step 5: Re-sort by Blended Score           │         │
│  │   [doc2, doc1, doc3] ← New order!         │         │
│  └────────────────────────────────────────────┘         │
│                                                          │
│  Output: Reranked documents                             │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

---

## Score Blending Strategy

```
┌─────────────────────────────────────────────────┐
│           WHY BLEND SCORES?                     │
├─────────────────────────────────────────────────┤
│                                                 │
│  ┌──────────────┐      ┌─────────────┐        │
│  │   Original   │      │  Reranking  │        │
│  │  Retrieval   │  +   │   Score     │   =    │
│  │    Score     │      │             │        │
│  │   (30%)      │      │   (70%)     │        │
│  └──────────────┘      └─────────────┘        │
│        │                      │                │
│        │                      │                │
│   Captures:              Captures:             │
│   • Index signals        • Semantic            │
│   • Vector distance        similarity          │
│   • BM25 (if used)       • Cross-query         │
│                            relevance           │
│                                                 │
│               ┌──────────────┐                 │
│               │   Blended    │                 │
│               │    Score     │                 │
│               │ (Best of Both)│                │
│               └──────────────┘                 │
│                                                 │
└─────────────────────────────────────────────────┘
```

**Why 70/30?**
- Reranking is more accurate (higher weight)
- Original score still matters (context from retrieval)
- Prevents over-correction
- **Tunable!** Try 80/20 or 60/40

---

## Implementation in Code

```
LongtermMemoryHelper.GetLongTermMemory()
                │
                ├─► SearchAsync (vector search)
                │            │
                │            ├─► doc1: 0.65
                │            ├─► doc2: 0.58
                │            └─► doc3: 0.45
                │
                ├─► documents.Count > 1 ?
                │            │
                │            YES
                │            │
                │            ▼
                ├─► Helpers.GetEmbeddingGenerator()
                │            │
                │            ▼
                ├─► Reranker.RerankAsync(query, docs, embGen)
                │            │
                │            ├─► Generate embeddings
                │            ├─► Compute similarities
                │            ├─► Blend scores
                │            └─► Re-sort
                │            
                │            ▼
                │       doc2: 0.82 (NEW RANK #1)
                │       doc1: 0.71
                │       doc3: 0.53
                │
                ▼
        Return reranked documents
                │
                ▼
        LLM receives better context
```

---

## Console Output Example

```console
[LookupKernelmemoriesStep] Searching for: "What is the cancellation policy?"

Did a SEARCH
[Initial retrieval - 3 documents found]

[Reranker] Starting rerank for 3 documents
[Reranker] Doc: policy.pdf (part 12) | Original: 0.450 | Rerank: 0.892 | Blended: 0.759
[Reranker] Doc: terms.pdf (part 3)   | Original: 0.620 | Rerank: 0.715 | Blended: 0.687
[Reranker] Doc: faq.pdf (part 8)     | Original: 0.580 | Rerank: 0.623 | Blended: 0.610
[Reranker] Reranking complete. Returning top 3 results
[Reranker] ✓ Reranking changed document order for better relevance!

[ResponseStepWithHalluCheck] Generating answer with context...
```

**What to point out in demo:**
1. Original scores from vector search
2. Reranking scores (semantic similarity)
3. Blended final scores
4. Order change notification
5. Better chunks sent to LLM

---

## Comparison: With vs Without Reranking

```
╔══════════════════════════════════════════════════════════╗
║            WITHOUT RERANKING                             ║
╠══════════════════════════════════════════════════════════╣
║  Query: "How to cancel my subscription?"                 ║
║                                                          ║
║  Retrieved:                                              ║
║  1. policy.pdf (part 5) - Score: 0.68                   ║
║     "Our terms and conditions require..."                ║
║     ❌ Not about cancellation!                           ║
║                                                          ║
║  2. faq.pdf (part 12) - Score: 0.62                     ║
║     "To cancel your subscription, email..."              ║
║     ✓ This is the answer! But rank #2                   ║
║                                                          ║
║  3. billing.pdf (part 3) - Score: 0.55                  ║
║     "Billing cycles are monthly..."                      ║
║     ❌ Not relevant                                      ║
║                                                          ║
║  LLM receives: Terms → Cancellation → Billing            ║
║  Answer quality: Medium (relevant doc is #2)             ║
╚══════════════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════════════╗
║            WITH RERANKING                                ║
╠══════════════════════════════════════════════════════════╣
║  Query: "How to cancel my subscription?"                 ║
║                                                          ║
║  After reranking:                                        ║
║  1. faq.pdf (part 12) - Score: 0.85 ← MOVED UP!        ║
║     "To cancel your subscription, email..."              ║
║     ✓ Perfect match!                                    ║
║                                                          ║
║  2. policy.pdf (part 5) - Score: 0.72 ← MOVED DOWN      ║
║     "Our terms and conditions require..."                ║
║     ✓ Supporting context                                ║
║                                                          ║
║  3. billing.pdf (part 3) - Score: 0.61                  ║
║     "Billing cycles are monthly..."                      ║
║     ✓ Weakly related                                    ║
║                                                          ║
║  LLM receives: Cancellation → Terms → Billing            ║
║  Answer quality: High (best doc is #1)                   ║
╚══════════════════════════════════════════════════════════╝
```

**Key difference:** Best answer moves from #2 to #1!

---

Use these diagrams in your slides for maximum impact! 🎤
