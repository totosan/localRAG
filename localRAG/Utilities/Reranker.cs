using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using localRAG.Models;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;

namespace localRAG.Utilities
{
    #region Slide 9: Reranking Implementation
    
    /// <summary>
    /// 🎤 SLIDE 9: Reranking - Improving Retrieval Precision
    /// 
    /// Simple but effective reranker for RAG that computes semantic similarity scores
    /// between the user query and each retrieved chunk, then reorders by relevance.
    /// Perfect for demonstrating reranking in a talk!
    /// 
    /// Demo Breakpoint: Line 69 (inside reranking loop)
    /// Watch Variables: similarity, blendedScore, rerankedDocs
    /// 
    /// Key Concepts:
    /// - Two-stage retrieval: Fast recall (vector DB) → Precise rerank (similarity)
    /// - Blended scoring: 70% rerank + 30% original
    /// - Visible console logs show reordering effect
    /// </summary>
    public static class Reranker
    {
        #region 9.1: Semantic Reranking (Embedding-based)
        
        /// <summary>
        /// Reranks documents based on semantic similarity to the query.
        /// Uses embeddings to compute cosine similarity for accurate relevance scoring.
        /// 
        /// This is the MAIN reranking method - show this during your demo!
        /// </summary>
        /// <param name="query">The user's search query</param>
        /// <param name="documents">List of documents to rerank</param>
        /// <param name="embeddingGenerator">Kernel Memory embedding generator</param>
        /// <param name="topK">Number of top results to return (default: all)</param>
        /// <returns>Reranked list of documents ordered by relevance</returns>
        public static async Task<List<DocumentsSimple>> RerankAsync(
            string query, 
            List<DocumentsSimple> documents, 
            ITextEmbeddingGenerator embeddingGenerator,
            int topK = -1)
        {
            if (documents == null || documents.Count == 0)
            {
                TraceLogger.Log("[Reranker] No documents to rerank");
                return documents ?? new List<DocumentsSimple>();
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                TraceLogger.Log("[Reranker] Empty query, returning original order");
                return documents;
            }

            try
            {
                TraceLogger.Log($"[Reranker] Starting rerank for {documents.Count} documents", echoToConsole: true);
                
                // Generate query embedding
                var queryEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(query);
                
                // Generate embeddings for each document and compute similarity scores
                var rerankedDocs = new List<(DocumentsSimple doc, float score)>();
                
                foreach (var doc in documents)
                {
                    // Generate embedding for document content
                    var docEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(doc.Content);
                    
                    // Compute cosine similarity between query and document (using the Data property)
                    float similarity = CosineSimilarity(queryEmbedding.Data, docEmbedding.Data);
                    
                    // Blend original retrieval score with reranking score (70% rerank, 30% original)
                    float blendedScore = (0.7f * similarity) + (0.3f * doc.Score);
                    
                    rerankedDocs.Add((doc, blendedScore));
                    
                    TraceLogger.Log($"[Reranker] Doc: {doc.SourceName} (part {doc.PartitionNumber}) | Original: {doc.Score:F3} | Rerank: {similarity:F3} | Blended: {blendedScore:F3}");
                }
                
                // Sort by blended score (descending) and update scores
                var sorted = rerankedDocs
                    .OrderByDescending(x => x.score)
                    .Select(x => 
                    {
                        x.doc.Score = x.score; // Update with blended score
                        return x.doc;
                    })
                    .ToList();
                
                // Return top-k if specified
                int resultCount = topK > 0 ? Math.Min(topK, sorted.Count) : sorted.Count;
                var finalResults = sorted.Take(resultCount).ToList();
                
                TraceLogger.Log($"[Reranker] Reranking complete. Returning top {finalResults.Count} results", echoToConsole: true);
                
                // Show reordering effect
                if (documents.Count > 1 && finalResults.Count > 1)
                {
                    bool reordered = !documents.Take(finalResults.Count)
                        .SequenceEqual(finalResults.Take(documents.Count));
                    
                    if (reordered)
                    {
                        TraceLogger.Log("[Reranker] ✓ Reranking changed document order for better relevance!", echoToConsole: true);
                    }
                }
                
                return finalResults;
            }
            catch (Exception ex)
            {
                TraceLogger.Log($"[Reranker] Error during reranking: {ex.Message}. Returning original order.", echoToConsole: true);
                return documents;
            }
        }
        
        #endregion
        
        #region 9.2: Keyword-based Reranking (Fallback)
        
        /// <summary>
        /// Simpler keyword-based reranker that doesn't require embeddings.
        /// Good fallback or for demonstrating different reranking strategies.
        /// 
        /// Show this as alternative approach: keyword overlap vs semantic similarity
        /// </summary>
        /// <param name="query">The user's search query</param>
        /// <param name="documents">List of documents to rerank</param>
        /// <param name="topK">Number of top results to return (default: all)</param>
        /// <returns>Reranked list of documents ordered by keyword overlap</returns>
        public static List<DocumentsSimple> ReRankByKeywordOverlap(
            string query, 
            List<DocumentsSimple> documents,
            int topK = -1)
        {
            if (documents == null || documents.Count == 0 || string.IsNullOrWhiteSpace(query))
            {
                return documents ?? new List<DocumentsSimple>();
            }
            
            TraceLogger.Log($"[Reranker] Starting keyword-based rerank for {documents.Count} documents");
            
            // Extract keywords from query
            var queryKeywords = KeywordExtractor.ExtractKeywords(query, maxKeywords: 10)
                .Select(k => k.ToLowerInvariant())
                .ToHashSet();
            
            // Score each document by keyword overlap
            var scored = documents.Select(doc =>
            {
                // Count how many query keywords appear in the document
                var docText = doc.Content.ToLowerInvariant();
                int keywordMatches = queryKeywords.Count(keyword => docText.Contains(keyword));
                
                // Blend keyword score with original retrieval score
                float keywordScore = keywordMatches / (float)Math.Max(queryKeywords.Count, 1);
                float blendedScore = (0.6f * keywordScore) + (0.4f * doc.Score);
                
                doc.Score = blendedScore;
                TraceLogger.Log($"[Reranker-Keyword] Doc: {doc.SourceName} | Matches: {keywordMatches}/{queryKeywords.Count} | Score: {blendedScore:F3}");
                
                return doc;
            })
            .OrderByDescending(d => d.Score)
            .ToList();
            
            int resultCount = topK > 0 ? Math.Min(topK, scored.Count) : scored.Count;
            return scored.Take(resultCount).ToList();
        }
        
        #endregion
        
        #region 9.3: Cosine Similarity Helper
        
        /// <summary>
        /// Computes cosine similarity between two embedding vectors.
        /// Returns a value between -1 and 1, where 1 means identical direction.
        /// 
        /// Core math behind semantic similarity - simple but powerful!
        /// </summary>
        private static float CosineSimilarity(ReadOnlyMemory<float> vectorA, ReadOnlyMemory<float> vectorB)
        {
            var a = vectorA.Span;
            var b = vectorB.Span;
            
            if (a.Length != b.Length)
            {
                throw new ArgumentException("Vectors must have the same dimension");
            }
            
            float dotProduct = 0f;
            float magnitudeA = 0f;
            float magnitudeB = 0f;
            
            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }
            
            float magnitude = (float)Math.Sqrt(magnitudeA) * (float)Math.Sqrt(magnitudeB);
            
            if (magnitude == 0f)
            {
                return 0f;
            }
            
            return dotProduct / magnitude;
        }
        
        #endregion // 9.3: Cosine Similarity Helper
        
        #endregion // Slide 9: Reranking Implementation
    }
}
