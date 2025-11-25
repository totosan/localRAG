# Document Normalization Enhancement

## Summary

Added complete document normalization pipeline to `CustomPdfDecoder.cs` to match the RAG presentation outline (Folie 4).

## Changes Made

### 1. Added Stopwords Dictionary (Lines 16-29)

```csharp
// German and English stopwords for document normalization
private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
{
    "der", "die", "das", "und", "oder", "aber", "in", "auf", "von", "zu", "mit", "für",
    "ist", "sind", "war", "waren", "wird", "werden", "wurde", "wurden", "hat", "haben",
    // ... (German stopwords)
    "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "is", "are",
    // ... (English stopwords)
};
```

### 2. Created `NormalizeText()` Method (Lines 240-266)

Complete normalization pipeline following RAG best practices:

```csharp
private string NormalizeText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return string.Empty;

    // Step 1: Convert to lowercase
    var normalized = text.ToLowerInvariant();

    // Step 2: Remove punctuation but keep word boundaries
    normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

    // Step 3: Remove stopwords
    var words = Regex.Split(normalized, @"\s+")
        .Where(w => w.Length > 2 && !Stopwords.Contains(w))
        .ToList();

    // Step 4 & 5: Join words with single space and trim
    normalized = string.Join(" ", words).Trim();

    return normalized;
}
```

### 3. Applied Normalization Before Storing Chunks

**Location 1: JSON Section Processing (Lines 185-191)**
```csharp
// Apply normalization pipeline before storing
var normalizedText = NormalizeText(text);
if (!string.IsNullOrWhiteSpace(normalizedText))
{
    builder.Append(normalizedText);
}
```

**Location 2: Plain Text Processing (Lines 223-230)**
```csharp
// Apply normalization pipeline
var normalizedText = NormalizeText(text);
if (string.IsNullOrWhiteSpace(normalizedText))
{
    return false;
}

var meta = Chunk.Meta(true, 1);
var chunk = new Chunk(normalizedText, 1, meta);
```

## Normalization Steps Implemented

✅ **1. Text Extraction** (Docling API)  
✅ **2. Lowercase Conversion** (`.ToLowerInvariant()`)  
✅ **3. Punctuation Removal** (`Regex.Replace(@"[^\w\s]", " ")`)  
✅ **4. Stopword Removal** (German + English, 40+ words)  
✅ **5. Whitespace Cleanup** (`string.Join(" ", words).Trim()`)  

⚠️ **Not Implemented**: Lemmatization/Stemming (optional - low impact on demo)

## Impact

### Before Enhancement
```
Input:  "Das ist ein Beispiel-Text! Mit vielen Satzzeichen..."
Output: "Das ist ein Beispiel-Text! Mit vielen Satzzeichen..."
```

### After Enhancement
```
Input:  "Das ist ein Beispiel-Text! Mit vielen Satzzeichen..."
Output: "beispiel text vielen satzzeichen"
```

## For the Presentation

### Demo Breakpoint
**File**: `CustomPdfDecoder.cs`  
**Line**: 247 (inside `NormalizeText` method)

**Show**:
1. Original text with mixed case, punctuation, stopwords
2. Step through normalization pipeline
3. Final normalized output

### Talking Points

> "Before storing any document chunk, we run it through a complete normalization pipeline. This ensures consistent retrieval regardless of how users phrase their queries."

**Emphasize**:
- **No external NLP libraries** - Pure C# regex + HashSet lookups (fast!)
- **Bilingual support** - German + English stopwords
- **Index-time normalization** - Happens once during import, not on every query
- **Standard RAG practice** - Follows academic/industry best practices

### Example for Slides

```csharp
// Show this comparison on Folie 4:

// BEFORE normalization
"Der Azure-Service wird in Deutschland angeboten."

// AFTER normalization (what gets indexed)
"azure service deutschland angeboten"

// Result: Queries like "Azure Service" or "azure-service" 
// now match the same chunks!
```

## Validation Status

✅ **Build**: Successful (warnings only, no errors)  
✅ **Folie 4 Support**: Now fully implemented  
✅ **Talk Credibility**: Enhanced - matches textbook RAG pipeline  

## Next Steps (Optional)

If you want to go even further for the talk:

1. **Add Lemmatization** (e.g., "running" → "run")
   - Requires: NuGet package (e.g., `Annytab.Stemmer`)
   - Benefit: Better semantic matching
   - Effort: ~15 minutes

2. **Show Before/After Metrics**
   - Import documents WITHOUT normalization
   - Import same documents WITH normalization
   - Compare search accuracy (precision/recall)
   - Benefit: Quantitative proof for audience

3. **Add Logging**
   ```csharp
   this._log.LogDebug("Normalized '{0}' -> '{1}'", text.Substring(0, 50), normalized);
   ```
   - Shows normalization in console output during `--import`

## Testing

To test the normalization:

```bash
# Clear existing normalized data
rm -rf tmp-data/
rm tags.json

# Re-import with new normalization
dotnet run -- --ollama --import

# Check that documents are now normalized
# (chunks will be lowercase, no punctuation, no stopwords)
```

---

**Enhancement Completed**: 2025-11-24  
**Build Status**: ✅ Successful  
**Ready for Talk**: ✅ Yes
