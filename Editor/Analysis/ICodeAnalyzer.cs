namespace WinOS_Editor.Analysis
{
    public interface ICodeAnalyzer
    {
        Task<List<Symbol>> AnalyzeFileAsync(string filePath);
    }
}

