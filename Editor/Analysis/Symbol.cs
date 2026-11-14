namespace WinOS_Editor.Analysis
{
    public enum SymbolKind
    {
        Namespace,
        Class,
        Struct,
        Union,
        Function,
        Method,
        Constructor,
        Destructor,
        Variable,
        Field,
        Property,
        Enum,
        EnumMember,
        Typedef,
        TypeAlias,
        Using
    }

    public sealed class Symbol
    {
        // Display identity
        public string Name { get; set; } = "";
        public SymbolKind Kind { get; set; }

        // Developer-written documentation
        public string Summary { get; set; } = "";

        // Type information
        public string? Type { get; set; }
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

        // Clang AST identity. ClangIds retains every redeclaration ID
        // so a chain of previousDecl links can be resolved after merging.
        public string? ClangId { get; set; }
        public string? PreviousDeclId { get; set; }
        public List<string> ClangIds { get; set; } = [];

        // Used internally for overload and redeclaration matching.
        public string? MangledName { get; set; }
        public string? Signature { get; set; }
    }

    public sealed class SymbolParameter
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? DefaultValue { get; set; }
    }
}
