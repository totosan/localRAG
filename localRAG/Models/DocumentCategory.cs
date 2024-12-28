using System.Collections.Generic;

public class DocumentCategory
{
    // A dictionary where the key is the subcategory name and the value is a list of questions/keywords
    public Dictionary<string, List<string>> Subcategories { get; set; } = new Dictionary<string, List<string>>();
}

public class DocumentCategories
{
    // A dictionary where the key is the main category name and the value is a DocumentCategory object
    public Dictionary<string, DocumentCategory> Categories { get; set; } = new Dictionary<string, DocumentCategory>();
}