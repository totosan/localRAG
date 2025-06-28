using System;
using System.Collections.Generic;
using System.Linq;

namespace localRAG.Plugins
{
    public static class HallucinationCheckPlugin
    {
        /// <summary>
        /// Checks if the generated answer is grounded in the retrieved context chunks.
        /// Returns true if the answer is supported by the context, false if it may be a hallucination.
        /// </summary>
        /// <param name="answer">The generated answer to check.</param>
        /// <param name="retrievedChunks">The list of retrieved context chunks.</param>
        /// <param name="minOverlap">Minimum number of overlapping words to consider as grounded.</param>
        public static bool IsGrounded(string answer, List<string> retrievedChunks, int minOverlap = 3)
        {
            if (string.IsNullOrWhiteSpace(answer) || retrievedChunks == null || retrievedChunks.Count == 0)
                return false;

            var answerWords = new HashSet<string>(answer.Split(new[] { ' ', '.', ',', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in retrievedChunks)
            {
                var chunkWords = new HashSet<string>(chunk.Split(new[] { ' ', '.', ',', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                int overlap = answerWords.Intersect(chunkWords).Count();
                if (overlap >= minOverlap)
                    return true;
            }
            return false;
        }
    }
}
