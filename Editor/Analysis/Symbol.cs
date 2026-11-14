namespace WinOS_Editor.Analysis
{
    public enum SymbolKind
    {
        Namespace,
        Class,
        Struct,
        Function,
        Method,
        Constructor,
        Destructor,
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

        // Developer-written documentation
        public string Summary { get; set; } = "";

        // Type information
        public string? ReturnType { get; set; }

        // Function parameters
        public List<SymbolParameter> Parameters { get; set; } = [];

        // Hierarchy
        public List<Symbol> Children { get; set; } = [];

        // Source location
        public string FilePath { get; set; } = "";

        public int Line { get; set; }
        public int Column { get; set; }

        // Declaration information
        public bool IsDefinition { get; set; }

        // Clang declaration identity
        public string? ClangId { get; set; }
    }

    public sealed class SymbolParameter
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? DefaultValue { get; set; }
    }
}