using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol;
using SharpTools.Tools.Services;
using System.Reflection;
using System.Text.Json;
using System.ComponentModel;
using SharpTools.Tools.Mcp.Models;
using SharpTools.Tools.Mcp.Helpers;

namespace SharpTools.Tools.Mcp.Tools;

// Marker class for ILogger<T> category specific to AnalysisTools
public class AnalysisToolsLogCategory { }

[McpServerToolType]
public static partial class AnalysisTools {
    internal static async Task<object> BuildRoslynSubtypeTreeAsync(INamedTypeSymbol typeSymbol, ICodeAnalysisService codeAnalysisService, CancellationToken cancellationToken) {
        var membersByKind = new Dictionary<string, List<object>>();

        foreach (var member in typeSymbol.GetMembers()) {
            cancellationToken.ThrowIfCancellationRequested();

            if (member.IsImplicitlyDeclared || member.Kind == SymbolKind.ErrorType || ToolHelpers.IsPropertyAccessor(member)) {
                continue;
            }

            var locationInfo = GetDeclarationLocationInfo(member);
            var memberLocation = locationInfo.FirstOrDefault();
            var kind = ToolHelpers.GetSymbolKindString(member);

            // Create an entry for this kind if it doesn't exist
            if (!membersByKind.ContainsKey(kind)) {
                membersByKind[kind] = new List<object>();
            }

            // Build signature without duplicates
            var memberModifiers = ToolHelpers.GetRoslynSymbolModifiersString(member);
            var memberBaseSignature = CodeAnalysisService.GetFormattedSignatureAsync(member, false);

            // Combine modifiers and signature, avoiding duplicates
            string memberSignature;
            if (!string.IsNullOrWhiteSpace(memberModifiers)) {
                if (memberBaseSignature.StartsWith(memberModifiers)) {
                    memberSignature = memberBaseSignature;
                } else {
                    memberSignature = $"{memberModifiers} {memberBaseSignature}".Trim();
                }
            } else {
                memberSignature = memberBaseSignature;
            }

            // Fix duplicate return type issue for methods
            if (member is IMethodSymbol memberMethod) {
                if (memberMethod.ReturnsVoid) {
                    memberSignature = System.Text.RegularExpressions.Regex.Replace(memberSignature, @"\bvoid\s+void\b", "void");
                } else {
                    var memberReturnType = memberMethod.ReturnType.ToDisplayString();
                    var memberDuplicatePattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(memberReturnType)}\s+{System.Text.RegularExpressions.Regex.Escape(memberReturnType)}\b";
                    memberSignature = System.Text.RegularExpressions.Regex.Replace(memberSignature, memberDuplicatePattern, memberReturnType);
                }
            }

            var memberInfo = new {
                signature = memberSignature,
                fullyQualifiedName = FuzzyFqnLookupService.GetSearchableString(member),
                line = memberLocation?.StartLine,
                members = member is INamedTypeSymbol nestedType ?
                    await BuildRoslynSubtypeTreeAsync(nestedType, codeAnalysisService, cancellationToken) : null
            };

            membersByKind[kind].Add(memberInfo);
        }

        // Sort members within each kind by signature
        foreach (var kind in membersByKind.Keys.ToList()) {
            membersByKind[kind] = membersByKind[kind]
                .OrderBy(m => ((dynamic)m).line)
                .OrderBy(m => ((dynamic)m).signature)
                .ToList();
        }

        // Sort kinds in a logical order: Nested Types, Fields, Properties, Events, Methods
        var orderedKinds = membersByKind.Keys
            .OrderBy(k => k switch {
                "Class" => 1,
                "Interface" => 1,
                "Struct" => 1,
                "Enum" => 1,
                "Field" => 2,
                "Property" => 3,
                "Event" => 4,
                "Method" => 5,
                _ => 99
            })
            .ThenBy(k => k)
            .ToDictionary(k => k, k => membersByKind[k]);

