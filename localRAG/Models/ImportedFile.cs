using Microsoft.KernelMemory;

namespace localRAG.Models;
public class ImportedFile
{
    public string Filename { get; set; }
    public string DocId { get; set; }
    public TagCollection Tags { get; set; }
}