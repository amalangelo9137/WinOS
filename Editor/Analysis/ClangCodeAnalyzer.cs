using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WinOS_Editor.Analysis
{
    /// <summary>
    /// Extracts the source-level symbol hierarchy from Clang's JSON AST.
    /// The analyzer deliberately uses Clang for parsing instead of attempting
    /// to understand C++ syntax with regular expressions.
    /// </summary>
    public sealed class ClangCodeAnalyzer : ICodeAnalyzer
    {
        private readonly string _clangPath;

        public ClangCodeAnalyzer(string llvmPath)
        {
            _clangPath = Path.Combine(llvmPath, "clang++.exe");

            if (!File.Exists(_clangPath))
            {
                throw new FileNotFoundException(
                    "clang++.exe was not found.", _clangPath);
            }
        }

        public async Task<List<Symbol>> AnalyzeFileAsync(string filePath)
        {
            filePath = Path.GetFullPath(filePath);

            string sourceText = await File.ReadAllTextAsync(filePath);
            string astJson = await GetAstJsonAsync(filePath);

            using JsonDocument document = JsonDocument.Parse(astJson);

            List<Symbol> symbols = [];

            if (document.RootElement.TryGetProperty(
                "inner", out JsonElement children) &&
                children.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in children.EnumerateArray())
                    ParseNode(child, filePath, sourceText, symbols);
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
            {
                throw new InvalidOperationException(
                    $"Clang analysis failed (exit code {process.ExitCode}):\n{errors}");
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException(
                    "Clang completed without returning an AST.");
            }

            return output;
        }

        /// <summary>
        /// Parses a declaration node. Transparent AST wrappers (such as
        /// linkage specifications and templates) are traversed without
        /// creating artificial hierarchy nodes.
        /// </summary>
        private static void ParseNode(
            JsonElement node,
            string sourceFile,
            string sourceText,
            List<Symbol> destination)
        {
            string clangKind = GetString(node, "kind");

            if (string.IsNullOrEmpty(clangKind) || IsImplicit(node))
                return;

            if (IsTransparentDeclaration(clangKind))
            {
                ParseInnerDeclarations(
                    node, sourceFile, sourceText, destination);
                return;
            }

            if (!IsFromSourceFile(node, sourceFile))
                return;

            SymbolKind? symbolKind = GetSymbolKind(clangKind, node);

            if (symbolKind == null)
                return;

            string name = GetString(node, "name");

            if (string.IsNullOrWhiteSpace(name))
            {
                if (clangKind == "NamespaceDecl")
                    name = "(anonymous namespace)";
                else if (clangKind == "EnumDecl")
                    name = "(anonymous enum)";
                else
                    return;
            }

            string? clangId = GetString(node, "id");
            if (string.IsNullOrEmpty(clangId))
                clangId = null;

            string? previousDeclId = GetString(node, "previousDecl");
            if (string.IsNullOrEmpty(previousDeclId))
                previousDeclId = null;

            Symbol symbol = new()
            {
                Name = name,
                Kind = symbolKind.Value,
                FilePath = sourceFile,
                Line = GetLocationNumber(node, "line"),
                Column = GetLocationNumber(node, "col"),
                Summary = GetDocumentation(node),
                IsDefinition = IsDefinition(node, clangKind),
                ClangId = clangId,
                PreviousDeclId = previousDeclId,
                MangledName = GetOptionalString(node, "mangledName"),
                Type = GetDeclaredType(node)
            };

            if (clangId != null)
                symbol.ClangIds.Add(clangId);

            if (IsCallable(clangKind))
            {
                symbol.ReturnType = GetReturnType(node);
                symbol.Parameters = GetParameters(node, sourceText);
                symbol.Signature = GetCallableSignature(node);
            }

            if (IsContainer(clangKind))
            {
                ParseInnerDeclarations(
                    node, sourceFile, sourceText, symbol.Children);
            }

            AddOrMerge(destination, symbol);
        }

        private static void ParseInnerDeclarations(
            JsonElement node,
            string sourceFile,
            string sourceText,
            List<Symbol> destination)
        {
            if (!node.TryGetProperty("inner", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement child in children.EnumerateArray())
                ParseNode(child, sourceFile, sourceText, destination);
        }

        private static bool IsTransparentDeclaration(string kind)
        {
            return kind is
                "LinkageSpecDecl" or
                "ExternCContextDecl" or
                "ExportDecl" or
                "ClassTemplateDecl" or
                "FunctionTemplateDecl" or
                "TypeAliasTemplateDecl" or
                "VarTemplateDecl";
        }

        private static SymbolKind? GetSymbolKind(
            string kind, JsonElement node)
        {
            return kind switch
            {
                "NamespaceDecl" => SymbolKind.Namespace,
                "CXXRecordDecl" or "RecordDecl" => GetRecordKind(node),
                "FunctionDecl" => SymbolKind.Function,
                "CXXMethodDecl" or "CXXConversionDecl" => SymbolKind.Method,
                "CXXConstructorDecl" => SymbolKind.Constructor,
                "CXXDestructorDecl" => SymbolKind.Destructor,
                "VarDecl" => SymbolKind.Variable,
                "FieldDecl" => SymbolKind.Field,
                "EnumDecl" => SymbolKind.Enum,
                "EnumConstantDecl" => SymbolKind.EnumMember,
                "TypedefDecl" => SymbolKind.Typedef,
                "TypeAliasDecl" => SymbolKind.TypeAlias,
                "UsingDecl" => SymbolKind.Using,
                _ => null
            };
        }

        private static SymbolKind GetRecordKind(JsonElement node)
        {
            return GetString(node, "tagUsed") switch
            {
                "struct" => SymbolKind.Struct,
                "union" => SymbolKind.Union,
                _ => SymbolKind.Class
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
                "CXXDestructorDecl" or
                "CXXConversionDecl";
        }

        private static string? GetDeclaredType(JsonElement node)
        {
            if (!node.TryGetProperty("type", out JsonElement type))
                return null;

            string value = GetString(type, "qualType");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string GetCallableSignature(JsonElement node)
        {
            // qualType contains the parameter types and, where applicable,
            // cv/ref qualifiers. It therefore distinguishes overloads.
            return GetDeclaredType(node) ?? "";
        }

        private static List<SymbolParameter> GetParameters(
            JsonElement node, string sourceText)
        {
            List<SymbolParameter> parameters = [];

            if (!node.TryGetProperty("inner", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                return parameters;
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (GetString(child, "kind") != "ParmVarDecl")
                    continue;

                parameters.Add(new SymbolParameter
                {
                    Name = GetString(child, "name"),
                    Type = GetString(child, "type", "qualType"),
                    DefaultValue = GetDefaultValue(child, sourceText)
                });
            }

            return parameters;
        }

        private static string? GetDefaultValue(
            JsonElement parameter, string sourceText)
        {
            if (!parameter.TryGetProperty("inner", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                string value = GetSourceText(child, sourceText);

                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        private static string GetSourceText(
            JsonElement node, string sourceText)
        {
            if (!node.TryGetProperty("range", out JsonElement range))
                return "";

            if (!range.TryGetProperty("begin", out JsonElement begin) ||
                !range.TryGetProperty("end", out JsonElement end))
            {
                return "";
            }

            int start = GetOffset(begin);
            int finish = GetOffset(end);
            int tokenLength = GetTokenLength(end);

            if (start < 0 || finish < start || start >= sourceText.Length)
                return "";

            int exclusiveEnd = Math.Min(
                sourceText.Length,
                finish + Math.Max(tokenLength, 1));

            if (exclusiveEnd <= start)
                return "";

            return sourceText.Substring(start, exclusiveEnd - start);
        }

        private static int GetOffset(JsonElement location)
        {
            JsonElement actual = GetPreferredLocation(location);

            return actual.TryGetProperty("offset", out JsonElement offset) &&
                   offset.TryGetInt32(out int result)
                ? result
                : -1;
        }

        private static int GetTokenLength(JsonElement location)
        {
            JsonElement actual = GetPreferredLocation(location);

            return actual.TryGetProperty("tokLen", out JsonElement length) &&
                   length.TryGetInt32(out int result)
                ? result
                : 1;
        }

        private static string? GetReturnType(JsonElement node)
        {
            string kind = GetString(node, "kind");

            if (kind is "CXXConstructorDecl" or "CXXDestructorDecl")
                return null;

            string functionType = GetDeclaredType(node) ?? "";
            int separator = functionType.IndexOf(" (", StringComparison.Ordinal);

            return separator >= 0
                ? functionType[..separator]
                : functionType;
        }

        private static string GetDocumentation(JsonElement node)
        {
            if (!node.TryGetProperty("inner", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

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

            if (kind is "ParagraphComment" or "BlockCommandComment")
            {
                if (output.Length > 0 &&
                    output[^1] != '\n' &&
                    output[^1] != ' ')
                {
                    output.AppendLine();
                }
            }

            if (!node.TryGetProperty("inner", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement child in children.EnumerateArray())
                CollectCommentText(child, output);
        }

        private static bool IsDefinition(JsonElement node, string kind)
        {
            if (kind is "NamespaceDecl" or "EnumConstantDecl")
                return true;

            if (kind is "CXXRecordDecl" or "RecordDecl")
            {
                return node.TryGetProperty(
                    "completeDefinition", out JsonElement complete) &&
                    complete.ValueKind == JsonValueKind.True;
            }

            if (kind == "EnumDecl")
            {
                return node.TryGetProperty(
                           "completeDefinition", out JsonElement complete) &&
                       complete.ValueKind == JsonValueKind.True ||
                       HasInnerKind(node, "EnumConstantDecl");
            }

            if (IsCallable(kind))
                return HasFunctionBody(node);

            if (kind == "VarDecl")
            {
                bool hasInitializer = node.TryGetProperty("init", out _);
                bool isExtern = GetString(node, "storageClass") == "extern";

                return hasInitializer || !isExtern;
            }

            if (kind == "FieldDecl")
                return true;

            if (kind is "TypedefDecl" or "TypeAliasDecl" or "UsingDecl")
                return true;

            if (node.TryGetProperty(
                "isThisDeclarationADefinition", out JsonElement definition))
            {
                return definition.ValueKind == JsonValueKind.True;
            }

            return false;
        }

        private static bool HasFunctionBody(JsonElement node)
        {
            if (!node.TryGetProperty("inner", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (GetString(child, "kind") is
                    "CompoundStmt" or
                    "CXXTryStmt" or
                    "CoroutineBodyStmt")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasInnerKind(JsonElement node, string targetKind)
        {
            if (!node.TryGetProperty("inner", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (GetString(child, "kind") == targetKind)
                    return true;
            }

            return false;
        }

        private static bool IsImplicit(JsonElement node)
        {
            return node.TryGetProperty(
                       "isImplicit", out JsonElement value) &&
                   value.ValueKind == JsonValueKind.True;
        }

        private static bool IsFromSourceFile(
            JsonElement node, string sourceFile)
        {
            if (!node.TryGetProperty("loc", out JsonElement location))
                return false;

            bool sawExplicitFile = false;
            bool sawIncludedFrom = false;

            CheckLocation(location, sourceFile,
                ref sawExplicitFile, ref sawIncludedFrom);

            if (sawExplicitFile)
                return LocationMatchesSource(location, sourceFile);

            // Clang omits repeated file names for locations in the same file.
            // Included declarations normally carry includedFrom metadata.
            if (sawIncludedFrom)
                return false;

            // No explicit file and no includedFrom: this is commonly a
            // subsequent declaration in the main file whose file was elided.
            return true;
        }

        private static void CheckLocation(
            JsonElement location,
            string sourceFile,
            ref bool sawExplicitFile,
            ref bool sawIncludedFrom)
        {
            if (location.ValueKind != JsonValueKind.Object)
                return;

            if (location.TryGetProperty("includedFrom", out JsonElement included) &&
                included.ValueKind == JsonValueKind.Object)
            {
                sawIncludedFrom = true;
            }

            if (location.TryGetProperty("file", out JsonElement file) &&
                file.ValueKind == JsonValueKind.String)
            {
                sawExplicitFile = true;
            }

            if (location.TryGetProperty("expansionLoc", out JsonElement expansion))
                CheckLocation(expansion, sourceFile,
                    ref sawExplicitFile, ref sawIncludedFrom);

            if (location.TryGetProperty("spellingLoc", out JsonElement spelling))
                CheckLocation(spelling, sourceFile,
                    ref sawExplicitFile, ref sawIncludedFrom);
        }

        private static bool LocationMatchesSource(
            JsonElement location, string sourceFile)
        {
            if (LocationMatchesSourceAtNode(location, sourceFile))
                return true;

            if (location.TryGetProperty(
                "expansionLoc", out JsonElement expansion) &&
                LocationMatchesSourceAtNode(expansion, sourceFile))
            {
                return true;
            }

            return location.TryGetProperty(
                       "spellingLoc", out JsonElement spelling) &&
                   LocationMatchesSourceAtNode(spelling, sourceFile);
        }

        private static bool LocationMatchesSourceAtNode(
            JsonElement location, string sourceFile)
        {
            if (!location.TryGetProperty("file", out JsonElement file) ||
                file.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string declaredFile = file.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(declaredFile))
                return false;

            string fullPath = Path.IsPathRooted(declaredFile)
                ? Path.GetFullPath(declaredFile)
                : Path.GetFullPath(
                    declaredFile,
                    Path.GetDirectoryName(sourceFile)!);

            return string.Equals(
                fullPath, sourceFile,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int GetLocationNumber(
            JsonElement node, string property)
        {
            if (!node.TryGetProperty("loc", out JsonElement location))
                return 0;

            JsonElement actual = GetPreferredLocation(location);

            return actual.TryGetProperty(property, out JsonElement value) &&
                   value.TryGetInt32(out int result)
                ? result
                : 0;
        }

        private static JsonElement GetPreferredLocation(JsonElement location)
        {
            if (location.TryGetProperty(
                "expansionLoc", out JsonElement expansion))
            {
                return expansion;
            }

            if (location.TryGetProperty(
                "spellingLoc", out JsonElement spelling))
            {
                return spelling;
            }

            return location;
        }

        private static string GetString(JsonElement node, string property)
        {
            if (node.ValueKind != JsonValueKind.Object ||
                !node.TryGetProperty(property, out JsonElement value))
            {
                return "";
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        private static string GetString(
            JsonElement node, string parent, string property)
        {
            if (node.ValueKind != JsonValueKind.Object ||
                !node.TryGetProperty(parent, out JsonElement nested) ||
                nested.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            return GetString(nested, property);
        }

        private static string? GetOptionalString(
            JsonElement node, string property)
        {
            string value = GetString(node, property);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Merges redeclarations without merging overloads. Clang's
        /// previousDecl chain is authoritative; the signature fallback is
        /// restricted to this parent's child list, which represents one scope.
        /// </summary>
        private static void AddOrMerge(
            List<Symbol> symbols, Symbol incoming)
        {
            Symbol? existing = null;

            if (!string.IsNullOrEmpty(incoming.ClangId))
            {
                existing = symbols.FirstOrDefault(s =>
                    s.ClangIds.Contains(incoming.ClangId) ||
                    s.ClangId == incoming.ClangId);
            }

            if (existing == null &&
                !string.IsNullOrEmpty(incoming.PreviousDeclId))
            {
                existing = symbols.FirstOrDefault(s =>
                    s.ClangIds.Contains(incoming.PreviousDeclId) ||
                    s.ClangId == incoming.PreviousDeclId);
            }

            if (existing == null)
            {
                existing = symbols.FirstOrDefault(s =>
                    HasSameFallbackIdentity(s, incoming));
            }

            if (existing == null)
            {
                symbols.Add(incoming);
                return;
            }

            RememberClangIdentity(existing, incoming);

            if (incoming.IsDefinition && !existing.IsDefinition)
            {
                existing.IsDefinition = true;
                existing.FilePath = incoming.FilePath;
                existing.Line = incoming.Line;
                existing.Column = incoming.Column;
            }

            if (string.IsNullOrEmpty(existing.Summary))
                existing.Summary = incoming.Summary;

            if (string.IsNullOrEmpty(existing.Type))
                existing.Type = incoming.Type;

            if (incoming.IsDefinition || existing.Parameters.Count == 0)
            {
                if (incoming.Parameters.Count > 0)
                    existing.Parameters = incoming.Parameters;

                if (!string.IsNullOrEmpty(incoming.ReturnType))
                    existing.ReturnType = incoming.ReturnType;
            }

            if (string.IsNullOrEmpty(existing.MangledName))
                existing.MangledName = incoming.MangledName;

            if (string.IsNullOrEmpty(existing.Signature))
                existing.Signature = incoming.Signature;

            foreach (Symbol child in incoming.Children)
                AddOrMerge(existing.Children, child);
        }

        private static bool HasSameFallbackIdentity(
            Symbol existing, Symbol incoming)
        {
            if (existing.Name != incoming.Name ||
                existing.Kind != incoming.Kind)
            {
                return false;
            }

            if (IsCallableKind(incoming.Kind))
            {
                if (!string.IsNullOrEmpty(existing.MangledName) &&
                    !string.IsNullOrEmpty(incoming.MangledName))
                {
                    return existing.MangledName == incoming.MangledName;
                }

                return existing.Signature == incoming.Signature;
            }

            // AddOrMerge is called only among siblings, so the containing
            // namespace/class scope is already identical.
            return true;
        }

        private static bool IsCallableKind(SymbolKind kind)
        {
            return kind is
                SymbolKind.Function or
                SymbolKind.Method or
                SymbolKind.Constructor or
                SymbolKind.Destructor;
        }

        private static void RememberClangIdentity(
            Symbol existing, Symbol incoming)
        {
            if (!string.IsNullOrEmpty(incoming.ClangId))
            {
                if (string.IsNullOrEmpty(existing.ClangId))
                    existing.ClangId = incoming.ClangId;

                if (!existing.ClangIds.Contains(incoming.ClangId))
                    existing.ClangIds.Add(incoming.ClangId);
            }

            if (!string.IsNullOrEmpty(incoming.PreviousDeclId) &&
                !existing.ClangIds.Contains(incoming.PreviousDeclId))
            {
                existing.ClangIds.Add(incoming.PreviousDeclId);
            }

            foreach (string id in incoming.ClangIds)
            {
                if (!existing.ClangIds.Contains(id))
                    existing.ClangIds.Add(id);
            }
        }
    }
}
