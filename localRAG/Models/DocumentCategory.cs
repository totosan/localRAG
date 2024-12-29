using System.Collections.Generic;

public class DocumentQuestions{
    public List<string> Questions { get; set; } = new List<string>();
}

public class DocumentCategory
{
    // A dictionary where the key is the subcategory name and the value is a list of questions/keywords
    public Dictionary<string, DocumentQuestions> Subcategories { get; set; } = new Dictionary<string, DocumentQuestions>();
}

public class DocumentCategories
{
    // A dictionary where the key is the main category name and the value is a DocumentCategory object
    public Dictionary<string, DocumentCategory> Categories { get; set; } = new Dictionary<string, DocumentCategory>();
}