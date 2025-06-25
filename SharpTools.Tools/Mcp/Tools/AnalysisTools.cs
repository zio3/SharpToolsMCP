using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol;
using SharpTools.Tools.Services;
using System.Reflection;
using System.Text.Json;

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

            var memberInfo = new {
                signature = ToolHelpers.GetRoslynSymbolModifiersString(member) + " " + CodeAnalysisService.GetFormattedSignatureAsync(member, false),
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
    [Description("Display members (methods, properties, fields) of classes and interfaces. Perfect for understanding APIs and implementation details")]
    public static async Task<object> GetMembers(
    StatelessWorkspaceFactory workspaceFactory,
    ICodeAnalysisService codeAnalysisService,
    IFuzzyFqnLookupService fuzzyFqnLookupService,
    ILogger<AnalysisToolsLogCategory> logger,
    [Description("Path to your project file (.csproj), solution (.sln), or any C# file in the project")] string contextPath,
    [Description("Target class name to analyze. Use fully qualified name (MyApp.Services.UserService) or short name (UserService)")] string fullyQualifiedTypeName,
    [Description("Include private members in results (true=show all, false=public only)")] bool includePrivateMembers,
    CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(contextPath, nameof(contextPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedTypeName, nameof(fullyQualifiedTypeName), logger);

            logger.LogInformation("Executing '{GetMembers}' for: {TypeName} in context {ContextPath} (IncludePrivate: {IncludePrivate})",
                nameof(GetMembers), fullyQualifiedTypeName, contextPath, includePrivateMembers);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(contextPath);

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
                var fuzzyMatches = await fuzzyFqnLookupService.FindMatchesAsync(fullyQualifiedTypeName, new StatelessSolutionManager(solution), cancellationToken);
                var bestMatch = fuzzyMatches.FirstOrDefault();
                if (bestMatch == null || !(bestMatch.Symbol is INamedTypeSymbol namedTypeSymbol)) {
                    throw new McpException($"🔍 型が見つかりません: '{fullyQualifiedTypeName}'\n💡 確認方法:\n• {ToolHelpers.SharpToolPrefix}{nameof(Tools.DocumentTools.ReadTypesFromRoslynDocument)} で利用可能な型を確認\n• 完全修飾名（MyApp.Models.User）で試してください\n• 名前空間が正しいかを確認");
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
                            string signature = ToolHelpers.GetRoslynSymbolModifiersString(member)
                                + " " + (CodeAnalysisService.GetFormattedSignatureAsync(member, false)).Replace(typeName + ".", string.Empty).Trim()
                                + (!string.IsNullOrEmpty(xmlDocs) ? $" // {xmlDocs.Replace("\n", " ").Replace("\r", "")}" : "");

                            if (!membersByLocation.ContainsKey(locationKey)) {
                                membersByLocation[locationKey] = new Dictionary<string, List<string>>();
                            }

                            if (!membersByLocation[locationKey].ContainsKey(kind)) {
                                membersByLocation[locationKey][kind] = new List<string>();
                            }

                            membersByLocation[locationKey][kind].Add(signature);
                        } catch (Exception ex) {
                            logger.LogWarning(ex, "Error processing member {MemberName} in {TypeName}", member.Name, fullyQualifiedTypeName);
                        }
                    }
                }

                var typeLocations = GetDeclarationLocationInfo(namedTypeSymbol);
                return ToolHelpers.ToJson(new {
                    kind = ToolHelpers.GetSymbolKindString(namedTypeSymbol),
                    signature = ToolHelpers.GetRoslynSymbolModifiersString(namedTypeSymbol) + " " + namedTypeSymbol.ToDisplayString(ToolHelpers.FullyQualifiedFormatWithoutGlobal),
                    xmlDocs = await codeAnalysisService.GetXmlDocumentationAsync(namedTypeSymbol, cancellationToken) ?? string.Empty,
                    note = $"💡 Next steps: Use {ToolHelpers.SharpToolPrefix}{nameof(GetMethodSignature)} to see method signatures, or {ToolHelpers.SharpToolPrefix}{nameof(GetMembers)} to explore type members.",
                    locations = typeLocations,
                    includesPrivateMembers = includePrivateMembers,
                    membersByLocation = membersByLocation
                });
            } finally {
                workspace?.Dispose();
            }
        }, logger, nameof(GetMembers), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(GetMethodSignature), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("🔍 Safely view method signature without the body. Perfect for checking before using OverwriteMember. Example: 'MyClass.ProcessData'")]
    public static async Task<string> GetMethodSignature(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeAnalysisService codeAnalysisService,
        IFuzzyFqnLookupService fuzzyFqnLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        [Description("Path to your project file (.csproj), solution (.sln), or any C# file in the project")] string contextPath,
        [Description("The method name. Examples: 'MyClass.ProcessData', 'ProcessData', or full FQN")] string methodIdentifier,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(contextPath, nameof(contextPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(methodIdentifier, nameof(methodIdentifier), logger);

            logger.LogInformation("Executing '{GetMethodSignature}' for: {MethodIdentifier} in context {ContextPath}",
                nameof(GetMethodSignature), methodIdentifier, contextPath);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(contextPath);

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
                var fuzzyMatches = await fuzzyFqnLookupService.FindMatchesAsync(methodIdentifier, new StatelessSolutionManager(solution), cancellationToken);
                var bestMatch = fuzzyMatches.FirstOrDefault(m => m.Symbol is IMethodSymbol);

                if (bestMatch == null || !(bestMatch.Symbol is IMethodSymbol methodSymbol)) {
                    throw new McpException($"🔍 メソッドが見つかりません: '{methodIdentifier}'\n💡 確認方法:\n• {ToolHelpers.SharpToolPrefix}{nameof(GetMembers)} で利用可能なメソッドを確認\n• 完全修飾名（MyClass.MyMethod）で試してください\n• 型名とメソッド名が正しいかを確認");
                }

                // Get the declaration location
                var locationInfo = GetDeclarationLocationInfo(methodSymbol);
                var location = locationInfo.FirstOrDefault();

                // Build the signature
                var modifiers = ToolHelpers.GetRoslynSymbolModifiersString(methodSymbol);
                var signature = CodeAnalysisService.GetFormattedSignatureAsync(methodSymbol, false);
                // Remove duplicate modifiers if any
                var fullSignature = $"{modifiers} {signature}".Trim();
                
                // Fix duplicate return type issue
                if (methodSymbol.ReturnsVoid) {
                    fullSignature = fullSignature.Replace("void void", "void");
                } else {
                    var returnType = methodSymbol.ReturnType.ToDisplayString();
                    var duplicatePattern = $"{returnType} {returnType}";
                    if (fullSignature.Contains(duplicatePattern)) {
                        fullSignature = fullSignature.Replace(duplicatePattern, returnType);
                    }
                }

                // Get XML documentation if available
                string xmlDocs = await codeAnalysisService.GetXmlDocumentationAsync(methodSymbol, cancellationToken) ?? string.Empty;

                // Get parameter descriptions
                var parameterDescriptions = new List<string>();
                foreach (var parameter in methodSymbol.Parameters) {
                    var paramXmlDoc = await codeAnalysisService.GetXmlDocumentationAsync(parameter, cancellationToken);
                    if (!string.IsNullOrEmpty(paramXmlDoc)) {
                        parameterDescriptions.Add($"  - {parameter.Name}: {paramXmlDoc}");
                    }
                }

                var result = new System.Text.StringBuilder();
                result.AppendLine($"📋 Method Signature for '{methodSymbol.Name}':");
                result.AppendLine($"   Location: {location?.FilePath ?? "Unknown"} (Line {location?.StartLine ?? 0})");
                result.AppendLine($"   Signature: {fullSignature}");

                if (!string.IsNullOrEmpty(xmlDocs)) {
                    result.AppendLine($"   Documentation: {xmlDocs}");
                }

                if (parameterDescriptions.Any()) {
                    result.AppendLine("   Parameters:");
                    foreach (var desc in parameterDescriptions) {
                        result.AppendLine(desc);
                    }
                }

                result.AppendLine($"\n💡 Use this signature with {ToolHelpers.SharpToolPrefix}OverwriteMember to safely update the method");

                return result.ToString();
            } finally {
                workspace?.Dispose();
            }
        }, logger, nameof(GetMethodSignature), cancellationToken);
    }

    #endregion
}