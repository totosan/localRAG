using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace localRAG.Utilities
{
    #region Slide 6: Keyword-Extraktion
    
    /// <summary>
    /// 🎤 SLIDE 6: Keyword-Extraktion (4 Methods)
    /// 
    /// Simple keyword extraction using TF-IDF-inspired approach and phrase extraction (RAKE-like)
    /// Suitable for demonstration purposes in RAG presentations
    /// 
    /// Methods:
    /// 1. TF-IDF Frequency Analysis (Lines 80-95)
    /// 2. Technical Term Detection (Lines 97-105)
    /// 3. RAKE Phrase Extraction (Lines 115-150)
    /// 4. Named Entity Recognition (Lines 154-178)
    /// 
    /// Demo Breakpoint: Line 54 (after ExtractKeywords returns)
    /// Watch Variables: extractedKeywords, namedEntities
    /// </summary>
    public static class KeywordExtractor
    {
        private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            // German stopwords
            "der", "die", "das", "und", "oder", "aber", "in", "auf", "von", "zu", "mit", "für",
            "ist", "sind", "war", "waren", "wird", "werden", "wurde", "wurden", "hat", "haben",
            "ein", "eine", "einer", "einem", "einen", "des", "dem", "den", "als", "auch", "an",
            "bei", "nach", "um", "am", "im", "zum", "zur", "über", "unter", "durch", "vor",
            
            // English stopwords
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
            "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "having",
            "do", "does", "did", "doing", "would", "should", "could", "ought", "will", "shall",
            "may", "might", "must", "can", "this", "that", "these", "those", "i", "you", "he",
            "she", "it", "we", "they", "them", "their", "what", "which", "who", "when", "where",
            "why", "how", "all", "each", "every", "both", "few", "more", "most", "other", "some",
            "such", "no", "nor", "not", "only", "own", "same", "so", "than", "too", "very",
            "from", "up", "down", "out", "off", "over", "under", "again", "further", "then",
            "once", "here", "there", "about", "above", "after", "before", "below", "between",
            "during", "through", "into", "by", "as", "if", "because", "while", "until"
        };

        private static readonly HashSet<string> TechnicalIndicators = new(StringComparer.OrdinalIgnoreCase)
        {
            // API & Web Technologies
            "api", "sdk", "http", "https", "rest", "graphql", "grpc", "json", "xml", "yaml",
            "oauth", "jwt", "cors", "webhook", "endpoint", "middleware",
            
            // Data Storage & Databases
            "sql", "nosql", "mongodb", "postgresql", "redis", "cosmos", "dynamodb",
            "database", "cache", "blob", "storage", "repository", "index", "schema",
            
            // Cloud & Infrastructure
            "docker", "kubernetes", "k8s", "azure", "aws", "gcp", "cloud", "serverless",
            "container", "pod", "deployment", "service", "ingress", "namespace",
            
            // Architecture & Design
            "microservice", "architecture", "pattern", "framework", "library", "plugin",
            "monolith", "distributed", "event-driven", "cqrs", "saga", "circuit-breaker",
            
            // AI & Machine Learning
            "llm", "gpt", "embedding", "vector", "semantic", "transformer", "bert",
            "openai", "ollama", "anthropic", "claude", "gemini", "model", "inference",
            "fine-tuning", "prompt", "completion", "chat", "assistant",
            
            // RAG & Knowledge Management
            "rag", "retrieval", "augmented", "generation", "knowledge", "context",
            "chunk", "chunking", "similarity", "cosine", "relevance", "rerank",
            "reranking", "hybrid-search", "semantic-search", "vector-search",
            "kernel-memory", "semantic-kernel", "memory", "recall",
            
            // Document Processing & Text Extraction
            "pdf", "ocr", "tesseract", "docling", "pypdf", "pdfplumber", "tika",
            "markdown", "html", "plaintext", "document", "parser", "extractor",
            "layout", "table", "figure", "caption", "header", "footer", "metadata",
            "annotation", "bookmark", "toc", "page", "paragraph", "sentence",
            
            // NLP & Text Analysis
            "nlp", "tokenization", "lemmatization", "stemming", "stopword",
            "tf-idf", "bm25", "rake", "named-entity", "ner", "pos-tagging",
            "keyword", "phrase", "ngram", "bigram", "trigram", "collocation",
            "sentiment", "classification", "clustering", "topic-modeling",
            
            // Search & Retrieval
            "elasticsearch", "solr", "lucene", "search", "query", "filter",
            "facet", "aggregation", "ranking", "scoring", "boost", "fuzzy",
            "wildcard", "proximity", "phrase-match", "inverted-index",
            
            // Programming & Development
            "csharp", "dotnet", "python", "typescript", "javascript", "java",
            "async", "await", "task", "thread", "parallel", "concurrent",
            "exception", "logging", "telemetry", "metric", "trace", "span",
            
            // Security & Authentication
            "encryption", "decryption", "hash", "salt", "certificate", "tls",
            "ssl", "authentication", "authorization", "rbac", "permissions",
            
            // Testing & Quality
            "unittest", "integration-test", "e2e", "mock", "stub", "fixture",
            "assertion", "coverage", "benchmark", "profiling"
        };

        /// <summary>
        /// Extract keywords from text using multiple methods:
        /// 1. High-frequency words (TF-IDF-inspired)
        /// 2. Technical terms
        /// 3. Key phrases (RAKE-inspired)
        /// </summary>
        public static List<string> ExtractKeywords(string text, int maxKeywords = 10)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Method 1: Extract single-word keywords (high frequency, non-stopwords)
            var singleWords = ExtractFrequentWords(text, maxKeywords / 2);
            foreach (var word in singleWords)
                keywords.Add(word);

            // Method 2: Extract technical terms
            var technicalTerms = ExtractTechnicalTerms(text);
            foreach (var term in technicalTerms)
                keywords.Add(term);

            // Method 3: Extract key phrases (2-3 word combinations)
            var phrases = ExtractKeyPhrases(text, maxKeywords / 3);
            foreach (var phrase in phrases)
                keywords.Add(phrase);

            return keywords.Take(maxKeywords).ToList();
        }

        #region 6.1: Method 1 - TF-IDF Frequency Analysis
        
        /// <summary>
        /// Extract frequently occurring words (TF-inspired, without IDF since we process per-chunk)
        /// Demonstrates: Frequency counting, stopword filtering, deterministic ordering
        /// </summary>
        private static List<string> ExtractFrequentWords(string text, int topN)
        {
            var words = Regex.Split(text.ToLowerInvariant(), @"\W+")
                .Where(w => w.Length > 3 && !Stopwords.Contains(w))
                .ToList();

            var wordFrequency = words
                .GroupBy(w => w)
                .Select(g => new { Word = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Word) // Deterministic ordering
                .Take(topN)
                .Select(x => x.Word)
                .ToList();

            return wordFrequency;
        }
        
        #endregion
        
        #region 6.2: Method 2 - Technical Term Detection
        
        /// <summary>
        /// Identify technical terms from a predefined list
        /// Demonstrates: Domain-specific vocabulary matching
        /// </summary>
        private static List<string> ExtractTechnicalTerms(string text)
        {
            var lowerText = text.ToLowerInvariant();
            return TechnicalIndicators
                .Where(term => lowerText.Contains(term))
                .ToList();
        }
        
        #endregion
        
        #region 6.3: Method 3 - RAKE Phrase Extraction
        
        /// <summary>
        /// Extract key phrases using RAKE approach (Rapid Automatic Keyword Extraction)
        /// 
        /// RAKE Algorithm:
        /// 1. Split text into candidate phrases using stopwords as DELIMITERS
        /// 2. Keep content words together (e.g., "cloud native architecture")
        /// 3. Calculate word scores based on co-occurrence
        /// 4. Score phrases by summing their word scores
        /// 
        /// Demonstrates: Multi-word phrase detection with stopwords as boundaries
        /// </summary>
        private static List<string> ExtractKeyPhrases(string text, int topN)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // Step 1: Create stopword/punctuation delimiter pattern
            var stopwordPattern = string.Join("|", Stopwords.Select(Regex.Escape));
            var delimiterPattern = $@"\b({stopwordPattern})\b|[^\w\s]+";
            
            // Step 2: Split text into candidate phrases (stopwords act as delimiters)
            var candidatePhrases = Regex.Split(text.ToLowerInvariant(), delimiterPattern, RegexOptions.IgnoreCase)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(p => p.Length > 3) // Skip very short fragments
                .Where(p => !Stopwords.Contains(p)) // Skip any remaining stopwords
                .ToList();

            // Step 3: Extract multi-word phrases (2-4 words)
            var multiWordPhrases = candidatePhrases
                .Select(phrase => phrase.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(words => words.Length >= 2 && words.Length <= 4) // Multi-word phrases only
                .Where(words => words.All(w => w.Length > 2)) // All words must be substantial
                .Select(words => string.Join(" ", words))
                .ToList();

            // Step 4: Calculate word frequencies for scoring
            var allWords = multiWordPhrases
                .SelectMany(phrase => phrase.Split(' '))
                .ToList();

            var wordFrequency = allWords
                .GroupBy(w => w)
                .ToDictionary(g => g.Key, g => g.Count());

            // Step 5: Score phrases (sum of word frequencies * phrase length bonus)
            var phraseScores = multiWordPhrases
                .GroupBy(p => p)
                .Select(g => new
                {
                    Phrase = g.Key,
                    Frequency = g.Count(),
                    WordScore = g.Key.Split(' ').Sum(w => wordFrequency.GetValueOrDefault(w, 0)),
                    LengthBonus = g.Key.Split(' ').Length
                })
                .Select(x => new
                {
                    x.Phrase,
                    // Combined score: word co-occurrence + frequency + length bonus
                    Score = (x.WordScore * x.Frequency) + (x.LengthBonus * 2)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Phrase) // Deterministic ordering
                .Take(topN)
                .Select(x => x.Phrase)
                .ToList();

            return phraseScores;
        }
        
        #endregion
        
        #region 6.4: Method 4 - Named Entity Recognition

        /// <summary>
        /// Extract named entities (simplified - looks for capitalized word sequences)
        /// Useful for extracting proper nouns, product names, etc.
        /// </summary>
        public static List<string> ExtractNamedEntities(string text, int maxEntities = 5)
        {
            // Match sequences of capitalized words (potential named entities)
            var entityPattern = @"\b[A-ZÄÖÜ][a-zäöüß]+(?:\s+[A-ZÄÖÜ][a-zäöüß]+)*\b";
            var matches = Regex.Matches(text, entityPattern);

            var entities = matches
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(e => e.Length > 2 && !Stopwords.Contains(e))
                .GroupBy(e => e)
                .Select(g => new { Entity = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Entity)
                .Take(maxEntities)
                .Select(x => x.Entity)
                .ToList();

            return entities;
        }
        
        #endregion // 6.4: Named Entity Recognition
        
        #endregion // Slide 6: Keyword-Extraktion
    }
}
