namespace localRAG.Models
{
    public class DocumentsSimple
    {
        public string SourceName { get; set; }
        public string FilePath { get; set; }
        public string PartitionPath { get; set; }
        public string DocumentId { get; set; }
        public int PartitionNumber { get; set; }
        public string Content { get; set; }

        public float Score { get; set; }
    }
}