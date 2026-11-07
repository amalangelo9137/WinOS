namespace WinOS_Editor.Analysis
{
    public enum SymbolKind
    {
        Namespace,
        Class,
        Struct,
        Function,
        Method,
        Variable,
        Field,
        Property,
        Enum,
        EnumMember
    }

    public sealed class Symbol
    {
        // Identity
        public string Name { get; set; } = "";
        public SymbolKind Kind { get; set; }

        // Documentation written by the developer
        public string Summary { get; set; } = "";

        // Function information
        public string? ReturnType { get; set; }
        public List<SymbolParameter> Parameters { get; set; } = [];

        // Hierarchical structure
        public List<Symbol> Children { get; set; } = [];

        // Source location
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public sealed class SymbolParameter
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? DefaultValue { get; set; }
    }
}
