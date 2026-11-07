namespace WinOS_Editor
{
    public class FrameworkProject
    {
        // framework metadata
        public string Name { get; set; } = "NewFramework";
        public string Version { get; set; } = "1.0.0";
        public string Description { get; set; } = "";

        // source file dirs
        public List<string> SourceFiles { get; set; } = [];

        // for the ide workspace
        public List<string> OpenDocuments { get; set; } = [];
        public string? ActiveDocumentPath { get; set; }
    }
}
