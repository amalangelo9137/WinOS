using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace WinOS_Editor.Analysis
{
    public sealed class ClangCodeAnalyzer : ICodeAnalyzer
    {
        private readonly string _clangPath;

        public ClangCodeAnalyzer(string llvmPath)
        {
            _clangPath = Path.Combine(llvmPath, "clang++.exe");

            if (!File.Exists(_clangPath))
                throw new FileNotFoundException(
                    "clang++.exe was not found.", _clangPath);
        }

        public async Task<List<Symbol>> AnalyzeFileAsync(string filePath)
        {
            filePath = Path.GetFullPath(filePath);

            string astJson = await GetAstJsonAsync(filePath);

            using JsonDocument document = JsonDocument.Parse(astJson);

            List<Symbol> symbols = [];

            if (document.RootElement.TryGetProperty(
                "inner", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    Symbol? symbol = ParseSymbol(child, filePath);

                    if (symbol != null)
                        AddOrMerge(symbols, symbol);
                }
            }

            return symbols;
        }

        private async Task<string> GetAstJsonAsync(string filePath)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = _clangPath,
                WorkingDirectory = Path.GetDirectoryName(filePath)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-Xclang");
            startInfo.ArgumentList.Add("-ast-dump=json");
            startInfo.ArgumentList.Add("-fparse-all-comments");
            startInfo.ArgumentList.Add("-fsyntax-only");
            startInfo.ArgumentList.Add(filePath);

            using Process process = new() { StartInfo = startInfo };

            process.Start();

            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string output = await outputTask;
            string errors = await errorTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Clang analysis failed:\n{errors}");

            return output;
        }

        private Symbol? ParseSymbol(JsonElement node, string sourceFile)
        {
            string clangKind = GetString(node, "kind");

            if (string.IsNullOrEmpty(clangKind) || IsImplicit(node))
                return null;

            if (!IsFromSourceFile(node, sourceFile))
                return null;

            SymbolKind? kind = GetSymbolKind(clangKind);

            if (kind == null)
                return null;

            string name = GetString(node, "name");

            if (string.IsNullOrWhiteSpace(name))
                return null;

            Symbol symbol = new()
            {
                Name = name,
                Kind = kind.Value,
                FilePath = sourceFile,
                Line = GetLocationNumber(node, "line"),
                Column = GetLocationNumber(node, "col"),
                Summary = GetDocumentation(node),
                IsDefinition = IsDefinition(node, clangKind)
            };

            if (IsCallable(clangKind))
            {
                symbol.ReturnType = GetReturnType(node);
                symbol.Parameters = GetParameters(node);
            }

            if (IsContainer(clangKind) &&
                node.TryGetProperty("inner", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    Symbol? childSymbol = ParseSymbol(child, sourceFile);

                    if (childSymbol != null)
                        AddOrMerge(symbol.Children, childSymbol);
                }
            }

            return symbol;
        }

        private static SymbolKind GetRecordKind(string node)
        {
            return node == "struct"
                ? SymbolKind.Struct
                : SymbolKind.Class;
        }

        private static SymbolKind? GetSymbolKind(string kind)
        {
            return kind switch
            {
                "NamespaceDecl" => SymbolKind.Namespace,
                "CXXRecordDecl" or "RecordDecl" => GetRecordKind(kind),
                "FunctionDecl" => SymbolKind.Function,
                "CXXMethodDecl" => SymbolKind.Method,
                "CXXConstructorDecl" => SymbolKind.Constructor,
                "CXXDestructorDecl" => SymbolKind.Destructor,
                "VarDecl" => SymbolKind.Variable,
                "FieldDecl" => SymbolKind.Field,
                "EnumDecl" => SymbolKind.Enum,
                "EnumConstantDecl" => SymbolKind.EnumMember,
                _ => null
            };
        }

        private static bool IsContainer(string kind)
        {
            return kind is
                "NamespaceDecl" or
                "CXXRecordDecl" or
                "RecordDecl" or
                "EnumDecl";
        }

        private static bool IsCallable(string kind)
        {
            return kind is
                "FunctionDecl" or
                "CXXMethodDecl" or
                "CXXConstructorDecl" or
                "CXXDestructorDecl";
        }

        private static List<SymbolParameter> GetParameters(JsonElement node)
        {
            List<SymbolParameter> parameters = [];

            if (!node.TryGetProperty("inner", out JsonElement children))
                return parameters;

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (GetString(child, "kind") != "ParmVarDecl")
                    continue;

                string type = "";

                if (child.TryGetProperty("type", out JsonElement typeElement))
                    type = GetString(typeElement, "qualType");

                parameters.Add(new SymbolParameter
                {
                    Name = GetString(child, "name"),
                    Type = type,
                    DefaultValue = GetDefaultValue(child)
                });
            }

            return parameters;
        }

        private static string? GetDefaultValue(JsonElement parameter)
        {
            if (!parameter.TryGetProperty("inner", out JsonElement children))
                return null;

            // A parameter with a default argument has an expression
            // child in addition to its declaration metadata.
            foreach (JsonElement child in children.EnumerateArray())
            {
                string kind = GetString(child, "kind");

                if (kind is "IntegerLiteral" or "FloatingLiteral"
                    or "StringLiteral" or "CharacterLiteral"
                    or "CXXBoolLiteralExpr" or "CXXNullPtrLiteralExpr"
                    or "UnaryOperator" or "DeclRefExpr"
                    or "CXXConstructExpr")
                {
                    return GetExpressionValue(child);
                }
            }

            return null;
        }

        private static string GetExpressionValue(JsonElement node)
        {
            if (node.TryGetProperty("value", out JsonElement value))
                return value.ToString();

            if (node.TryGetProperty("name", out JsonElement name))
                return name.ToString();

            if (node.TryGetProperty("opcode", out JsonElement opcode))
                return opcode.ToString();

            if (node.TryGetProperty("inner", out JsonElement children))
            {
                List<string> parts = [];

                foreach (JsonElement child in children.EnumerateArray())
                {
                    string part = GetExpressionValue(child);

                    if (!string.IsNullOrEmpty(part))
                        parts.Add(part);
                }

                return string.Join(" ", parts);
            }

            return "";
        }

        private static string? GetReturnType(JsonElement node)
        {
            if (GetString(node, "kind") is
                "CXXConstructorDecl" or "CXXDestructorDecl")
                return null;

            if (!node.TryGetProperty("type", out JsonElement type))
                return null;

            string functionType = GetString(type, "qualType");

            int separator = functionType.IndexOf(" (");

            return separator >= 0
                ? functionType[..separator]
                : functionType;
        }

        private static string GetDocumentation(JsonElement node)
        {
            if (!node.TryGetProperty("inner", out JsonElement children))
                return "";

            StringBuilder output = new();

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (GetString(child, "kind") == "FullComment")
                    CollectCommentText(child, output);
            }

            return output.ToString().Trim();
        }

        private static void CollectCommentText(
            JsonElement node, StringBuilder output)
        {
            string kind = GetString(node, "kind");

            if (kind == "TextComment")
            {
                output.Append(GetString(node, "text"));
                return;
            }

            if (kind == "ParagraphComment" && output.Length > 0)
                output.AppendLine();

            if (node.TryGetProperty("inner", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    CollectCommentText(child, output);
            }
        }

        private static bool HasFunctionBody(JsonElement node)
        {
            if (!node.TryGetProperty("inner", out JsonElement inner)
                || inner.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement child in inner.EnumerateArray())
            {
                string? kind = GetString(child, "kind");

                if (kind is "CompoundStmt"
                    or "CXXTryStmt"
                    or "CoroutineBodyStmt")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasInnerKind(JsonElement node, string targetKind)
        {
            if (!node.TryGetProperty("inner", out JsonElement inner)
                || inner.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement child in inner.EnumerateArray())
            {
                if (GetString(child, "kind") == targetKind)
                    return true;
            }

            return false;
        }

        private static bool IsDefinition(JsonElement node, string kind)
        {
            // Namespaces always represent definitions.
            if (kind == "NamespaceDecl")
                return true;

            // Enum members are actual declared members.
            if (kind == "EnumConstantDecl")
                return true;

            // A record is complete only when Clang marks it as such.
            if (kind is "CXXRecordDecl" or "RecordDecl")
            {
                return node.TryGetProperty("completeDefinition", out JsonElement complete)
                    && complete.ValueKind == JsonValueKind.True;
            }

            // An enum is defined when it contains its enumerators.
            if (kind == "EnumDecl")
            {
                return HasInnerKind(node, "EnumConstantDecl");
            }

            // Functions are definitions when they contain a body.
            if (kind is "FunctionDecl"
                or "CXXMethodDecl"
                or "CXXConstructorDecl"
                or "CXXDestructorDecl")
            {
                return HasFunctionBody(node);
            }

            // An extern variable without an initializer is only a declaration.
            if (kind == "VarDecl")
            {
                bool hasInitializer =
                    node.TryGetProperty("init", out _);

                bool isExtern =
                    GetString(node, "storageClass") == "extern";

                return hasInitializer || !isExtern;
            }

            // Fields belong to their containing record definition.
            if (kind == "FieldDecl")
                return true;

            // Use Clang's explicit answer when available.
            if (node.TryGetProperty(
                "isThisDeclarationADefinition",
                out JsonElement definition))
            {
                return definition.ValueKind == JsonValueKind.True;
            }

            return false;
        }

        private static bool IsImplicit(JsonElement node)
        {
            return node.TryGetProperty(
                "isImplicit", out JsonElement value)
                && value.ValueKind == JsonValueKind.True;
        }

        private static bool IsFromSourceFile(
            JsonElement node, string sourceFile)
        {
            if (!node.TryGetProperty("loc", out JsonElement location))
                return false;

            if (location.TryGetProperty(
                "includedFrom", out JsonElement includedFrom) &&
                includedFrom.ValueKind != JsonValueKind.Null)
                return false;

            if (location.TryGetProperty(
                "file", out JsonElement file))
            {
                string declaredFile = file.GetString() ?? "";

                if (string.IsNullOrEmpty(declaredFile))
                    return true;

                return string.Equals(
                    Path.GetFullPath(declaredFile),
                    sourceFile,
                    StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static int GetLocationNumber(
            JsonElement node, string property)
        {
            if (!node.TryGetProperty("loc", out JsonElement location))
                return 0;

            if (!location.TryGetProperty(
                property, out JsonElement value))
                return 0;

            return value.TryGetInt32(out int result) ? result : 0;
        }

        private static string GetString(JsonElement node, string property)
        {
            if (!node.TryGetProperty(
                property, out JsonElement value))
                return "";

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        private static void AddOrMerge(List<Symbol> symbols, Symbol incoming)
        {
            Symbol? existing = symbols.FirstOrDefault(s =>
                s.Name == incoming.Name &&
                s.Kind == incoming.Kind &&
                s.Line == incoming.Line);

            if (existing == null)
            {
                symbols.Add(incoming);
                return;
            }

            if (incoming.IsDefinition && !existing.IsDefinition)
            {
                existing.IsDefinition = true;
                existing.Children = incoming.Children;
                existing.Parameters = incoming.Parameters;
                existing.ReturnType = incoming.ReturnType;
            }

            if (string.IsNullOrEmpty(existing.Summary))
                existing.Summary = incoming.Summary;
        }
    }
}