        var locations = GetDeclarationLocationInfo(typeSymbol);
        // For partial classes, we may have multiple locations
        object? location = locations.Count > 1 ? locations : locations.FirstOrDefault();
        return new {
            kind = ToolHelpers.GetSymbolKindString(typeSymbol),
            signature = ToolHelpers.GetRoslynTypeSpecificModifiersString(typeSymbol) + " " +
                typeSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
            fullyQualifiedName = FuzzyFqnLookupService.GetSearchableString(typeSymbol),
            location = location,
            membersByKind = orderedKinds
        };
    }
    private static object BuildReflectionSubtypeTree(Type type, CancellationToken cancellationToken) {
        var members = new List<object>();

        foreach (var memberInfo in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static |
                                                 BindingFlags.DeclaredOnly)) {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip property/event accessors to reduce noise
            if (memberInfo is MethodInfo mi && mi.IsSpecialName &&
                (mi.Name.StartsWith("get_") || mi.Name.StartsWith("set_") ||
                 mi.Name.StartsWith("add_") || mi.Name.StartsWith("remove_"))) {
                continue;
            }

            var memberItem = new {
                kind = ToolHelpers.GetReflectionMemberTypeKindString(memberInfo),
                signature = ToolHelpers.GetReflectionMemberModifiersString(memberInfo) + " " + memberInfo.ToString(),
                members = memberInfo is Type nestedType ? BuildReflectionSubtypeTree(nestedType, cancellationToken) : null
            };

            members.Add(memberItem);
        }

        members = members.OrderBy(m => ((dynamic)m).kind)
                        .ThenBy(m => ((dynamic)m).signature)
                        .GroupBy(m => ((dynamic)m).kind)
                        .Select(g => (object)new {
                            kind = g.Key,
                            members = g.Select(m => new {
                                ((dynamic)m).signature,
                                ((dynamic)m).members
                            }).ToList()
                        })
                        .ToList();

        return new {
            kind = ToolHelpers.GetReflectionTypeKindString(type),
            signature = ToolHelpers.GetReflectionTypeModifiersString(type) + " " + type.FullName,
            members
        };
    }

    private static object GetReflectionTypeMembersAsync(
        Type reflectionType,
        bool includePrivateMembers,
        ILogger<AnalysisToolsLogCategory> logger,
        CancellationToken cancellationToken) {

        var apiMembers = new List<object>();
        try {
            foreach (var memberInfo in reflectionType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly)) {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip property and event accessors which are exposed as separate methods
                if (memberInfo is MethodInfo mi && mi.IsSpecialName &&
                    (mi.Name.StartsWith("get_") || mi.Name.StartsWith("set_") ||
                    mi.Name.StartsWith("add_") || mi.Name.StartsWith("remove_"))) {
                    continue;
                }

                // Determine if the member should be included based on its accessibility
                bool shouldInclude;
                string accessibilityString = "";

                try {
                    switch (memberInfo) {
                        case FieldInfo fi:
                            shouldInclude = includePrivateMembers || fi.IsPublic || fi.IsAssembly || fi.IsFamily || fi.IsFamilyOrAssembly;
                            accessibilityString = ToolHelpers.GetReflectionMemberModifiersString(fi);
                            break;
                        case MethodBase mb:
                            shouldInclude = includePrivateMembers || mb.IsPublic || mb.IsAssembly || mb.IsFamily || mb.IsFamilyOrAssembly;
                            accessibilityString = ToolHelpers.GetReflectionMemberModifiersString(mb);
                            break;
                        case PropertyInfo pi:
                            var getter = pi.GetGetMethod(true);
                            shouldInclude = includePrivateMembers;  // Default for properties with no accessor
                            if (getter != null) {
                                shouldInclude = includePrivateMembers || getter.IsPublic || getter.IsAssembly || getter.IsFamily || getter.IsFamilyOrAssembly;
                            }
                            accessibilityString = ToolHelpers.GetReflectionMemberModifiersString(pi);
                            break;
                        case EventInfo ei:
                            var adder = ei.GetAddMethod(true);
                            shouldInclude = includePrivateMembers;  // Default for events with no accessor
                            if (adder != null) {
                                shouldInclude = includePrivateMembers || adder.IsPublic || adder.IsAssembly || adder.IsFamily || adder.IsFamilyOrAssembly;
                            }
                            accessibilityString = ToolHelpers.GetReflectionMemberModifiersString(ei);
                            break;
                        case Type nt: // Nested Type
                            shouldInclude = includePrivateMembers || nt.IsPublic || nt.IsNestedPublic || nt.IsNestedAssembly ||
                                nt.IsNestedFamily || nt.IsNestedFamORAssem;
                            accessibilityString = ToolHelpers.GetReflectionMemberModifiersString(nt);
                            break;
                        default:
                            shouldInclude = includePrivateMembers;
                            break;
                    }

                    if (shouldInclude) {
                        apiMembers.Add(new {
                            name = memberInfo.Name,
                            kind = ToolHelpers.GetReflectionMemberTypeKindString(memberInfo),
                            modifiers = accessibilityString,
                            signature = memberInfo.ToString(),
                        });
                    }
                } catch (Exception ex) {
                    logger.LogWarning(ex, "Error processing reflection member {MemberName}", memberInfo.Name);

                    // Add with partial information
                    if (includePrivateMembers) {
                        apiMembers.Add(new {
                            name = memberInfo.Name,
                            kind = "Unknown",
                            modifiers = "Error",
                            signature = $"{memberInfo.Name} (Error: {ex.Message})",
                        });
                    }
                }
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Error retrieving members for reflection type {TypeName}", reflectionType.FullName);
            throw new McpException($"Failed to retrieve members for type '{reflectionType.FullName}': {ex.Message}");
        }

        return ToolHelpers.ToJson(new {
            typeName = reflectionType.FullName,
            source = "Reflection",
            includesPrivateMembers = includePrivateMembers,
            members = apiMembers.OrderBy(m => ((dynamic)m).kind).ThenBy(m => ((dynamic)m).name).ToList()
        });
    }
    private static string TrimLeadingWhitespace(string line) {
        int index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }
        return index < line.Length ? line.Substring(index) : string.Empty;
    }
    private static async Task<string> HandlePartialTypeDefinitionAsync(
        ISymbol roslynSymbol,
        Solution? solution,
        ICodeAnalysisService codeAnalysisService,
        ILogger<AnalysisToolsLogCategory> logger,
        CancellationToken cancellationToken) {

        var namedTypeSymbol = (Microsoft.CodeAnalysis.INamedTypeSymbol)roslynSymbol;
        var partialDeclarations = new List<object>();
        var allSourceCode = new List<string>();
        var allFiles = new List<string>();

        // Generate reference context for the type
        string referenceContext = await ContextInjectors.CreateTypeReferenceContextAsync(codeAnalysisService, logger, namedTypeSymbol, cancellationToken);

        foreach (var syntaxRef in roslynSymbol.DeclaringSyntaxReferences) {
            if (syntaxRef.SyntaxTree?.FilePath == null) {
                continue;
            }

            var document = solution?.GetDocument(syntaxRef.SyntaxTree);
            if (document == null) {
                logger.LogWarning("Could not find document for partial declaration in file: {FilePath}", syntaxRef.SyntaxTree.FilePath);
                continue;
            }

            var node = await syntaxRef.GetSyntaxAsync(cancellationToken);

            // Find the appropriate parent node that represents the full definition
            SyntaxNode? definitionNode = node;
            while (definitionNode != null &&
                   !(definitionNode is MemberDeclarationSyntax) &&
                   !(definitionNode is TypeDeclarationSyntax)) {
                definitionNode = definitionNode.Parent;
            }

            if (definitionNode == null) {
                logger.LogWarning("Could not find definition syntax for partial declaration in file: {FilePath}", syntaxRef.SyntaxTree.FilePath);
                continue;
            }

            var result = definitionNode.ToString();
            var filePath = document.FilePath ?? "unknown location";
            var lineInfo = definitionNode.GetLocation().GetLineSpan();

            // Remove leading whitespace from each line
            var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++) {
                lines[i] = TrimLeadingWhitespace(lines[i]);
            }
            result = string.Join(Environment.NewLine, lines);

            allFiles.Add($"{filePath} (Lines {lineInfo.StartLinePosition.Line + 1} - {lineInfo.EndLinePosition.Line + 1})");
            allSourceCode.Add($"<code partialDefinitionFile='{filePath}' lines='{lineInfo.StartLinePosition.Line + 1} - {lineInfo.EndLinePosition.Line + 1}'>\n{result}\n</code>");
        }

        var combinedSource = string.Join("\n\n", allSourceCode);
        var fileList = string.Join(", ", allFiles);

        return $"<definition>\n{referenceContext}\n// Partial type found in {allFiles.Count} files: {fileList}\n\n{combinedSource}\n</definition>";
    }
    private static async Task<string> HandleSingleDefinitionAsync(
        ISymbol roslynSymbol,
        Solution? solution,
        Location sourceLocation,
        ICodeAnalysisService codeAnalysisService,
        ILogger<AnalysisToolsLogCategory> logger,
        CancellationToken cancellationToken) {

        if (sourceLocation.SourceTree == null) {
            throw new McpException($"Source tree not available for symbol '{roslynSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal)}'.");
        }

        var document = solution?.GetDocument(sourceLocation.SourceTree);
        if (document == null) {
            throw new McpException($"Could not find document for symbol '{roslynSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal)}'.");
        }

        var symbolSyntax = await sourceLocation.SourceTree.GetRootAsync(cancellationToken);
        var node = symbolSyntax.FindNode(sourceLocation.SourceSpan);

        if (node == null) {
            throw new McpException($"Could not find syntax node for symbol '{roslynSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal)}'.");
        }

        // Find the appropriate parent node that represents the full definition
        SyntaxNode? definitionNode = node;
        if (roslynSymbol is Microsoft.CodeAnalysis.IMethodSymbol || roslynSymbol is Microsoft.CodeAnalysis.IPropertySymbol ||
            roslynSymbol is Microsoft.CodeAnalysis.IFieldSymbol || roslynSymbol is Microsoft.CodeAnalysis.IEventSymbol ||
            roslynSymbol is Microsoft.CodeAnalysis.INamedTypeSymbol) {
            while (definitionNode != null &&
                   !(definitionNode is MemberDeclarationSyntax) &&
                   !(definitionNode is TypeDeclarationSyntax)) {
                definitionNode = definitionNode.Parent;
            }
        }

        if (definitionNode == null) {
            throw new McpException($"Could not find definition syntax for symbol '{roslynSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal)}'.");
        }

        var result = definitionNode.ToString();
        var filePath = document.FilePath ?? "unknown location";
        var lineInfo = definitionNode.GetLocation().GetLineSpan();

        // Generate reference context based on symbol type
        string referenceContext = roslynSymbol switch {
            Microsoft.CodeAnalysis.INamedTypeSymbol type => await ContextInjectors.CreateTypeReferenceContextAsync(codeAnalysisService, logger, type, cancellationToken),
            Microsoft.CodeAnalysis.IMethodSymbol method => await ContextInjectors.CreateCallGraphContextAsync(codeAnalysisService, logger, method, cancellationToken),
            _ => string.Empty // TODO: consider adding context for properties, fields, events, etc.
        };

        // Remove leading whitespace from each line
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++) {
            lines[i] = TrimLeadingWhitespace(lines[i]);
        }
        result = string.Join(Environment.NewLine, lines);

        return $"<definition>\n{referenceContext}\n<code file='{filePath}' lines='{lineInfo.StartLinePosition.Line + 1} - {lineInfo.EndLinePosition.Line + 1}'>\n{result}\n</code>\n</definition>";
    }
    public class LocationInfo {
        public string FilePath { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    public class MethodSignatureResult {
        public string SearchTerm { get; set; } = string.Empty;
        public int TotalMatches { get; set; }
        public List<MethodDetail> Methods { get; set; } = new();
        public string? Note { get; set; }
        
        public override string ToString() {
            return ToolHelpers.ToJson(this);
        }
    }

    public class MethodDetail {
        public string Name { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string FullyQualifiedName { get; set; } = string.Empty;
        public string ContainingType { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public List<ParameterDetail> Parameters { get; set; } = new();
        public LocationInfo? Location { get; set; }
        public string? Documentation { get; set; }
        public XmlDocumentation? StructuredDocumentation { get; set; }
        public List<string> Modifiers { get; set; } = new();
        public double MatchScore { get; set; }
        public string MatchReason { get; set; } = string.Empty;
        public bool IsOverloaded { get; set; }
        public string? GenericConstraints { get; set; }
    }

    public class ParameterDetail {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsOptional { get; set; }
        public string? DefaultValue { get; set; }
        public string? Documentation { get; set; }
        public List<string> Modifiers { get; set; } = new(); // ref, out, in, params
    }
    private static List<LocationInfo> GetDeclarationLocationInfo(ISymbol symbol) {
        var locations = new List<LocationInfo>();

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences) {
            if (syntaxRef.SyntaxTree?.FilePath == null) {
                continue;
            }

            var node = syntaxRef.GetSyntax();
            var fullSpan = node switch {
                TypeDeclarationSyntax typeNode => typeNode.GetLocation().GetLineSpan(),
                MethodDeclarationSyntax methodNode => methodNode.GetLocation().GetLineSpan(),
                PropertyDeclarationSyntax propertyNode => propertyNode.GetLocation().GetLineSpan(),
                EventDeclarationSyntax eventNode => eventNode.GetLocation().GetLineSpan(),
                LocalFunctionStatementSyntax localFuncNode => localFuncNode.GetLocation().GetLineSpan(),
                ConstructorDeclarationSyntax ctorNode => ctorNode.GetLocation().GetLineSpan(),
                DestructorDeclarationSyntax dtorNode => dtorNode.GetLocation().GetLineSpan(),
                OperatorDeclarationSyntax opNode => opNode.GetLocation().GetLineSpan(),
                ConversionOperatorDeclarationSyntax convNode => convNode.GetLocation().GetLineSpan(),
                FieldDeclarationSyntax fieldNode => fieldNode.GetLocation().GetLineSpan(),
                DelegateDeclarationSyntax delegateNode => delegateNode.GetLocation().GetLineSpan(),
                // For any other symbol, use its direct location span
                _ => node.GetLocation().GetLineSpan()
            };

            locations.Add(new LocationInfo {
                FilePath = syntaxRef.SyntaxTree.FilePath,
                StartLine = fullSpan.StartLinePosition.Line + 1,
                EndLine = fullSpan.EndLinePosition.Line + 1
            });
        }

        // If we couldn't get any locations from DeclaringSyntaxReferences (rare), fall back to Locations
        if (!locations.Any()) {
            foreach (var location in symbol.Locations.Where(l => l.IsInSource)) {
                if (location.SourceTree?.FilePath == null) {
                    continue;
                }

                var lineSpan = location.GetLineSpan();
                locations.Add(new LocationInfo {
                    FilePath = location.SourceTree.FilePath,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1
                });
            }
        }

        return locations;
    }

    #region Main Methods

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(GetMembers), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("🔍 .NET専用 - .cs/.sln/.csprojファイルのみ対応。クラスやインターフェースのメンバー（メソッド、プロパティ、フィールド）を表示")]
    public static async Task<object> GetMembers(
    StatelessWorkspaceFactory workspaceFactory,
    ICodeAnalysisService codeAnalysisService,
    IFuzzyFqnLookupService fuzzyFqnLookupService,
    ILogger<AnalysisToolsLogCategory> logger,
    [Description(".NETソリューション(.sln)またはプロジェクト(.csproj)ファイルのパス")] string solutionOrProjectPath,
    [Description("Class name to analyze (e.g., 'UserService' or 'MyApp.Services.UserService')")] string classNameOrFqn,
    [Description("Include private members in results (true=show all, false=public only)")] bool includePrivateMembers,
    CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(solutionOrProjectPath, nameof(GetMembers), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(solutionOrProjectPath, nameof(solutionOrProjectPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(classNameOrFqn, nameof(classNameOrFqn), logger);

            logger.LogInformation("Executing '{GetMembers}' for: {TypeName} in context {ProjectOrFilePath} (IncludePrivate: {IncludePrivate})",
                nameof(GetMembers), classNameOrFqn, solutionOrProjectPath, includePrivateMembers);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(solutionOrProjectPath);

            try {
                Solution solution;
                if (context is Solution sol) {
                    solution = sol;
                } else if (context is Project proj) {
                    solution = proj.Solution;
                } else {
                    var dynamicContext = (dynamic)context;
                    solution = ((Project)dynamicContext.Project).Solution;
                }

                // Use fuzzy lookup to find the symbol
                var fuzzyMatches = await fuzzyFqnLookupService.FindMatchesAsync(classNameOrFqn, new StatelessSolutionManager(solution), cancellationToken);
                var bestMatch = fuzzyMatches.FirstOrDefault();
                if (bestMatch == null || !(bestMatch.Symbol is INamedTypeSymbol namedTypeSymbol)) {
                    throw new McpException($"型が見つかりません: '{classNameOrFqn}'\n確認方法:\n• {ToolHelpers.SharpToolPrefix}{nameof(Tools.DocumentTools.ReadTypesFromRoslynDocument)} で利用可能な型を確認\n• 完全修飾名（例: MyApp.Models.User）または短縮名（例: User）で試してください\n• 名前空間が正しいかを確認");
                }

                string typeName = ToolHelpers.RemoveGlobalPrefix(namedTypeSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal));
                var membersByLocation = new Dictionary<string, Dictionary<string, List<string>>>();
                var defaultLocation = "Unknown Location";

                foreach (var member in namedTypeSymbol.GetMembers()) {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (member.IsImplicitlyDeclared || ToolHelpers.IsPropertyAccessor(member)) {
                        continue;
                    }

                    bool shouldInclude = includePrivateMembers ||
                        member.DeclaredAccessibility == Accessibility.Public ||
                        member.DeclaredAccessibility == Accessibility.Internal ||
                        member.DeclaredAccessibility == Accessibility.Protected ||
                        member.DeclaredAccessibility == Accessibility.ProtectedAndInternal ||
                        member.DeclaredAccessibility == Accessibility.ProtectedOrInternal;

                    if (shouldInclude) {
                        try {
                            var locationInfo = GetDeclarationLocationInfo(member);
                            var location = locationInfo.FirstOrDefault();
                            var locationKey = location != null
                                ? location.FilePath
                                : defaultLocation;

                            var kind = ToolHelpers.GetSymbolKindString(member);
                            string xmlDocs = await codeAnalysisService.GetXmlDocumentationAsync(member, cancellationToken) ?? string.Empty;

                            // Build signature without duplicates
                            var modifiers = ToolHelpers.GetRoslynSymbolModifiersString(member);
                            var baseSignature = CodeAnalysisService.GetFormattedSignatureAsync(member, false).Replace(typeName + ".", string.Empty).Trim();

                            // Combine modifiers and signature, avoiding duplicates
                            string signature;
                            if (!string.IsNullOrWhiteSpace(modifiers)) {
                                if (baseSignature.StartsWith(modifiers)) {
                                    signature = baseSignature;
                                } else {
                                    signature = $"{modifiers} {baseSignature}".Trim();
                                }
                            } else {
                                signature = baseSignature;
                            }

                            // Fix duplicate return type issue for methods
                            if (member is IMethodSymbol methodSymbol) {
                                if (methodSymbol.ReturnsVoid) {
                                    signature = System.Text.RegularExpressions.Regex.Replace(signature, @"\bvoid\s+void\b", "void");
                                } else {
                                    var returnType = methodSymbol.ReturnType.ToDisplayString();
                                    var duplicatePattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(returnType)}\s+{System.Text.RegularExpressions.Regex.Escape(returnType)}\b";
                                    signature = System.Text.RegularExpressions.Regex.Replace(signature, duplicatePattern, returnType);
                                }
                            }

                            // Add XML documentation comment if available
                            if (!string.IsNullOrEmpty(xmlDocs)) {
                                signature += $" // {xmlDocs.Replace("\n", " ").Replace("\r", "")}";
                            }

                            if (!membersByLocation.ContainsKey(locationKey)) {
                                membersByLocation[locationKey] = new Dictionary<string, List<string>>();
                            }

                            if (!membersByLocation[locationKey].ContainsKey(kind)) {
                                membersByLocation[locationKey][kind] = new List<string>();
                            }

                            membersByLocation[locationKey][kind].Add(signature);
                        } catch (Exception ex) {
                            logger.LogWarning(ex, "Error processing member {MemberName} in {TypeName}", member.Name, classNameOrFqn);
                        }
                    }
                }

                // Build structured result
                var result = new GetMembersResult {
                    ClassName = namedTypeSymbol.Name,
                    FullyQualifiedName = namedTypeSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
                    IsPartial = namedTypeSymbol.Locations.Length > 1
                };

                // Add file path from first location
                var typeLocations = GetDeclarationLocationInfo(namedTypeSymbol);
                if (typeLocations.Any()) {
                    result.FilePath = typeLocations.First().FilePath;
                }

                // Add base types and interfaces
                if (namedTypeSymbol.BaseType != null && namedTypeSymbol.BaseType.SpecialType != SpecialType.System_Object) {
                    result.BaseTypes.Add(namedTypeSymbol.BaseType.ToDisplayString());
                }
                result.Interfaces.AddRange(namedTypeSymbol.Interfaces.Select(i => i.ToDisplayString()));

                // Build member list with detailed info
                var memberList = new List<MemberDetail>();
                foreach (var member in namedTypeSymbol.GetMembers()) {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (member.IsImplicitlyDeclared || ToolHelpers.IsPropertyAccessor(member)) {
                        continue;
                    }

                    bool shouldInclude = includePrivateMembers ||
                        member.DeclaredAccessibility == Accessibility.Public ||
                        member.DeclaredAccessibility == Accessibility.Internal ||
                        member.DeclaredAccessibility == Accessibility.Protected ||
                        member.DeclaredAccessibility == Accessibility.ProtectedAndInternal ||
                        member.DeclaredAccessibility == Accessibility.ProtectedOrInternal;

                    if (shouldInclude) {
                        var memberInfo = new MemberDetail {
                            Name = member.Name,
                            Type = ToolHelpers.GetSymbolKindString(member),
                            FullyQualifiedName = FuzzyFqnLookupService.GetSearchableString(member),
                            Accessibility = member.DeclaredAccessibility.ToString()
                        };

                        // Get modifiers
                        var modifiers = ToolHelpers.GetRoslynSymbolModifiersString(member);
                        if (!string.IsNullOrWhiteSpace(modifiers)) {
                            memberInfo.Modifiers.AddRange(modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                        }

                        // Build signature
                        var baseSignature = CodeAnalysisService.GetFormattedSignatureAsync(member, false);
                        memberInfo.Signature = !string.IsNullOrWhiteSpace(modifiers) && !baseSignature.StartsWith(modifiers)
                            ? $"{modifiers} {baseSignature}".Trim()
                            : baseSignature;

                        // Method-specific info
                        if (member is IMethodSymbol methodSymbol) {
                            memberInfo.ReturnType = methodSymbol.ReturnType.ToDisplayString();
                            memberInfo.Parameters = methodSymbol.Parameters.Select(p => new Models.ParameterInfo {
                                Name = p.Name,
                                Type = p.Type.ToDisplayString(),
                                DefaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null,
                                Modifiers = p.RefKind switch {
                                    RefKind.Ref => new List<string> { "ref" },
                                    RefKind.Out => new List<string> { "out" },
                                    RefKind.In => new List<string> { "in" },
                                    _ => new List<string>()
                                }
                            }).ToList();
                        }
                        // Property-specific info
                        else if (member is IPropertySymbol propertySymbol) {
                            memberInfo.ReturnType = propertySymbol.Type.ToDisplayString();
                        }
                        // Field-specific info
                        else if (member is IFieldSymbol fieldSymbol) {
                            memberInfo.ReturnType = fieldSymbol.Type.ToDisplayString();
                        }

                        // Location info
                        var locationInfo = GetDeclarationLocationInfo(member);
                        if (locationInfo.Any()) {
                            var loc = locationInfo.First();
                            memberInfo.Location = new MemberLocation {
                                FilePath = loc.FilePath,
                                Line = loc.StartLine,
                                Column = 1 // Column information not available in LocationInfo
                            };
                        }

                        // XML documentation
                        memberInfo.XmlDocs = await codeAnalysisService.GetXmlDocumentationAsync(member, cancellationToken);

                        // Attributes
                        memberInfo.Attributes.AddRange(member.GetAttributes().Select(a => a.AttributeClass?.ToDisplayString() ?? ""));

                        memberList.Add(memberInfo);
                    }
                }

                result.Members = memberList;

                // Add nested types
                result.NestedTypes.AddRange(
                    namedTypeSymbol.GetTypeMembers()
                        .Select(t => t.ToDisplayString())
                );

                // Calculate statistics
                result.Statistics = new ClassStatistics {
                    TotalMembers = memberList.Count,
                    PublicMembers = memberList.Count(m => m.Accessibility == "Public"),
                    PrivateMembers = memberList.Count(m => m.Accessibility == "Private"),
                    ProtectedMembers = memberList.Count(m => m.Accessibility == "Protected"),
                    InternalMembers = memberList.Count(m => m.Accessibility == "Internal"),
                    MethodCount = memberList.Count(m => m.Type == "Method"),
                    PropertyCount = memberList.Count(m => m.Type == "Property"),
                    FieldCount = memberList.Count(m => m.Type == "Field"),
                    EventCount = memberList.Count(m => m.Type == "Event"),
                    NestedTypeCount = result.NestedTypes.Count
                };

                return result;
            } finally {
                workspace?.Dispose();
            }
        }, logger, nameof(GetMembers), cancellationToken);
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(GetMethodSignature), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("🔍 .NET専用 - .cs/.sln/.csprojファイルのみ対応。メソッドシグネチャを確認（本体なし）")]
    public static async Task<object> GetMethodSignature(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeAnalysisService codeAnalysisService,
        IFuzzyFqnLookupService fuzzyFqnLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        [Description(".NETソリューション(.sln)またはプロジェクト(.csproj)ファイルのパス")] string solutionOrProjectPath,
        [Description("Method name (e.g., 'ProcessData', 'MyClass.ProcessData', or full FQN)")] string methodName,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(solutionOrProjectPath, nameof(GetMethodSignature), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(solutionOrProjectPath, nameof(solutionOrProjectPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(methodName, nameof(methodName), logger);

            logger.LogInformation("Executing '{GetMethodSignature}' for: {MethodName} in context {ProjectOrFilePath}",
                nameof(GetMethodSignature), methodName, solutionOrProjectPath);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(solutionOrProjectPath);

            try {
                Solution solution;
                if (context is Solution sol) {
                    solution = sol;
                } else if (context is Project proj) {
                    solution = proj.Solution;
                } else {
                    var dynamicContext = (dynamic)context;
                    solution = ((Project)dynamicContext.Project).Solution;
                }

                // 改善されたFuzzyFqnLookupServiceで検索
                var fuzzyMatches = await fuzzyFqnLookupService.FindMatchesAsync(methodName, new StatelessSolutionManager(solution), cancellationToken);
                var methodMatches = fuzzyMatches
                    .Where(m => m.Symbol is IMethodSymbol)
                    .OrderByDescending(m => m.Score)
                    .Take(10) // 最大10件の候補を返す
                    .ToList();

                if (!methodMatches.Any()) {
                    var availableMethodsHint = "";
                    if (!methodName.Contains(".")) {
                        availableMethodsHint = "\n• より具体的に: 'ClassName.MethodName' の形式で試してください";
                    } else if (methodName.Split('.').Length == 2) {
                        availableMethodsHint = "\n• より具体的に: 'Namespace.ClassName.MethodName' の形式で試してください";
                    }
                    
                    throw new McpException($"メソッドが見つかりません: '{methodName}'\n確認方法:\n• {ToolHelpers.SharpToolPrefix}{nameof(GetMembers)} で利用可能なメソッドを確認{availableMethodsHint}\n• 型名とメソッド名が正しいかを確認");
                }

                // オーバーロード検出
                var methodsByName = methodMatches.GroupBy(m => ((IMethodSymbol)m.Symbol).Name).ToList();
                var overloadedMethods = methodsByName.Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();

                // 構造化されたメソッド詳細を構築
                var methods = new List<MethodDetail>();
                foreach (var match in methodMatches) {
                    var methodSymbol = (IMethodSymbol)match.Symbol;
                    var methodDetail = await BuildStructuredMethodDetail(methodSymbol, match, codeAnalysisService, cancellationToken);
                    methodDetail.IsOverloaded = overloadedMethods.Contains(match);
                    methods.Add(methodDetail);
                }

                // 結果オブジェクトの構築
                var result = new MethodSignatureResult {
                    SearchTerm = methodName,
                    TotalMatches = methodMatches.Count,
                    Methods = methods,
                    Note = $"Use these signatures with {ToolHelpers.SharpToolPrefix}OverwriteMember to safely update methods"
                };

                return result;
            } finally {
                workspace?.Dispose();
            }
        }, logger, nameof(GetMethodSignature), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(GetMethodImplementation), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("🔍 .NET専用 - .cs/.sln/.csprojファイルのみ対応。メソッドの完全な実装（本体含む）を取得")]
    public static async Task<object> GetMethodImplementation(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeAnalysisService codeAnalysisService,
        IFuzzyFqnLookupService fuzzyFqnLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        [Description(".NETソリューション(.sln)またはプロジェクト(.csproj)ファイルのパス")] string solutionOrProjectPath,
        [Description("Method name (e.g., 'ProcessData', 'MyClass.ProcessData', or full FQN)")] string methodName,
        [Description("実装の最大行数制限（デフォルト: 500）")] int maxLines = 500,
        [Description("XMLドキュメントも含めるか（デフォルト: true）")] bool includeDocumentation = true,
        [Description("依存関係情報も含めるか（デフォルト: false）")] bool includeDependencies = false,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(solutionOrProjectPath, nameof(GetMethodImplementation), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(solutionOrProjectPath, nameof(solutionOrProjectPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(methodName, nameof(methodName), logger);

            logger.LogInformation("Executing '{GetMethodImplementation}' for: {MethodName} in context {ProjectOrFilePath}",
                nameof(GetMethodImplementation), methodName, solutionOrProjectPath);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(solutionOrProjectPath);

            try {
                Solution solution;
                if (context is Solution sol) {
                    solution = sol;
                } else if (context is Project proj) {
                    solution = proj.Solution;
                } else {
                    var dynamicContext = (dynamic)context;
                    solution = ((Project)dynamicContext.Project).Solution;
                }

                // 改善されたFuzzyFqnLookupServiceで検索
                var fuzzyMatches = await fuzzyFqnLookupService.FindMatchesAsync(methodName, new StatelessSolutionManager(solution), cancellationToken);
                var methodMatches = fuzzyMatches
                    .Where(m => m.Symbol is IMethodSymbol)
                    .OrderByDescending(m => m.Score)
                    .Take(10) // 最大10件の候補を返す
                    .ToList();

                if (!methodMatches.Any()) {
                    var availableMethodsHint = "";
                    if (!methodName.Contains(".")) {
                        availableMethodsHint = "\n• より具体的に: 'ClassName.MethodName' の形式で試してください";
                    } else if (methodName.Split('.').Length == 2) {
                        availableMethodsHint = "\n• より具体的に: 'Namespace.ClassName.MethodName' の形式で試してください";
                    }
                    
                    throw new McpException($"メソッドが見つかりません: '{methodName}'\n確認方法:\n• {ToolHelpers.SharpToolPrefix}{nameof(GetMembers)} で利用可能なメソッドを確認{availableMethodsHint}\n• 型名とメソッド名が正しいかを確認");
                }

                // メソッドのグループ（オーバーロード）を識別
                var methodGroups = methodMatches
                    .GroupBy(m => new { 
                        ContainingType = ((IMethodSymbol)m.Symbol).ContainingType.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
                        Name = ((IMethodSymbol)m.Symbol).Name 
                    })
                    .ToList();

                var results = new List<MethodImplementationDetail>();

                foreach (var match in methodMatches) {
                    var methodSymbol = (IMethodSymbol)match.Symbol;
                    var detail = await BuildMethodImplementationDetail(methodSymbol, match, solution, codeAnalysisService, maxLines, includeDocumentation, includeDependencies, cancellationToken);
                    
                    // オーバーロードが存在するかチェック
                    var groupKey = new { 
                        ContainingType = methodSymbol.ContainingType.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
                        Name = methodSymbol.Name 
                    };
                    detail.IsOverloaded = methodGroups.Any(g => g.Key.Equals(groupKey) && g.Count() > 1);
                    
                    results.Add(detail);
                }

                var result = new MethodImplementationResult {
                    SearchTerm = methodName,
                    TotalMatches = results.Count,
                    Methods = results
                };

                if (results.Count > 1) {
                    result.Note = $"複数のメソッドが見つかりました。より具体的な名前で検索するか、{ToolHelpers.SharpToolPrefix}OverwriteMember で特定のシグネチャを指定してください。";
                }

                return ToolHelpers.ToJson(result);
            } finally {
                workspace?.Dispose();
            }
        }, logger, nameof(GetMethodImplementation), cancellationToken);
    }

    // 構造化されたメソッド詳細を構築するヘルパーメソッド
    private static async Task<MethodDetail> BuildStructuredMethodDetail(IMethodSymbol methodSymbol, FuzzyMatchResult match, ICodeAnalysisService codeAnalysisService, CancellationToken cancellationToken) {
        // 基本情報の取得
        var locationInfo = GetDeclarationLocationInfo(methodSymbol);
        var location = locationInfo.FirstOrDefault();
        
        // シグネチャ構築
        var modifiers = ToolHelpers.GetRoslynSymbolModifiersString(methodSymbol);
        var signature = CodeAnalysisService.GetFormattedSignatureAsync(methodSymbol, false);
        
        string fullSignature;
        if (!string.IsNullOrWhiteSpace(modifiers)) {
            if (signature.StartsWith(modifiers)) {
                fullSignature = signature;
            } else {
                fullSignature = $"{modifiers} {signature}".Trim();
            }
        } else {
            fullSignature = signature;
        }
        
        // 重複除去
        if (methodSymbol.ReturnsVoid) {
            fullSignature = System.Text.RegularExpressions.Regex.Replace(fullSignature, @"\bvoid\s+void\b", "void");
        } else {
            var returnType = methodSymbol.ReturnType.ToDisplayString();
            var duplicatePattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(returnType)}\s+{System.Text.RegularExpressions.Regex.Escape(returnType)}\b";
            fullSignature = System.Text.RegularExpressions.Regex.Replace(fullSignature, duplicatePattern, returnType);
        }
        
        // パラメータ詳細の構築
        var parameters = new List<ParameterDetail>();
        foreach (var parameter in methodSymbol.Parameters) {
            var paramModifiers = new List<string>();
            if (parameter.RefKind == RefKind.Ref) paramModifiers.Add("ref");
            else if (parameter.RefKind == RefKind.Out) paramModifiers.Add("out");
            else if (parameter.RefKind == RefKind.In) paramModifiers.Add("in");
            if (parameter.IsParams) paramModifiers.Add("params");
            
            var paramDoc = await codeAnalysisService.GetXmlDocumentationAsync(parameter, cancellationToken);
            
            string? defaultValue = null;
            if (parameter.HasExplicitDefaultValue && parameter.ExplicitDefaultValue != null) {
                try {
                    defaultValue = parameter.ExplicitDefaultValue.ToString();
                } catch {
                    defaultValue = "<unknown>";
                }
            }
            
            parameters.Add(new ParameterDetail {
                Name = parameter.Name,
                Type = parameter.Type.ToDisplayString(),
                IsOptional = parameter.HasExplicitDefaultValue,
                DefaultValue = defaultValue,
                Documentation = paramDoc,
                Modifiers = paramModifiers
            });
        }
        
        // XML文書化の取得
        string xmlDocs = await codeAnalysisService.GetXmlDocumentationAsync(methodSymbol, cancellationToken) ?? string.Empty;
        
        // 修飾子の分析
        var modifiersList = string.IsNullOrWhiteSpace(modifiers) 
            ? new List<string>() 
            : modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        
        // ジェネリック制約の取得
        string? genericConstraints = null;
        if (methodSymbol.IsGenericMethod) {
            var constraints = methodSymbol.TypeParameters
                .Where(tp => tp.ConstraintTypes.Any() || tp.HasReferenceTypeConstraint || tp.HasValueTypeConstraint || tp.HasUnmanagedTypeConstraint)
                .Select(tp => {
                    var constraintParts = new List<string>();
                    if (tp.HasReferenceTypeConstraint) constraintParts.Add("class");
                    if (tp.HasValueTypeConstraint) constraintParts.Add("struct");
                    if (tp.HasUnmanagedTypeConstraint) constraintParts.Add("unmanaged");
                    constraintParts.AddRange(tp.ConstraintTypes.Select(ct => ct.ToDisplayString()));
                    return $"where {tp.Name} : {string.Join(", ", constraintParts)}";
                });
            
            if (constraints.Any()) {
                genericConstraints = string.Join(" ", constraints);
            }
        }
        
        return new MethodDetail {
            Name = methodSymbol.Name,
            Signature = fullSignature,
            FullyQualifiedName = FuzzyFqnLookupService.GetSearchableString(methodSymbol),
            ContainingType = methodSymbol.ContainingType.ToDisplayString(),
            ReturnType = methodSymbol.ReturnType.ToDisplayString(),
            Parameters = parameters,
            Location = location,
            Documentation = xmlDocs,
            StructuredDocumentation = XmlDocumentationParser.ParseXmlDocumentation(xmlDocs),
            Modifiers = modifiersList,
            MatchScore = match.Score,
            MatchReason = match.MatchReason,
            IsOverloaded = false, // この値は後で設定される
            GenericConstraints = genericConstraints
        };
    }

    // メソッドの完全な実装詳細を構築するヘルパーメソッド
    private static async Task<MethodImplementationDetail> BuildMethodImplementationDetail(
        IMethodSymbol methodSymbol, 
        FuzzyMatchResult match, 
        Solution solution,
        ICodeAnalysisService codeAnalysisService, 
        int maxLines,
        bool includeDocumentation,
        bool includeDependencies,
        CancellationToken cancellationToken) {
        
        // 基本情報の取得
        var locationInfo = GetDeclarationLocationInfo(methodSymbol);
        var location = locationInfo.FirstOrDefault();
        
        // シグネチャの生成
        var signature = CodeAnalysisService.GetFormattedSignatureAsync(methodSymbol, includeContainingType: true);
        
        // モディファイアの取得
        var modifiers = ToolHelpers.GetRoslynSymbolModifiersString(methodSymbol).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        
        // パラメータ詳細の取得
        var parameters = new List<AnalysisTools.ParameterDetail>();
        foreach (var param in methodSymbol.Parameters) {
            var paramModifiers = new List<string>();
            if (param.RefKind == RefKind.Ref) paramModifiers.Add("ref");
            else if (param.RefKind == RefKind.Out) paramModifiers.Add("out");
            else if (param.RefKind == RefKind.In) paramModifiers.Add("in");
            if (param.IsParams) paramModifiers.Add("params");
            
            parameters.Add(new AnalysisTools.ParameterDetail {
                Name = param.Name,
                Type = param.Type.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
                DefaultValue = param.HasExplicitDefaultValue ? param.ExplicitDefaultValue?.ToString() : null,
                IsOptional = param.IsOptional,
                Modifiers = paramModifiers
            });
        }
        
        // 実装の取得
        string fullImplementation = string.Empty;
        string? xmlDocumentation = null;
        int actualLineCount = 0;
        MethodDependencies? dependencies = null;
        
        if (methodSymbol.DeclaringSyntaxReferences.Any()) {
            var syntaxRef = methodSymbol.DeclaringSyntaxReferences.First();
            var syntaxTree = syntaxRef.SyntaxTree;
            
            if (syntaxTree != null) {
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var methodNode = root.FindNode(syntaxRef.Span);
                
                // より上位のメソッド宣言ノードを探す
                var methodDeclaration = methodNode.AncestorsAndSelf()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault();
                
                if (methodDeclaration != null) {
                    // XMLドキュメントの取得
                    if (includeDocumentation) {
                        var trivia = methodDeclaration.GetLeadingTrivia();
                        var xmlTrivia = trivia
                            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || 
                                       t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                            .ToList();
                        
                        if (xmlTrivia.Any()) {
                            xmlDocumentation = string.Join("\n", xmlTrivia.Select(t => t.ToString().Trim()));
                        }
                    }
                    
                    // 完全な実装を取得（属性、XMLドキュメント、メソッド本体含む）
                    var fullNode = methodDeclaration;
                    if (includeDocumentation && xmlDocumentation != null) {
                        // XMLドキュメントコメントも含める
                        fullImplementation = methodDeclaration.GetLeadingTrivia().ToString() + methodDeclaration.ToString();
                    } else {
                        fullImplementation = methodDeclaration.ToString();
                    }
                    
                    // インデントの正規化
                    fullImplementation = NormalizeIndentation(fullImplementation);
                    
                    // 行数のカウント
                    var lines = fullImplementation.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    actualLineCount = lines.Length;
                    
                    // 依存関係の解析
                    if (includeDependencies) {
                        var document = solution.GetDocument(syntaxTree);
                        if (document != null) {
                            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                            if (semanticModel != null) {
                                dependencies = AnalyzeDependencies(methodDeclaration, semanticModel);
                            }
                        }
                    }
                }
            }
        }
        
        // サイズ制限の適用
        bool isTruncated = false;
        string? truncationWarning = null;
        int displayedLineCount = actualLineCount;
        
        if (actualLineCount > maxLines) {
            isTruncated = true;
            fullImplementation = TruncateImplementation(fullImplementation, maxLines);
            displayedLineCount = maxLines;
            truncationWarning = $"実装が{maxLines}行を超えています（実際: {actualLineCount}行）。表示は最初と最後の{maxLines/2}行に制限されています。";
        }
        
        return new MethodImplementationDetail {
            Name = methodSymbol.Name,
            Signature = signature,
            FullImplementation = fullImplementation,
            FullyQualifiedName = methodSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
            ContainingType = methodSymbol.ContainingType.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
            ReturnType = methodSymbol.ReturnType.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
            Parameters = parameters,
            Location = location != null ? new LocationInfo {
                FilePath = location.FilePath,
                StartLine = location.StartLine,
                EndLine = location.EndLine
            } : null,
            XmlDocumentation = xmlDocumentation,
            Modifiers = modifiers,
            IsOverloaded = false, // これは呼び出し元で設定される
            IsTruncated = isTruncated,
            ActualLineCount = actualLineCount,
            DisplayedLineCount = displayedLineCount,
            Dependencies = dependencies,
            TruncationWarning = truncationWarning
        };
    }
    
    // インデントを正規化するヘルパーメソッド
    private static string NormalizeIndentation(string implementation) {
        var lines = implementation.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        // 空でない行の最小インデントを見つける
        int minIndent = int.MaxValue;
        foreach (var line in lines) {
            if (!string.IsNullOrWhiteSpace(line)) {
                int indent = 0;
                while (indent < line.Length && char.IsWhiteSpace(line[indent])) {
                    indent++;
                }
                minIndent = Math.Min(minIndent, indent);
            }
        }
        
        // 最小インデントを削除
        if (minIndent < int.MaxValue && minIndent > 0) {
            for (int i = 0; i < lines.Length; i++) {
                if (lines[i].Length >= minIndent) {
                    lines[i] = lines[i].Substring(minIndent);
                }
            }
        }
        
        return string.Join(Environment.NewLine, lines);
    }
    
    // 実装を省略するヘルパーメソッド
    private static string TruncateImplementation(string implementation, int maxLines) {
        var lines = implementation.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        if (lines.Length <= maxLines) {
            return implementation;
        }
        
        int halfLines = maxLines / 2;
        var result = new List<string>();
        
        // 最初のhalfLines行
        result.AddRange(lines.Take(halfLines));
        
        // 省略マーカー
        result.Add("");
        result.Add("// ... [省略: " + (lines.Length - maxLines) + "行] ...");
        result.Add("");
        
        // 最後のhalfLines行
        result.AddRange(lines.Skip(lines.Length - halfLines));
        
        return string.Join(Environment.NewLine, result);
    }
    
    // 依存関係を解析するヘルパーメソッド
    private static MethodDependencies AnalyzeDependencies(MethodDeclarationSyntax methodNode, SemanticModel semanticModel) {
        var dependencies = new MethodDependencies();
        
        // メソッド呼び出しの解析
        var invocations = methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations) {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol calledMethod) {
                var methodName = calledMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (!dependencies.CalledMethods.Contains(methodName)) {
                    dependencies.CalledMethods.Add(methodName);
                }
            }
        }
        
        // フィールド・プロパティアクセスの解析
        var memberAccesses = methodNode.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
        foreach (var memberAccess in memberAccesses) {
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is IFieldSymbol field) {
                var fieldName = field.Name;
                if (!dependencies.UsedFields.Contains(fieldName)) {
                    dependencies.UsedFields.Add(fieldName);
                }
            } else if (symbolInfo.Symbol is IPropertySymbol property) {
                var propertyName = property.Name;
                if (!dependencies.UsedProperties.Contains(propertyName)) {
                    dependencies.UsedProperties.Add(propertyName);
                }
            }
        }
        
        // 使用している型の解析
        var identifierNames = methodNode.DescendantNodes().OfType<IdentifierNameSyntax>();
        foreach (var identifier in identifierNames) {
            var symbolInfo = semanticModel.GetSymbolInfo(identifier);
            if (symbolInfo.Symbol is ITypeSymbol typeSymbol && 
                typeSymbol.TypeKind != TypeKind.TypeParameter) {
                var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (!dependencies.UsedTypes.Contains(typeName) && 
                    !IsCommonType(typeName)) {
                    dependencies.UsedTypes.Add(typeName);
                }
            }
        }
        
        // throw文の解析
        var throwStatements = methodNode.DescendantNodes().OfType<ThrowStatementSyntax>();
        foreach (var throwStatement in throwStatements) {
            if (throwStatement.Expression != null) {
                var typeInfo = semanticModel.GetTypeInfo(throwStatement.Expression);
                if (typeInfo.Type != null) {
                    var exceptionType = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    if (!dependencies.ThrownExceptions.Contains(exceptionType)) {
                        dependencies.ThrownExceptions.Add(exceptionType);
                    }
                }
            }
        }
        
        return dependencies;
    }
    
    // 一般的な型かどうかを判定するヘルパーメソッド
    private static bool IsCommonType(string typeName) {
        var commonTypes = new HashSet<string> {
            "string", "int", "long", "double", "float", "decimal", "bool",
            "object", "void", "Task", "Task<>", "List<>", "Dictionary<,>",
            "IEnumerable<>", "IList<>", "Array", "DateTime", "TimeSpan"
        };
        
        return commonTypes.Any(t => typeName.StartsWith(t));
    }

    #endregion

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(FindUsages), Idempotent = true, Destructive = false, OpenWorld = true, ReadOnly = true)]
    [Description("🔍 .NET専用 - .sln/.csprojファイルのみ対応。プロジェクト全体でシンボルの使用箇所を検索")]
    public static async Task<object> FindUsages(
        StatelessWorkspaceFactory workspaceFactory,
        ILogger<AnalysisToolsLogCategory> logger,
        [Description(".NETソリューション(.sln)またはプロジェクト(.csproj)ファイルのパス")] string solutionOrProjectPath,
        [Description("検索対象のシンボル名（クラス名、メソッド名、プロパティ名など）")] string symbolName,
        [Description("検索結果の最大件数（デフォルト: 100）")] int maxResults = 100,
        [Description("プライベートメンバーも含めるか（デフォルト: true）")] bool includePrivateMembers = true,
        [Description("継承されたメンバーも含めるか（デフォルト: false）")] bool includeInheritedMembers = false,
        [Description("外部ライブラリのシンボルも検索するか（デフォルト: true）")] bool includeExternalSymbols = true,
        [Description("検索モード: 'declaration'(宣言のみ) | 'usage'(使用箇所) | 'all'(両方)（デフォルト: 'all'）")] string searchMode = "all",
        CancellationToken cancellationToken = default) {
        
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<object, AnalysisToolsLogCategory>(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(solutionOrProjectPath, nameof(FindUsages), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(solutionOrProjectPath, nameof(solutionOrProjectPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(symbolName, nameof(symbolName), logger);

            logger.LogInformation("Executing '{FindUsages}' for symbol: {SymbolName} in {FilePath}",
                nameof(FindUsages), symbolName, solutionOrProjectPath);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(solutionOrProjectPath);

            try {
                Solution solution;
                if (context is Solution sol) {
                    solution = sol;
                } else if (context is Project proj) {
                    solution = proj.Solution;
                } else {
                    // For other contexts (e.g., Document), get the solution through the project
                    var dynamicContext = (dynamic)context;
                    solution = ((Project)dynamicContext.Project).Solution;
                }

                if (solution.Projects.Count() == 0) {
                    throw new McpException($"プロジェクトが見つかりません: {solutionOrProjectPath}");
                }

                // Find symbols by name across all projects
                var allSymbols = new List<ISymbol>();
                foreach (var project in solution.Projects) {
                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    if (compilation == null) continue;

                    var symbols = await GetSymbolsByNameEnhanced(
                        compilation, 
                        symbolName, 
                        includePrivateMembers, 
                        includeInheritedMembers,
                        includeExternalSymbols,
                        searchMode,
                        cancellationToken);
                    allSymbols.AddRange(symbols);
                }

                // Remove duplicates and limit search targets
                var targetSymbols = allSymbols
                    .Distinct(SymbolEqualityComparer.Default)
                    .Take(10) // Limit to top 10 symbols for performance
                    .ToList();

                if (targetSymbols.Count == 0) {
                    return new FindUsagesResult {
                        SearchTerm = symbolName,
                        SymbolsFound = new List<FoundSymbol>(),
                        TotalReferences = 0,
                        References = new List<FileUsage>(),
                        Summary = new FindUsagesSummary {
                            AffectedFileCount = 0,
                            Truncated = false
                        },
                        Message = $"シンボル '{symbolName}' が見つかりませんでした"
                    };
                }

                // Find references for each symbol
                var allReferences = new List<SymbolReferenceLocation>();
                foreach (var symbol in targetSymbols) {
                    var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
                    
                    foreach (var reference in references) {
                        foreach (var location in reference.Locations) {
                            if (location.Location.IsInSource && location.Document?.FilePath != null) {
                                var contextText = await GetContextText(location.Document, location.Location, cancellationToken);
                                var lineSpan = location.Location.GetLineSpan();
                                
                                allReferences.Add(new SymbolReferenceLocation {
                                    SymbolName = symbol.Name,
                                    SymbolKind = symbol.Kind.ToString(),
                                    FilePath = location.Document.FilePath,
                                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                                    ColumnNumber = lineSpan.StartLinePosition.Character + 1,
                                    ContextText = contextText
                                });
                            }
                        }
                    }
                }

                // Group results by file and create response
                var groupedReferences = allReferences
                    .OrderBy(r => r.FilePath)
                    .ThenBy(r => r.LineNumber)
                    .Take(maxResults)
                    .GroupBy(r => r.FilePath)
                    .Select(group => new FileUsage {
                        FilePath = group.Key ?? "",
                        ReferenceCount = group.Count(),
                        Locations = group.Select(r => new UsageLocation {
                            Line = r.LineNumber,
                            Column = r.ColumnNumber,
                            Context = r.ContextText,
                            SymbolKind = r.SymbolKind
                        }).ToList()
                    }).ToList();

                return new FindUsagesResult {
                    SearchTerm = symbolName,
                    SymbolsFound = targetSymbols.Select(s => new FoundSymbol {
                        Name = s.Name,
                        Kind = s.Kind.ToString(),
                        FullyQualifiedName = s.ToDisplayString(),
                        ContainingType = s.ContainingType?.Name
                    }).ToList(),
                    TotalReferences = allReferences.Count,
                    References = groupedReferences,
                    Summary = new FindUsagesSummary {
                        AffectedFileCount = allReferences.Select(r => r.FilePath).Distinct().Count(),
                        Truncated = allReferences.Count > maxResults
                    }
                };

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(FindUsages), cancellationToken);
    }

    private static List<ISymbol> GetSymbolsByName(Compilation compilation, string symbolName, bool includePrivateMembers, bool includeInheritedMembers) {
        var symbols = new List<ISymbol>();
        
        // Search through all types in the compilation
        foreach (var syntaxTree in compilation.SyntaxTrees) {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            
            // Find all declarations
            var declarations = root.DescendantNodes()
                .Where(n => n is BaseTypeDeclarationSyntax ||
                           n is MethodDeclarationSyntax ||
                           n is PropertyDeclarationSyntax ||
                           n is FieldDeclarationSyntax ||
                           n is EventDeclarationSyntax);
            
            foreach (var declaration in declarations) {
                var symbol = semanticModel.GetDeclaredSymbol(declaration);
                if (symbol != null && symbol.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase)) {
                    if (includePrivateMembers || symbol.DeclaredAccessibility != Accessibility.Private) {
                        symbols.Add(symbol);
                    }
                }
            }
        }
        
        return symbols.Distinct(SymbolEqualityComparer.Default).ToList();
    }

    private static async Task<List<ISymbol>> GetSymbolsByNameEnhanced(
        Compilation compilation, 
        string symbolName, 
        bool includePrivateMembers, 
        bool includeInheritedMembers,
        bool includeExternalSymbols,
        string searchMode,
        CancellationToken cancellationToken) {
        
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Phase 1: Declaration-based search (existing logic)
        if (searchMode == "declaration" || searchMode == "all") {
            var declaredSymbols = GetSymbolsByName(compilation, symbolName, includePrivateMembers, includeInheritedMembers);
            foreach (var symbol in declaredSymbols) {
                symbols.Add(symbol);
            }
        }
        
        // Phase 2: Usage-based search (find symbols used in code)
        if ((searchMode == "usage" || searchMode == "all") && includeExternalSymbols) {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Performance check - abort if taking too long
            if (stopwatch.ElapsedMilliseconds > 5000) {
                return symbols.ToList();
            }
            
            // Try to find external symbols in referenced assemblies
            if (searchMode == "all" || searchMode == "usage") {
                foreach (var reference in compilation.References.OfType<PortableExecutableReference>()) {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                    if (assemblySymbol != null) {
                        // Search in global namespace
                        SearchInNamespace(assemblySymbol.GlobalNamespace, symbolName, symbols, includePrivateMembers);
                    }
                    
                    // Performance check
                    if (stopwatch.ElapsedMilliseconds > 8000) {
                        break;
                    }
                }
            }
            
            foreach (var syntaxTree in compilation.SyntaxTrees) {
                cancellationToken.ThrowIfCancellationRequested();
                
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                
                // Find all identifiers that match the symbol name
                var identifiers = root.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(id => id.Identifier.Text.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
                
                // Also include generic name syntax for types like ILogger<T>
                var genericNames = root.DescendantNodes()
                    .OfType<GenericNameSyntax>()
                    .Where(gn => gn.Identifier.Text.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
                
                foreach (var identifier in identifiers) {
                    // Performance check
                    if (stopwatch.ElapsedMilliseconds > 10000) {
                        // Taking too long, return what we have
                        return symbols.ToList();
                    }
                    
                    var symbolInfo = semanticModel.GetSymbolInfo(identifier, cancellationToken);
                    
                    // Add the symbol if found
                    if (symbolInfo.Symbol != null) {
                        // Check accessibility
                        if (includePrivateMembers || symbolInfo.Symbol.DeclaredAccessibility != Accessibility.Private) {
                            symbols.Add(symbolInfo.Symbol);
                        }
                    }
                    
                    // Also check candidate symbols (for ambiguous references)
                    foreach (var candidate in symbolInfo.CandidateSymbols) {
                        if (includePrivateMembers || candidate.DeclaredAccessibility != Accessibility.Private) {
                            symbols.Add(candidate);
                        }
                    }
                }
                
                // Process generic names (e.g., ILogger<T>)
                foreach (var genericName in genericNames) {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Performance check
                    if (stopwatch.ElapsedMilliseconds > 10000) {
                        return symbols.ToList();
                    }
                    
                    var symbolInfo = semanticModel.GetSymbolInfo(genericName, cancellationToken);
                    
                    if (symbolInfo.Symbol != null) {
                        if (includePrivateMembers || symbolInfo.Symbol.DeclaredAccessibility != Accessibility.Private) {
                            symbols.Add(symbolInfo.Symbol);
                        }
                    }
                    
                    foreach (var candidate in symbolInfo.CandidateSymbols) {
                        if (includePrivateMembers || candidate.DeclaredAccessibility != Accessibility.Private) {
                            symbols.Add(candidate);
                        }
                    }
                }
                
                // Also look for member access expressions (e.g., logger.LogInformation)
                var memberAccess = root.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(ma => ma.Name.Identifier.Text.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
                
                foreach (var access in memberAccess) {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var symbolInfo = semanticModel.GetSymbolInfo(access.Name, cancellationToken);
                    if (symbolInfo.Symbol != null) {
                        if (includePrivateMembers || symbolInfo.Symbol.DeclaredAccessibility != Accessibility.Private) {
                            symbols.Add(symbolInfo.Symbol);
                        }
                    }
                }
            }
        }
        
        return symbols.ToList();
    }

    private static async Task<string> GetContextText(Document document, Location location, CancellationToken cancellationToken) {
        try {
            var sourceText = await document.GetTextAsync(cancellationToken);
            var lineSpan = location.GetLineSpan();
            var line = sourceText.Lines[lineSpan.StartLinePosition.Line];
            return line.ToString().Trim();
        } catch {
            return "";
        }
    }
    
    private static void SearchInNamespace(INamespaceSymbol namespaceSymbol, string symbolName, HashSet<ISymbol> symbols, bool includePrivateMembers) {
        // Search types in this namespace
        foreach (var type in namespaceSymbol.GetTypeMembers()) {
            if (type.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase)) {
                if (includePrivateMembers || type.DeclaredAccessibility != Accessibility.Private) {
                    symbols.Add(type);
                }
            }
            
            // Search members in the type
            foreach (var member in type.GetMembers()) {
                if (member.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase)) {
                    if (includePrivateMembers || member.DeclaredAccessibility != Accessibility.Private) {
                        symbols.Add(member);
                    }
                }
            }
        }
        
        // Recursively search child namespaces
        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers()) {
            SearchInNamespace(childNamespace, symbolName, symbols, includePrivateMembers);
        }
    }
}