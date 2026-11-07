namespace WinOS_Editor
{
    public class Document
    {
        public required string FilePath { get; set; }
        public string Content { get; set; } = "";

        public bool IsModified { get; set; } = false;

        public string FileName =>
            System.IO.Path.GetFileName(FilePath);
    }
}