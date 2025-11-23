using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace localRAG.Utilities
{
    /// <summary>
    /// Simple keyword extraction using TF-IDF-inspired approach and phrase extraction (RAKE-like)
    /// Suitable for demonstration purposes in RAG presentations
    /// </summary>
    public static class KeywordExtractor
    {
        private static readonly HashSet<string> GermanStopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "der", "die", "das", "und", "oder", "aber", "in", "auf", "von", "zu", "mit", "für",
            "ist", "sind", "war", "waren", "wird", "werden", "wurde", "wurden", "hat", "haben",
            "ein", "eine", "einer", "einem", "einen", "des", "dem", "den", "als", "auch", "an",
            "bei", "nach", "um", "am", "im", "zum", "zur", "über", "unter", "durch", "vor",
            "this", "that", "the", "and", "or", "but", "in", "on", "at", "to", "for", "of",
            "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did"
        };

        private static readonly HashSet<string> TechnicalIndicators = new(StringComparer.OrdinalIgnoreCase)
        {
            "api", "sdk", "http", "rest", "json", "xml", "sql", "nosql", "docker", "kubernetes",
            "microservice", "architecture", "pattern", "framework", "library", "database", "cache"
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

        /// <summary>
        /// Extract frequently occurring words (TF-inspired, without IDF since we process per-chunk)
        /// </summary>
        private static List<string> ExtractFrequentWords(string text, int topN)
        {
            var words = Regex.Split(text.ToLowerInvariant(), @"\W+")
                .Where(w => w.Length > 3 && !GermanStopwords.Contains(w))
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

        /// <summary>
        /// Identify technical terms from a predefined list
        /// </summary>
        private static List<string> ExtractTechnicalTerms(string text)
        {
            var lowerText = text.ToLowerInvariant();
            return TechnicalIndicators
                .Where(term => lowerText.Contains(term))
                .ToList();
        }

        /// <summary>
        /// Extract key phrases using RAKE-inspired approach (noun phrases between stopwords)
        /// </summary>
        private static List<string> ExtractKeyPhrases(string text, int topN)
        {
            // Split by sentence-ending punctuation
            var sentences = Regex.Split(text, @"[.!?;]")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var phrases = new List<string>();

            foreach (var sentence in sentences)
            {
                // Split sentence into words
                var words = Regex.Split(sentence.ToLowerInvariant(), @"\W+")
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .ToList();

                // Build phrases between stopwords
                var currentPhrase = new List<string>();
                
                foreach (var word in words)
                {
                    if (GermanStopwords.Contains(word))
                    {
                        // Stopword encountered - save accumulated phrase if valid
                        if (currentPhrase.Count >= 2 && currentPhrase.Count <= 3)
                        {
                            phrases.Add(string.Join(" ", currentPhrase));
                        }
                        currentPhrase.Clear();
                    }
                    else if (word.Length > 2) // Ignore very short words
                    {
                        currentPhrase.Add(word);
                    }
                }

                // Don't forget the last phrase
                if (currentPhrase.Count >= 2 && currentPhrase.Count <= 3)
                {
                    phrases.Add(string.Join(" ", currentPhrase));
                }
            }

            // Score phrases by word frequency (simplified RAKE scoring)
            var phraseScores = phrases
                .GroupBy(p => p)
                .Select(g => new { Phrase = g.Key, Score = g.Count() * g.Key.Split(' ').Length })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Phrase) // Deterministic ordering
                .Take(topN)
                .Select(x => x.Phrase)
                .ToList();

            return phraseScores;
        }

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
                .Where(e => e.Length > 2 && !GermanStopwords.Contains(e))
                .GroupBy(e => e)
                .Select(g => new { Entity = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Entity)
                .Take(maxEntities)
                .Select(x => x.Entity)
                .ToList();

            return entities;
        }
    }
}
