namespace WinOS_Editor
{
    public class DocMgr
    {
        private readonly List<Document> _documents = [];
        public IReadOnlyList<Document> Documents => _documents;
        public Document? ActiveDocument { get; private set; }

        // document opening/closing
        public Document OpenDocument(string filePath)
        {
            // Check whether the file is already open.
            Document? existing = _documents.Find(
                d => d.FilePath == filePath);

            if (existing != null)
            {
                ActiveDocument = existing;
                return existing;
            }

            // Create a document from the file.
            Document document = new()
            {
                FilePath = filePath,
                Content = System.IO.File.ReadAllText(filePath)
            };

            _documents.Add(document);
            ActiveDocument = document;

            return document;
        }
        public void CloseDocument(Document document)
        {
            if (_documents.Remove(document))
            {
                if (ActiveDocument == document)
                {
                    ActiveDocument = _documents.LastOrDefault();
                }
            }
        }

        // document saving
        public void SaveDocument(Document document)
        {
            System.IO.File.WriteAllText(
                document.FilePath,
                document.Content
            );

            document.IsModified = false;
        }
        public void SaveAllDocuments()
        {
            foreach (Document document in _documents)
            {
                if (document.IsModified)
                    SaveDocument(document);
            }
        }

        public void Clear()
        {
            _documents.Clear();
            ActiveDocument = null;
        }

        public void SetActiveDocument(Document document)
        {
            if (_documents.Contains(document))
                ActiveDocument = document;
        }
    }
}
