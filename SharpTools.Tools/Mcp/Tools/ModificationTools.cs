using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Mcp;
using SharpTools.Tools.Services;

namespace SharpTools.Tools.Mcp.Tools;

// Marker class for ILogger<T> category specific to ModificationTools
public class ModificationToolsLogCategory { }

[McpServerToolType]
public static class ModificationTools {
    private static string GetMemberName(MemberDeclarationSyntax memberSyntax) {
        return memberSyntax switch {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
            DestructorDeclarationSyntax dtor => dtor.Identifier.Text,
            OperatorDeclarationSyntax op => op.OperatorToken.Text,
            ConversionOperatorDeclarationSyntax conv => conv.Type.ToString(),
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.Text,
            EnumDeclarationSyntax enumDecl => enumDecl.Identifier.Text,
            TypeDeclarationSyntax type => type.Identifier.Text,
            DelegateDeclarationSyntax del => del.Identifier.Text,
            EventDeclarationSyntax evt => evt.Identifier.Text,
            EventFieldDeclarationSyntax evtField => evtField.Declaration.Variables.First().Identifier.Text,
            IndexerDeclarationSyntax indexer => "this[]", // Indexers don't have names but use the 'this' keyword
            NamespaceDeclarationSyntax ns => ns.Name.ToString(), // Handle namespace declarations
            FileScopedNamespaceDeclarationSyntax fsns => fsns.Name.ToString(), // Handle file-scoped namespace declarations
            _ => throw new NotSupportedException($"Unsupported member type: {memberSyntax.GetType().Name}")
        };
    }
    // Helper method to check if the member is allowed to be added
    private static bool IsMemberAllowed(INamedTypeSymbol typeSymbol, MemberDeclarationSyntax newMember, string memberName, CancellationToken cancellationToken) {
        // Special handling for method overloads
        if (newMember is MethodDeclarationSyntax newMethod) {
            // Get all existing methods with the same name
            var existingMethods = typeSymbol.GetMembers(memberName)
                .OfType<IMethodSymbol>()
                .Where(m => !m.IsImplicitlyDeclared && m.MethodKind == MethodKind.Ordinary)
                .ToList();

            if (!existingMethods.Any()) {
                return true; // No method with the same name exists
            }

            // Convert parameters of the new method to comparable format
            var newMethodParams = newMethod.ParameterList.Parameters
                .Select(p => new {
                    Type = p.Type?.ToString() ?? "unknown",
                    IsRef = p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword)),
                    IsOut = p.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword))
                })
                .ToList();

            // Check if any existing method has the same parameter signature
            foreach (var existingMethod in existingMethods) {
                if (existingMethod.Parameters.Length != newMethodParams.Count) {
                    continue; // Different parameter count, not a duplicate
                }

                bool signatureMatches = true;
                for (int i = 0; i < existingMethod.Parameters.Length; i++) {
                    var existingParam = existingMethod.Parameters[i];
                    var newParam = newMethodParams[i];

                    // Compare parameter types and ref/out modifiers
                    if (existingParam.Type.ToDisplayString() != newParam.Type ||
                        existingParam.RefKind == RefKind.Ref != newParam.IsRef ||
                        existingParam.RefKind == RefKind.Out != newParam.IsOut) {
                        signatureMatches = false;
                        break;
                    }
                }

                if (signatureMatches) {
                    return false; // Found a method with the same signature
                }
            }

            return true; // No matching signature found
        } else {
            // For non-method members, simply check if a member with the same name exists
            return !typeSymbol.GetMembers(memberName).Any(m => !m.IsImplicitlyDeclared);
        }
    }

    // Helper method to match symbols with flexibility
    private static bool IsSymbolMatch(ISymbol symbol, string fullyQualifiedName) {
        // Direct name match (for simple names)
        if (symbol.Name == fullyQualifiedName)
            return true;

        // Build the fully qualified name manually
        var fqn = BuildFullyQualifiedName(symbol);
        if (fqn == fullyQualifiedName)
            return true;

        // For methods, also try without parentheses
        if (symbol is IMethodSymbol) {
            var displayString = symbol.ToDisplayString();
            var displayWithoutParens = displayString.Replace("()", "");
            if (displayWithoutParens == fullyQualifiedName)
                return true;
        }

        // Check if the pattern matches the end of the FQN
        if (fqn.EndsWith("." + fullyQualifiedName))
            return true;

        return false;
    }

    private static string BuildFullyQualifiedName(ISymbol symbol) {
        var parts = new List<string>();

        // Add symbol name
        parts.Add(symbol.Name);

        // Add containing types and namespaces
        var container = symbol.ContainingSymbol;
        while (container != null) {
            if (container is INamespaceSymbol ns && ns.IsGlobalNamespace)
                break;

            parts.Insert(0, container.Name);
            container = container.ContainingSymbol;
        }

        return string.Join(".", parts);
    }
    // Helper method to check if a string is a valid C# identifier
    private static bool IsValidCSharpIdentifier(string name) {
        return SyntaxFacts.IsValidIdentifier(name);
    }

    // Legacy stateful MoveMember has been removed - use the stateless version in #region Main Methods
    /// <summary>
    /// Finds an existing document in the project that contains the specified namespace.
    /// </summary>
    private static async Task<Document?> FindExistingDocumentWithNamespaceAsync(Project project, INamespaceSymbol namespaceSymbol, CancellationToken cancellationToken) {
        var namespaceName = namespaceSymbol.ToDisplayString();

        foreach (var document in project.Documents) {
            if (document.FilePath?.EndsWith(".cs") != true) continue;

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root is CompilationUnitSyntax compilationUnit) {
                // Check if this document already contains the target namespace
                var hasNamespace = compilationUnit.Members
                    .OfType<NamespaceDeclarationSyntax>()
                    .Any(n => n.Name.ToString() == namespaceName);

                if (hasNamespace || (namespaceSymbol.IsGlobalNamespace && compilationUnit.Members.Any())) {
                    return document;
                }
            }
        }

        return null;
    }
    /// <summary>
    /// Creates a new document for the specified namespace.
    /// </summary>
    private static Task<Document> CreateDocumentForNamespaceAsync(Project project, INamespaceSymbol namespaceSymbol, CancellationToken cancellationToken) {
        var namespaceName = namespaceSymbol.ToDisplayString();
        var fileName = string.IsNullOrEmpty(namespaceName) || namespaceSymbol.IsGlobalNamespace
            ? "GlobalNamespace.cs"
            : $"{namespaceName.Split('.').Last()}.cs";

        // Ensure the file name doesn't conflict with existing files
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;
        var projectDirectory = Path.GetDirectoryName(project.FilePath) ?? throw new InvalidOperationException("Project directory not found");

        var fullPath = Path.Combine(projectDirectory, fileName);
        while (project.Documents.Any(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))) {
            fileName = $"{baseName}{counter}{extension}";
            fullPath = Path.Combine(projectDirectory, fileName);
            counter++;
        }

        // Create basic content for the new file
        var content = namespaceSymbol.IsGlobalNamespace
            ? "// Global namespace file\n"
            : $"namespace {namespaceName} {{\n    // Namespace content\n}}\n";

        var newDocument = project.AddDocument(fileName, content, filePath: fullPath);
        return Task.FromResult(newDocument);
    }
    /// <summary>
    /// Adds a member to the specified namespace in the given document.
    /// </summary>
    private static async Task<Solution> AddMemberToNamespaceAsync(Document document, INamespaceSymbol namespaceSymbol, MemberDeclarationSyntax memberDeclaration, ICodeModificationService modificationService, CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root is not CompilationUnitSyntax compilationUnit) {
            throw new McpException("Destination document does not have a valid compilation unit.");
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

        if (namespaceSymbol.IsGlobalNamespace) {
            // Add to global namespace (compilation unit)
            editor.AddMember(compilationUnit, memberDeclaration);
        } else {
            // Find or create the target namespace
            var namespaceName = namespaceSymbol.ToDisplayString();
            var targetNamespace = compilationUnit.Members
                .OfType<NamespaceDeclarationSyntax>()
                .FirstOrDefault(n => n.Name.ToString() == namespaceName);

            if (targetNamespace == null) {
                // Create the namespace and add the member to it
                targetNamespace = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
                    .AddMembers(memberDeclaration);
                editor.AddMember(compilationUnit, targetNamespace);
            } else {
                // Add member to existing namespace
                editor.AddMember(targetNamespace, memberDeclaration);
            }
        }

        var changedDocument = editor.GetChangedDocument();
        var formattedDocument = await modificationService.FormatDocumentAsync(changedDocument, cancellationToken);
        return formattedDocument.Project.Solution;
    }

    /// <summary>
    /// Properly removes a member from its parent by deleting it from the parent's member collection.
    /// </summary>
    private static async Task<Solution> RemoveMemberFromParentAsync(Document document, SyntaxNode memberNode, ICodeModificationService modificationService, CancellationToken cancellationToken) {
        if (memberNode is not MemberDeclarationSyntax memberDeclaration) {
            throw new McpException($"Node is not a member declaration: {memberNode.GetType().Name}");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) {
            throw new McpException("Could not get syntax root from document.");
        }

        SyntaxNode newRoot;

        if (memberNode.Parent is CompilationUnitSyntax compilationUnit) {
            // Handle top-level members in the compilation unit
            var newMembers = compilationUnit.Members.Remove(memberDeclaration);
            newRoot = compilationUnit.WithMembers(newMembers);
        } else if (memberNode.Parent is NamespaceDeclarationSyntax namespaceDecl) {
            // Handle members in a namespace
            var newMembers = namespaceDecl.Members.Remove(memberDeclaration);
            var newNamespace = namespaceDecl.WithMembers(newMembers);
            newRoot = root.ReplaceNode(namespaceDecl, newNamespace);
        } else if (memberNode.Parent is TypeDeclarationSyntax typeDecl) {
            // Handle members in a type declaration (class, struct, interface, etc.)
            var newMembers = typeDecl.Members.Remove(memberDeclaration);
            var newType = typeDecl.WithMembers(newMembers);
            newRoot = root.ReplaceNode(typeDecl, newType);
        } else {
            throw new McpException($"Cannot remove member from parent of type {memberNode.Parent?.GetType().Name ?? "null"}.");
        }

        var newDocument = document.WithSyntaxRoot(newRoot);
        var formattedDocument = await modificationService.FormatDocumentAsync(newDocument, cancellationToken);
        return formattedDocument.Project.Solution;
    }
    private static async Task<(bool changed, string diff)> ProcessNonCodeFile(
        string filePath,
        string regexPattern,
        string replacementText,
        IDocumentOperationsService documentOperations,
        List<string> modifiedFiles,
        CancellationToken cancellationToken) {
        try {
            var (originalContent, _) = await documentOperations.ReadFileAsync(filePath, false, cancellationToken);
            var regex = new Regex(regexPattern, RegexOptions.Multiline);
            string newContent = regex.Replace(originalContent.NormalizeEndOfLines(), replacementText);

            // Only write if content changed
            if (newContent != originalContent) {
                // Note: we don't pass commit message here as we'll handle Git at a higher level
                // for all modified non-code files at once
                await documentOperations.WriteFileAsync(filePath, newContent, true, cancellationToken);
                modifiedFiles.Add(filePath);

                // Generate diff
                string diff = ContextInjectors.CreateCodeDiff(originalContent, newContent);
                return (true, diff);
            }

            return (false, string.Empty);
        } catch (Exception ex) {
            throw new McpException($"Error processing non-code file {filePath}: {ex.Message}");
        }
    }

    #region Main Methods

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(AddMember), Idempotent = false, Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("Adds one or more new member definitions to a specified type in a file. Works without a pre-loaded solution.")]
    public static async Task<string> AddMember(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ICodeAnalysisService codeAnalysisService,
        IComplexityAnalysisService complexityAnalysisService,
        ISemanticSimilarityService semanticSimilarityService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Path to the file containing the target type.")] string filePath,
        [Description("The C# code snippet to add as a member.")] string codeSnippet,
        [Description("Optional file name hint for partial types. Use 'auto' to determine automatically.")] string fileNameHint = "auto",
        [Description("Suggest a line number to insert the member near. '-1' to determine automatically.")] int lineNumberHint = -1,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(codeSnippet, "codeSnippet", logger);
            codeSnippet = codeSnippet.TrimBackslash();

            if (!File.Exists(filePath)) {
                throw new McpException($"File not found: {filePath}");
            }

            logger.LogInformation("Executing '{AddMember}' for file: {FilePath}", nameof(AddMember), filePath);

            // Create a workspace for the file
            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try {
                if (document == null) {
                    throw new McpException($"File {filePath} not found in the project");
                }

                // Parse the code snippet
                MemberDeclarationSyntax? memberSyntax;
                try {
                    memberSyntax = SyntaxFactory.ParseMemberDeclaration(codeSnippet);
                    if (memberSyntax == null) {
                        throw new McpException("Failed to parse code snippet as a valid member declaration.");
                    }
                } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                    logger.LogError(ex, "Failed to parse code snippet as member declaration");
                    throw new McpException($"Invalid C# syntax in code snippet: {ex.Message}");
                }

                // Get the semantic model
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null) {
                    throw new McpException("Failed to get semantic model for the document");
                }

                // Find the target type in the document
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null) {
                    throw new McpException("Failed to get syntax root for the document");
                }

                // Extract member name from the syntax
                string memberName = GetMemberName(memberSyntax);

                // Find all type declarations in the file
                var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();
                if (!typeDeclarations.Any()) {
                    throw new McpException("No type declarations found in the file");
                }

                // For now, if there's only one type, use it. Otherwise, require more specific targeting
                TypeDeclarationSyntax targetTypeNode;
                INamedTypeSymbol targetTypeSymbol;

                if (typeDeclarations.Count == 1) {
                    targetTypeNode = typeDeclarations.First();
                    var symbol = semanticModel.GetDeclaredSymbol(targetTypeNode);
                    if (symbol is not INamedTypeSymbol namedTypeSymbol) {
                        throw new McpException($"Could not get type symbol for {targetTypeNode.Identifier.Text}");
                    }
                    targetTypeSymbol = namedTypeSymbol;
                } else {
                    // Multiple types - need to infer from context or fail
                    throw new McpException($"Multiple type declarations found in file. Please use the full AddMember tool with a specific FQN.");
                }

                // Check for duplicate members
                if (!IsMemberAllowed(targetTypeSymbol, memberSyntax, memberName, cancellationToken)) {
                    throw new McpException($"A member with the name '{memberName}' already exists in '{targetTypeSymbol.ToDisplayString()}'" +
                        (memberSyntax is MethodDeclarationSyntax ? " with the same parameter signature." : "."));
                }

                // Add the member using DocumentEditor directly
                var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

                // Format the new member with proper indentation
                var formattedMember = memberSyntax.NormalizeWhitespace();

                // Add the member to the type
                editor.AddMember(targetTypeNode, formattedMember);

                // Get the changed document and format it
                var changedDocument = editor.GetChangedDocument();
                var formattedDocument = await Formatter.FormatAsync(changedDocument, options: null, cancellationToken);

                // Apply changes to the workspace
                var newSolution = formattedDocument.Project.Solution;
                if (!workspace.TryApplyChanges(newSolution)) {
                    throw new McpException("Failed to apply changes to the workspace");
                }

                // Check for compilation errors
                var updatedDocument = workspace.CurrentSolution.GetDocument(document.Id);
                if (updatedDocument == null) {
                    throw new McpException("Failed to get updated document after applying changes");
                }

                var compilation = await updatedDocument.Project.GetCompilationAsync(cancellationToken);
                if (compilation == null) {
                    throw new McpException("Failed to get compilation for error checking");
                }

                var diagnostics = compilation.GetDiagnostics(cancellationToken)
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Take(10)
                    .ToList();

                string errorMessages = "";
                if (diagnostics.Any()) {
                    errorMessages = string.Join("\n", diagnostics.Select(d => $"- {d.GetMessage()}"));
                }

                // Perform complexity analysis if possible
                string analysisResults = string.Empty;
                var updatedSemanticModel = await updatedDocument.GetSemanticModelAsync(cancellationToken);
                if (updatedSemanticModel != null) {
                    var updatedRoot = await updatedDocument.GetSyntaxRootAsync(cancellationToken);
                    if (updatedRoot != null) {
                        var addedMemberNode = updatedRoot.DescendantNodes()
                            .OfType<MemberDeclarationSyntax>()
                            .Where(m => m is not NamespaceDeclarationSyntax && m is not FileScopedNamespaceDeclarationSyntax)
                            .FirstOrDefault(m => GetMemberName(m) == memberName && m.IsKind(memberSyntax.Kind()));

                        if (addedMemberNode != null) {
                            var addedSymbol = updatedSemanticModel.GetDeclaredSymbol(addedMemberNode);
                            if (addedSymbol != null) {
                                analysisResults = await MemberAnalysisHelper.AnalyzeAddedMemberAsync(
                                    addedSymbol, complexityAnalysisService, semanticSimilarityService, logger, cancellationToken);
                            }
                        }
                    }
                }

                string baseMessage = $"Successfully added member to {targetTypeSymbol.ToDisplayString()} in {filePath}.\n\n" +
                    (string.IsNullOrEmpty(errorMessages) ? "<errorCheck>No compilation issues detected.</errorCheck>" :
                    ($"<errorCheck>Compilation errors detected:\n{errorMessages}</errorCheck>\n\n" +
                    $"If you choose to fix these issues, you must use {ToolHelpers.SharpToolPrefix + nameof(OverwriteMember)} to replace the member with a new definition."));

                return string.IsNullOrWhiteSpace(analysisResults) ? baseMessage : $"{baseMessage}\n\n{analysisResults}";
            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(AddMember), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(UpdateToolDescription), Idempotent = false, Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("🎯 Safely update only the Description attribute of a SharpTool method without touching the method body")]
    public static async Task<string> UpdateToolDescription(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Path to the C# file containing the tool method")] string filePath,
        [Description("The method name to update (e.g., 'GetMembers')")] string methodName,
        [Description("The new description text (without [Description(...)] wrapper)")] string newDescription,
        CancellationToken cancellationToken = default)
    {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () =>
        {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, nameof(filePath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(methodName, nameof(methodName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(newDescription, nameof(newDescription), logger);

            if (!File.Exists(filePath))
            {
                throw new McpException($"File not found: {filePath}");
            }

            logger.LogInformation("Executing '{UpdateToolDescription}' for method: {MethodName} in file: {FilePath}",
                nameof(UpdateToolDescription), methodName, filePath);

            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try
            {
                if (document == null)
                {
                    throw new McpException($"File {filePath} not found in the project");
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null)
                {
                    throw new McpException("Failed to get syntax root for the document");
                }

                // Find the method by name
                var methodNode = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == methodName);

                if (methodNode == null)
                {
                    throw new McpException($"⚠️ Method '{methodName}' not found in file {filePath}");
                }

                // Check if it's a SharpTool method
                var hasSharpToolAttribute = methodNode.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(a => a.Name.ToString().Contains("McpServerTool"));

                if (!hasSharpToolAttribute)
                {
                    throw new McpException($"⚠️ Method '{methodName}' is not a SharpTool (missing [McpServerTool] attribute)");
                }

                // Find existing Description attribute
                AttributeSyntax? existingDescription = null;
                AttributeListSyntax? containingList = null;

                foreach (var attrList in methodNode.AttributeLists)
                {
                    var descAttr = attrList.Attributes
                        .FirstOrDefault(a => a.Name.ToString() == "Description");
                    
                    if (descAttr != null)
                    {
                        existingDescription = descAttr;
                        containingList = attrList;
                        break;
                    }
                }

                SyntaxNode newRoot;

                if (existingDescription != null)
                {
                    // Update existing Description attribute
                    var newDescriptionAttr = SyntaxFactory.Attribute(
                        SyntaxFactory.IdentifierName("Description"),
                        SyntaxFactory.AttributeArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.AttributeArgument(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal(newDescription)
                                    )
                                )
                            )
                        )
                    );

                    newRoot = root.ReplaceNode(existingDescription, newDescriptionAttr);
                }
                else
                {
                    // Add new Description attribute
                    var newAttrList = SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Attribute(
                                SyntaxFactory.IdentifierName("Description"),
                                SyntaxFactory.AttributeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.AttributeArgument(
                                            SyntaxFactory.LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                SyntaxFactory.Literal(newDescription)
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    );

                    var newMethodNode = methodNode.AddAttributeLists(newAttrList);
                    newRoot = root.ReplaceNode(methodNode, newMethodNode);
                }

                var newDocument = document.WithSyntaxRoot(newRoot);
                var formattedDocument = await modificationService.FormatDocumentAsync(newDocument, cancellationToken);
                
                if (!workspace.TryApplyChanges(formattedDocument.Project.Solution))
                {
                    throw new McpException("Failed to apply changes to the workspace");
                }

                return $"✅ Successfully updated Description attribute for method '{methodName}' in {filePath}";
            }
            finally
            {
                workspace.Dispose();
            }
        }, logger, nameof(UpdateToolDescription), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(OverwriteMember), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Replaces the definition of an existing member with new C# code in a file. Works without a pre-loaded solution.")]
    public static async Task<string> OverwriteMember(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ICodeAnalysisService codeAnalysisService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Path to the file containing the member to rewrite.")] string filePath,
        [Description("FQN of the member to rewrite.")] string fullyQualifiedMemberName,
        [Description("The new C# code for the member. Include attributes and XML documentation if present. To delete, use `// Delete {memberName}`.")] string newMemberCode,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedMemberName, "fullyQualifiedMemberName", logger);
            ErrorHandlingHelpers.ValidateStringParameter(newMemberCode, "newMemberCode", logger);
            newMemberCode = newMemberCode.TrimBackslash();

            if (!File.Exists(filePath)) {
                throw new McpException($"File not found: {filePath}");
            }

            logger.LogInformation("Executing '{OverwriteMember}' for: {MemberName} in {FilePath}",
                nameof(OverwriteMember), fullyQualifiedMemberName, filePath);

            // Create a workspace for the file
            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try {
                if (document == null) {
                    throw new McpException($"File {filePath} not found in the project");
                }

                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation == null) {
                    throw new McpException("Failed to get compilation");
                }

                // Find the symbol
                ISymbol? symbol = null;

                // Try to find the symbol by FQN in the compilation
                foreach (var syntaxTree in compilation.SyntaxTrees) {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync(cancellationToken);

                    // Search for type and member declarations
                    var declarations = root.DescendantNodes()
                        .Where(n => n is MemberDeclarationSyntax || n is TypeDeclarationSyntax);

                    foreach (var node in declarations) {
                        // Special handling for field declarations
                        if (node is FieldDeclarationSyntax fieldDecl) {
                            foreach (var variable in fieldDecl.Declaration.Variables) {
                                var fieldSymbol = semanticModel.GetDeclaredSymbol(variable);
                                if (fieldSymbol != null && IsSymbolMatch(fieldSymbol, fullyQualifiedMemberName)) {
                                    symbol = fieldSymbol;
                                    break;
                                }
                            }
                        } else {
                            var declaredSymbol = semanticModel.GetDeclaredSymbol(node);
                            if (declaredSymbol != null && IsSymbolMatch(declaredSymbol, fullyQualifiedMemberName)) {
                                symbol = declaredSymbol;
                                break;
                            }
                        }

                        if (symbol != null) break;
                    }

                    if (symbol != null) break;
                }

                if (symbol == null) {
                    throw new McpException($"Symbol '{fullyQualifiedMemberName}' not found");
                }

                if (!symbol.DeclaringSyntaxReferences.Any()) {
                    throw new McpException($"Symbol '{fullyQualifiedMemberName}' has no declaring syntax references.");
                }

                var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault(sr => sr.SyntaxTree.FilePath == filePath);
                if (syntaxRef == null) {
                    throw new McpException($"Symbol '{fullyQualifiedMemberName}' is not declared in file {filePath}");
                }

                var oldNode = await syntaxRef.GetSyntaxAsync(cancellationToken);

                if (oldNode is not MemberDeclarationSyntax && oldNode is not TypeDeclarationSyntax) {
                    throw new McpException($"Symbol '{fullyQualifiedMemberName}' does not represent a replaceable member or type.");
                }

                bool isDelete = newMemberCode.StartsWith("// Delete", StringComparison.OrdinalIgnoreCase);
                if (isDelete) {
                    var commentTrivia = SyntaxFactory.Comment(newMemberCode);
                    var emptyNode = SyntaxFactory.EmptyStatement()
                        .WithLeadingTrivia(commentTrivia)
                        .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));

                    // Use DocumentEditor to delete the node
                    var editor = await DocumentEditor.CreateAsync(document, cancellationToken);
                    editor.RemoveNode(oldNode);

                    var changedDocument = editor.GetChangedDocument();
                    var formattedDocument = await Formatter.FormatAsync(changedDocument, options: null, cancellationToken);
                    var newSolution = formattedDocument.Project.Solution;

                    if (!workspace.TryApplyChanges(newSolution)) {
                        throw new McpException("Failed to apply deletion changes to the workspace");
                    }

                    var updatedDocument = workspace.CurrentSolution.GetDocument(document.Id);
                    if (updatedDocument != null) {
                        var updatedCompilation = await updatedDocument.Project.GetCompilationAsync(cancellationToken);
                        var diagnostics = updatedCompilation?.GetDiagnostics(cancellationToken)
                            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                            .Take(10)
                            .ToList() ?? new List<Diagnostic>();

                        string errorMessages = diagnostics.Any()
                            ? string.Join("\n", diagnostics.Select(d => $"- {d.GetMessage()}"))
                            : "<errorCheck>No compilation issues detected.</errorCheck>";

                        return $"Successfully deleted symbol {fullyQualifiedMemberName}.\n\n{errorMessages}";
                    }
                    return $"Successfully deleted symbol {fullyQualifiedMemberName}";
                }

                SyntaxNode? newNode;
                try {
                    // PRE-PARSE VALIDATION: Check for syntax errors before processing
                    var syntaxTree = CSharpSyntaxTree.ParseText(newMemberCode);
                    var syntaxDiagnostics = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                    
                    if (syntaxDiagnostics.Any()) {
                        var errorMessages = string.Join("\n", syntaxDiagnostics.Select(d => $"  - {d.GetMessage()}"));
                        throw new McpException($"Syntax errors in provided code:\n{errorMessages}");
                    }
                    
                    var parsedCode = SyntaxFactory.ParseCompilationUnit(newMemberCode);
                    newNode = parsedCode.Members.FirstOrDefault();

                    if (newNode is null) {
                        throw new McpException("Failed to parse new code as a valid member or type declaration.");
                    }

                    // Validate type compatibility
                    if (oldNode is TypeDeclarationSyntax && newNode is not TypeDeclarationSyntax) {
                        throw new McpException($"The new code was parsed as a {newNode.Kind()}, but a TypeDeclaration was expected.");
                    } else if (oldNode is MemberDeclarationSyntax && oldNode is not TypeDeclarationSyntax && newNode is not MemberDeclarationSyntax) {
                        throw new McpException($"The new code was parsed as a {newNode.Kind()}, but a MemberDeclaration was expected.");
                    }

                    // CRITICAL SAFETY CHECK: Detect incomplete method specifications
                    if (oldNode is MethodDeclarationSyntax oldMethod && newNode is MethodDeclarationSyntax newMethod) {
                        // First, check if the old method had a body
                        bool oldHasBody = oldMethod.Body != null || oldMethod.ExpressionBody != null;
                        
                        // Check various indicators that the new method might be incomplete
                        bool newHasBody = newMethod.Body != null || newMethod.ExpressionBody != null;
                        bool newIsAbstract = newMethod.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword));
                        bool newIsPartial = newMethod.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
                        bool newIsExtern = newMethod.Modifiers.Any(m => m.IsKind(SyntaxKind.ExternKeyword));
                        
                        // Check if the method ends with a semicolon (could be abstract/interface/extern)
                        bool newEndsWithSemicolon = newMethod.SemicolonToken.IsKind(SyntaxKind.SemicolonToken);
                        
                        // ADDITIONAL CHECK: Analyze the raw text to detect incomplete specifications
                        string newMethodText = newMethod.ToFullString().Trim();
                        bool looksIncomplete = false;
                        
                        // Pattern detection for incomplete methods
                        if (!newHasBody && !newIsAbstract && !newIsPartial && !newIsExtern) {
                            // Check if it looks like just a signature (no braces at all)
                            if (!newMethodText.Contains("{") && !newMethodText.Contains("=>")) {
                                looksIncomplete = true;
                            }
                            // Check if it ends with ) or > (generic) without body
                            else if (newMethodText.TrimEnd().EndsWith(")") || newMethodText.TrimEnd().EndsWith(">")) {
                                looksIncomplete = true;
                            }
                        }

                        // If the old method had a body and the new one doesn't (and it's not explicitly abstract/partial/extern)
                        if (oldHasBody && (!newHasBody || looksIncomplete) && !newIsAbstract && !newIsPartial && !newIsExtern) {
                            // This is likely an incomplete method specification
                            var warningMessage = $"⚠️ SAFETY WARNING: The provided code appears to be an incomplete method specification:\n\n" +
                                               $"Provided code:\n```csharp\n{newMethodText}\n```\n\n" +
                                               $"This would DELETE the method body! If you want to:\n" +
                                               $"   - Update the complete method: Include the entire method with its body\n" +
                                               $"   - Update just the signature: Use UpdateToolDescription for descriptions\n" +
                                               $"   - Delete the method: Use '// Delete {oldMethod.Identifier.Text}'\n" +
                                               $"   - View current implementation: Use {ToolHelpers.SharpToolPrefix}GetMethodSignature\n\n" +
                                               $"💡 Safety tip: Always provide the complete method implementation when using OverwriteMember";

                            throw new McpException(warningMessage);
                        }

                        // Check for missing attributes that existed in the original
                        var oldAttributes = oldMethod.AttributeLists.SelectMany(al => al.Attributes).Select(a => a.Name.ToString()).ToHashSet();
                        var newAttributes = newMethod.AttributeLists.SelectMany(al => al.Attributes).Select(a => a.Name.ToString()).ToHashSet();

                        var missingAttributes = oldAttributes.Except(newAttributes).ToList();
                        if (missingAttributes.Any()) {
                            logger.LogWarning("Attributes will be removed: {Attributes}", string.Join(", ", missingAttributes));
                        }
                    }
                } catch (Exception ex) when (ex is not McpException && ex is not OperationCanceledException) {
                    logger.LogError(ex, "Failed to parse replacement code");
                    throw new McpException($"Invalid C# syntax in replacement code: {ex.Message}");
                }

                // Use DocumentEditor to replace the node
                var editor2 = await DocumentEditor.CreateAsync(document, cancellationToken);
                editor2.ReplaceNode(oldNode, newNode.WithTriviaFrom(oldNode));

                var changedDocument2 = editor2.GetChangedDocument();
                var formattedDocument2 = await Formatter.FormatAsync(changedDocument2, options: null, cancellationToken);
                var newSolution2 = formattedDocument2.Project.Solution;

                if (!workspace.TryApplyChanges(newSolution2)) {
                    throw new McpException("Failed to apply replacement changes to the workspace");
                }

                // Generate diff
                var diffResult = ContextInjectors.CreateCodeDiff(oldNode.ToFullString(), newNode.ToFullString());

                var finalDocument = workspace.CurrentSolution.GetDocument(document.Id);
                if (finalDocument != null) {
                    var finalCompilation = await finalDocument.Project.GetCompilationAsync(cancellationToken);
                    var diagnostics = finalCompilation?.GetDiagnostics(cancellationToken)
                        .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                        .Take(10)
                        .ToList() ?? new List<Diagnostic>();

                    string errorMessages = diagnostics.Any()
                        ? $"<errorCheck>Compilation errors detected:\n{string.Join("\n", diagnostics.Select(d => $"- {d.GetMessage()}"))}</errorCheck>"
                        : "<errorCheck>No compilation issues detected.</errorCheck>";

                    return $"Successfully replaced symbol {fullyQualifiedMemberName}.\n\n{diffResult}\n\n{errorMessages}";
                }

                return $"Successfully replaced symbol {fullyQualifiedMemberName}.\n\n{diffResult}";

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(OverwriteMember), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(UpdateParameterDescription), Idempotent = false, Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("🎯 Safely update only the Description attribute of a method parameter without changing the method signature")]
    public static async Task<string> UpdateParameterDescription(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Path to the C# file containing the method")] string filePath,
        [Description("The method name containing the parameter")] string methodName,
        [Description("The parameter name to update")] string parameterName,
        [Description("The new description text (without [Description(...)] wrapper) - TESTED VERSION")] string newDescription,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, nameof(filePath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(methodName, nameof(methodName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(parameterName, nameof(parameterName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(newDescription, nameof(newDescription), logger);

            if (!File.Exists(filePath)) {
                throw new McpException($"File not found: {filePath}");
            }

            logger.LogInformation("Executing '{UpdateParameterDescription}' for parameter: {ParameterName} in method: {MethodName} in file: {FilePath}",
                nameof(UpdateParameterDescription), parameterName, methodName, filePath);

            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try {
                if (document == null) {
                    throw new McpException($"File {filePath} not found in the project");
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null) {
                    throw new McpException("Failed to get syntax root for the document");
                }

                // Find the method by name
                var methodNode = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == methodName);

                if (methodNode == null) {
                    throw new McpException($"⚠️ Method '{methodName}' not found in file {filePath}");
                }

                // Find the parameter by name
                var parameterNode = methodNode.ParameterList.Parameters
                    .FirstOrDefault(p => p.Identifier.Text == parameterName);

                if (parameterNode == null) {
                    throw new McpException($"⚠️ Parameter '{parameterName}' not found in method '{methodName}'");
                }

                // Find existing Description attribute on the parameter
                AttributeSyntax? existingDescription = null;
                AttributeListSyntax? containingList = null;

                foreach (var attrList in parameterNode.AttributeLists) {
                    var descAttr = attrList.Attributes
                        .FirstOrDefault(a => a.Name.ToString() == "Description");

                    if (descAttr != null) {
                        existingDescription = descAttr;
                        containingList = attrList;
                        break;
                    }
                }

                SyntaxNode newRoot;

                if (existingDescription != null) {
                    // Update existing Description attribute
                    var newDescriptionAttr = SyntaxFactory.Attribute(
                        SyntaxFactory.IdentifierName("Description"),
                        SyntaxFactory.AttributeArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.AttributeArgument(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal(newDescription)
                                    )
                                )
                            )
                        )
                    );

                    newRoot = root.ReplaceNode(existingDescription, newDescriptionAttr);
                } else {
                    // Add new Description attribute
                    var newAttrList = SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Attribute(
                                SyntaxFactory.IdentifierName("Description"),
                                SyntaxFactory.AttributeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.AttributeArgument(
                                            SyntaxFactory.LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                SyntaxFactory.Literal(newDescription)
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    );

                    var newParameterNode = parameterNode.AddAttributeLists(newAttrList);
                    newRoot = root.ReplaceNode(parameterNode, newParameterNode);
                }

                var newDocument = document.WithSyntaxRoot(newRoot);
                var formattedDocument = await modificationService.FormatDocumentAsync(newDocument, cancellationToken);

                if (!workspace.TryApplyChanges(formattedDocument.Project.Solution)) {
                    throw new McpException("Failed to apply changes to the workspace");
                }

                return $"✅ Successfully updated Description attribute for parameter '{parameterName}' in method '{methodName}' in {filePath}";
            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(UpdateParameterDescription), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(RenameSymbol), Idempotent = true, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Renames a symbol and updates all references in a file. Works without a pre-loaded solution.")]
    public static async Task<string> RenameSymbol(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ICodeAnalysisService codeAnalysisService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Path to the file containing the symbol to rename.")] string filePath,
        [Description("The old name of the symbol.")] string oldName,
        [Description("The new name for the symbol.")] string newName,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(oldName, "oldName", logger);
            ErrorHandlingHelpers.ValidateStringParameter(newName, "newName", logger);

            if (!File.Exists(filePath)) {
                throw new McpException($"File not found: {filePath}");
            }

            // Validate that the new name is a valid C# identifier
            if (!IsValidCSharpIdentifier(newName)) {
                throw new McpException($"'{newName}' is not a valid C# identifier for renaming.");
            }

            logger.LogInformation("Executing '{RenameSymbol}' for {OldName} to {NewName} in {FilePath}",
                nameof(RenameSymbol), oldName, newName, filePath);

            // Create a workspace for the file
            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try {
                if (document == null) {
                    throw new McpException($"File {filePath} not found in the project");
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null) {
                    throw new McpException("Failed to get syntax root");
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null) {
                    throw new McpException("Failed to get semantic model");
                }

                // Find all symbols with the old name in the document
                var symbolsToRename = new List<ISymbol>();
                var nodes = root.DescendantNodes().Where(n =>
                    (n is IdentifierNameSyntax id && id.Identifier.Text == oldName) ||
                    (n is MemberDeclarationSyntax member && GetMemberName(member) == oldName));

                foreach (var node in nodes) {
                    var symbol = semanticModel.GetSymbolInfo(node).Symbol ?? semanticModel.GetDeclaredSymbol(node);
                    if (symbol != null && !symbol.IsImplicitlyDeclared && !symbolsToRename.Contains(symbol)) {
                        symbolsToRename.Add(symbol);
                    }
                }

                if (!symbolsToRename.Any()) {
                    throw new McpException($"No symbol named '{oldName}' found in file {filePath}");
                }

                // For simplicity, rename the first found symbol
                // In a more sophisticated implementation, we might want to handle multiple symbols
                var symbolToRename = symbolsToRename.First();

                // Perform the rename using Roslyn's Renamer API directly
                var currentSolution = workspace.CurrentSolution;
                var renameOptions = new SymbolRenameOptions();
                var newSolution = await Renamer.RenameSymbolAsync(currentSolution, symbolToRename, renameOptions, newName, cancellationToken);

                // Check if changes were made
                var changeset = newSolution.GetChanges(currentSolution);
                var changedDocumentCount = changeset.GetProjectChanges().Sum(p => p.GetChangedDocuments().Count());

                if (changedDocumentCount == 0) {
                    logger.LogWarning("Rename operation produced no changes");
                }

                if (!workspace.TryApplyChanges(newSolution)) {
                    throw new McpException("Failed to apply rename changes to the workspace");
                }

                // Check for compilation errors
                var updatedDocument = workspace.CurrentSolution.GetDocument(document.Id);
                if (updatedDocument != null) {
                    var compilation = await updatedDocument.Project.GetCompilationAsync(cancellationToken);
                    var diagnostics = compilation?.GetDiagnostics(cancellationToken)
                        .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                        .Take(10)
                        .ToList() ?? new List<Diagnostic>();

                    string errorMessages = diagnostics.Any()
                        ? $"<errorCheck>Compilation errors detected:\n{string.Join("\n", diagnostics.Select(d => $"- {d.GetMessage()}"))}</errorCheck>"
                        : "<errorCheck>No compilation issues detected.</errorCheck>";

                    return $"Symbol '{oldName}' successfully renamed to '{newName}' in {changedDocumentCount} document(s).\n\n{errorMessages}";
                }

                return $"Symbol '{oldName}' successfully renamed to '{newName}' in {changedDocumentCount} document(s).";

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(RenameSymbol), cancellationToken);
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(FindAndReplace), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Performs regex-based find and replace in a single file. Works without a pre-loaded solution.")]
    public static async Task<string> FindAndReplace(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        IDocumentOperationsService documentOperations,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Path to the file to perform find and replace in.")] string filePath,
        [Description("Regex pattern in multiline mode. Use `\\s*` for unknown indentation. Remember to escape for JSON.")] string regexPattern,
        [Description("Replacement text, which can include regex groups ($1, ${name}, etc.)")] string replacementText,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(regexPattern, "regexPattern", logger);

            if (!File.Exists(filePath)) {
                throw new McpException($"File not found: {filePath}");
            }

            // Create stateless document operations service with context directory
            var contextDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            var nullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<StatelessDocumentOperationsService>.Instance;
            var statelessDocOps = new StatelessDocumentOperationsService(nullLogger, contextDirectory);

            // Normalize newlines in pattern
            regexPattern = regexPattern
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", @"\n")
                .Replace(@"\r\n", @"\n")
                .Replace(@"\r", @"\n");

            logger.LogInformation("Executing '{FindAndReplace}' with pattern: '{Pattern}' in {FilePath}",
                nameof(FindAndReplace), regexPattern, filePath);

            // Validate the regex pattern
            try {
                _ = new Regex(regexPattern, RegexOptions.Multiline);
            } catch (ArgumentException ex) {
                throw new McpException($"Invalid regular expression pattern: {ex.Message}");
            }

            // Check path accessibility using stateless service
            var pathInfo = statelessDocOps.GetPathInfo(filePath);
            if (!pathInfo.IsWritable) {
                logger.LogWarning("File is not writable: {FilePath}, Reason: {Reason}",
                    filePath, pathInfo.WriteRestrictionReason ?? "Unknown");
                throw new McpException($"Cannot modify file '{filePath}': {pathInfo.WriteRestrictionReason ?? "File is outside solution directory or in a protected location"}");
            }

            // Check if it's a code file
            if (!statelessDocOps.IsCodeFile(filePath)) {
                // Handle non-code file using stateless operations
                var (originalContent, _) = await statelessDocOps.ReadFileAsync(filePath, false, cancellationToken);
                var regex = new Regex(regexPattern, RegexOptions.Multiline);
                string newContent = regex.Replace(originalContent.NormalizeEndOfLines(), replacementText);

                if (newContent != originalContent) {
                    await statelessDocOps.WriteFileAsync(filePath, newContent, true, cancellationToken);
                    var diff = ContextInjectors.CreateCodeDiff(originalContent, newContent);
                    return $"Successfully replaced pattern in non-code file {filePath}.\n\n{diff}";
                } else {
                    throw new McpException($"No matches found for pattern '{regexPattern}' in file '{filePath}', or replacement produced identical text.");
                }
            }

            // Handle code file using Roslyn
            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try {
                if (document == null) {
                    throw new McpException($"File {filePath} not found in the project");
                }

                // Get the original text for comparison
                var originalText = await document.GetTextAsync(cancellationToken);
                var originalSolution = workspace.CurrentSolution;

                // Perform find and replace directly
                var regex = new Regex(regexPattern, RegexOptions.Multiline);
                var sourceText = await document.GetTextAsync(cancellationToken);
                var newText = regex.Replace(sourceText.ToString(), replacementText);

                if (newText == sourceText.ToString()) {
                    throw new McpException($"No matches found for pattern '{regexPattern}' in file '{filePath}', or replacement produced identical text.");
                }

                // Apply the changes using SourceText
                var newSourceText = SourceText.From(newText);
                var newDocument = document.WithText(newSourceText);
                var newSolution = newDocument.Project.Solution;

                if (!workspace.TryApplyChanges(newSolution)) {
                    throw new McpException("Failed to apply find and replace changes to the workspace");
                }

                // Generate diff
                var originalDoc = originalSolution.GetDocument(document.Id);
                var newDoc = workspace.CurrentSolution.GetDocument(document.Id);

                if (originalDoc != null && newDoc != null) {
                    var originalTextContent = await originalDoc.GetTextAsync(cancellationToken);
                    var newTextContent = await newDoc.GetTextAsync(cancellationToken);
                    var diff = ContextInjectors.CreateCodeDiff(originalTextContent.ToString(), newTextContent.ToString());

                    // Check for compilation errors
                    var compilation = await newDoc.Project.GetCompilationAsync(cancellationToken);
                    var diagnostics = compilation?.GetDiagnostics(cancellationToken)
                        .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                        .Take(10)
                        .ToList() ?? new List<Diagnostic>();

                    string errorMessages = diagnostics.Any()
                        ? $"<errorCheck>Compilation errors detected:\n{string.Join("\n", diagnostics.Select(d => $"- {d.GetMessage()}"))}</errorCheck>"
                        : "<errorCheck>No compilation issues detected.</errorCheck>";

                    return $"Successfully replaced pattern '{regexPattern}' with '{replacementText}' in {filePath}.\n\n{errorMessages}\n\n{diff}";
                }

                return $"Successfully replaced pattern '{regexPattern}' with '{replacementText}' in {filePath}.";

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(FindAndReplace), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(MoveMember), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Moves a member (property, field, method, nested type, etc.) from one type/namespace to another. Requires the solution path since member moves need full solution context.")]
    public static async Task<string> MoveMember(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        IFuzzyFqnLookupService fuzzyLookup,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Path to the solution (.sln) file containing both the source member and destination type.")] string solutionPath,
        [Description("FQN of the member to move.")] string fullyQualifiedMemberName,
        [Description("FQN of the destination type or namespace where the member should be moved.")] string fullyQualifiedDestinationTypeOrNamespaceName,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(solutionPath, nameof(solutionPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedMemberName, nameof(fullyQualifiedMemberName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedDestinationTypeOrNamespaceName, nameof(fullyQualifiedDestinationTypeOrNamespaceName), logger);

            // Validate solution file exists and has correct extension
            if (!File.Exists(solutionPath)) {
                throw new McpException($"Solution file not found: {solutionPath}");
            }
            if (!Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase)) {
                throw new McpException($"File '{solutionPath}' is not a .sln file.");
            }

            logger.LogInformation("Executing '{MoveMember}' moving {MemberName} to {DestinationName} in solution {SolutionPath}",
                nameof(MoveMember), fullyQualifiedMemberName, fullyQualifiedDestinationTypeOrNamespaceName, solutionPath);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(solutionPath);

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

                // Find the source member symbol using fuzzy lookup
                var tempSolutionManager = new StatelessSolutionManager(solution);
                var sourceMemberMatches = await fuzzyLookup.FindMatchesAsync(fullyQualifiedMemberName, tempSolutionManager, cancellationToken);
                var sourceMemberMatch = sourceMemberMatches.FirstOrDefault();
                if (sourceMemberMatch == null) {
                    throw new McpException($"No symbol found matching '{fullyQualifiedMemberName}'.");
                }

                var sourceMemberSymbol = sourceMemberMatch.Symbol;
                if (sourceMemberSymbol == null) {
                    throw new McpException($"Could not find symbol '{fullyQualifiedMemberName}' in the workspace.");
                }

                if (sourceMemberSymbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol or INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate })) {
                    throw new McpException($"Symbol '{fullyQualifiedMemberName}' is not a movable member type. Only fields, properties, methods, events, and nested types can be moved.");
                }

                // Find the destination symbol
                var destinationMatches = await fuzzyLookup.FindMatchesAsync(fullyQualifiedDestinationTypeOrNamespaceName, tempSolutionManager, cancellationToken);
                var destinationMatch = destinationMatches.FirstOrDefault();
                if (destinationMatch == null) {
                    throw new McpException($"No symbol found matching '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
                }

                var destinationSymbol = destinationMatch.Symbol;

                if (destinationSymbol is not (INamedTypeSymbol or INamespaceSymbol)) {
                    throw new McpException($"Destination '{fullyQualifiedDestinationTypeOrNamespaceName}' must be a type or namespace.");
                }

                // Get syntax references
                var sourceSyntaxRef = sourceMemberSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (sourceSyntaxRef == null) {
                    throw new McpException($"Could not find syntax reference for member '{fullyQualifiedMemberName}'.");
                }

                var sourceMemberNode = await sourceSyntaxRef.GetSyntaxAsync(cancellationToken);
                if (sourceMemberNode is not MemberDeclarationSyntax memberDeclaration) {
                    throw new McpException($"Source member '{fullyQualifiedMemberName}' is not a valid member declaration.");
                }

                Document currentSourceDocument = ToolHelpers.GetDocumentFromSyntaxNodeOrThrow(solution, sourceMemberNode);
                Document destinationDocument;
                INamedTypeSymbol? destinationTypeSymbol = null;
                INamespaceSymbol? destinationNamespaceSymbol = null;

                if (destinationSymbol is INamedTypeSymbol typeSym) {
                    destinationTypeSymbol = typeSym;
                    var destSyntaxRef = typeSym.DeclaringSyntaxReferences.FirstOrDefault();
                    if (destSyntaxRef == null) {
                        throw new McpException($"Could not find syntax reference for destination type '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
                    }
                    var destNode = await destSyntaxRef.GetSyntaxAsync(cancellationToken);
                    destinationDocument = ToolHelpers.GetDocumentFromSyntaxNodeOrThrow(solution, destNode);
                } else if (destinationSymbol is INamespaceSymbol nsSym) {
                    destinationNamespaceSymbol = nsSym;
                    var projectForDestination = solution.GetDocument(currentSourceDocument.Id)!.Project;
                    var existingDoc = await FindExistingDocumentWithNamespaceAsync(projectForDestination, nsSym, cancellationToken);
                    if (existingDoc != null) {
                        destinationDocument = existingDoc;
                    } else {
                        var newDoc = await CreateDocumentForNamespaceAsync(projectForDestination, nsSym, cancellationToken);
                        destinationDocument = newDoc;
                        solution = newDoc.Project.Solution; // Update solution after adding a document
                    }
                } else {
                    throw new McpException("Invalid destination symbol type.");
                }

                if (currentSourceDocument.Id == destinationDocument.Id && sourceMemberSymbol.ContainingSymbol.Equals(destinationSymbol, SymbolEqualityComparer.Default)) {
                    throw new McpException($"Source and destination are the same. Member '{fullyQualifiedMemberName}' is already in '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
                }

                string memberName = GetMemberName(memberDeclaration);
                INamedTypeSymbol? updatedDestinationTypeSymbol = null;

                if (destinationTypeSymbol != null) {
                    // Re-resolve destinationTypeSymbol from the potentially updated solution
                    var destinationDocumentFromCurrentSolution = solution.GetDocument(destinationDocument.Id)
                        ?? throw new McpException($"Destination document '{destinationDocument.FilePath}' not found in current solution for symbol re-resolution.");
                    var tempDestSymbol = await SymbolFinder.FindSymbolAtPositionAsync(destinationDocumentFromCurrentSolution, destinationTypeSymbol.Locations.First().SourceSpan.Start, cancellationToken);
                    updatedDestinationTypeSymbol = tempDestSymbol as INamedTypeSymbol;
                    if (updatedDestinationTypeSymbol == null) {
                        throw new McpException($"Could not re-resolve destination type symbol '{destinationTypeSymbol.ToDisplayString()}' in the current solution state.");
                    }
                }

                if (updatedDestinationTypeSymbol != null && !IsMemberAllowed(updatedDestinationTypeSymbol, memberDeclaration, memberName, cancellationToken)) {
                    throw new McpException($"A member with the name '{memberName}' already exists in destination type '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
                }

                try {
                    var actualDestinationDocument = solution.GetDocument(destinationDocument.Id)
                        ?? throw new McpException($"Destination document '{destinationDocument.FilePath}' not found in current solution before adding member.");

                    if (updatedDestinationTypeSymbol != null) {
                        solution = await modificationService.AddMemberAsync(actualDestinationDocument.Id, updatedDestinationTypeSymbol, memberDeclaration, -1, cancellationToken);
                    } else {
                        if (destinationNamespaceSymbol == null) throw new McpException("Destination namespace symbol is null when expected for namespace move.");
                        solution = await AddMemberToNamespaceAsync(actualDestinationDocument, destinationNamespaceSymbol, memberDeclaration, modificationService, cancellationToken);
                    }

                    // Re-acquire source document and node from the updated solution
                    var sourceDocumentInCurrentSolution = solution.GetDocument(currentSourceDocument.Id)
                        ?? throw new McpException("Source document not found in current solution after adding member to destination.");
                    var syntaxRootOfSourceInCurrentSolution = await sourceDocumentInCurrentSolution.GetSyntaxRootAsync(cancellationToken)
                        ?? throw new McpException("Could not get syntax root for source document in current solution.");

                    // Find the source member node in the updated tree
                    var sourceMemberNodeInCurrentTree = syntaxRootOfSourceInCurrentSolution.FindNode(sourceMemberNode.Span, findInsideTrivia: true, getInnermostNodeForTie: true);
                    if (sourceMemberNodeInCurrentTree == null || !(sourceMemberNodeInCurrentTree is MemberDeclarationSyntax)) {
                        // Fallback: Try to find by kind and name
                        sourceMemberNodeInCurrentTree = syntaxRootOfSourceInCurrentSolution
                            .DescendantNodes()
                            .OfType<MemberDeclarationSyntax>()
                            .FirstOrDefault(m => m.Kind() == memberDeclaration.Kind() && GetMemberName(m) == memberName);

                        if (sourceMemberNodeInCurrentTree == null) {
                            throw new McpException($"Failed to re-locate source member node '{memberName}' for removal after modifications.");
                        }
                    }

                    solution = await RemoveMemberFromParentAsync(sourceDocumentInCurrentSolution, sourceMemberNodeInCurrentTree, modificationService, cancellationToken);

                    // Apply changes
                    if (!workspace.TryApplyChanges(solution)) {
                        throw new McpException("Failed to apply changes to the workspace.");
                    }

                    // Get final documents for error checking
                    var finalSolution = workspace.CurrentSolution;
                    var finalSourceDocument = finalSolution.GetDocument(currentSourceDocument.Id);
                    var finalDestinationDocument = finalSolution.GetDocument(destinationDocument.Id);

                    StringBuilder errorBuilder = new StringBuilder();

                    // Since we're stateless, we'll do a simple compilation check
                    if (finalSourceDocument != null) {
                        var sourceSemanticModel = await finalSourceDocument.GetSemanticModelAsync(cancellationToken);
                        var sourceDiagnostics = sourceSemanticModel?.GetDiagnostics()
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Take(5);

                        if (sourceDiagnostics?.Any() == true) {
                            errorBuilder.AppendLine($"Compilation errors in source file {finalSourceDocument.FilePath}:");
                            foreach (var diag in sourceDiagnostics) {
                                errorBuilder.AppendLine($"  - {diag.GetMessage()}");
                            }
                        }
                    }

                    if (finalDestinationDocument != null && finalDestinationDocument.Id != finalSourceDocument?.Id) {
                        var destSemanticModel = await finalDestinationDocument.GetSemanticModelAsync(cancellationToken);
                        var destDiagnostics = destSemanticModel?.GetDiagnostics()
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Take(5);

                        if (destDiagnostics?.Any() == true) {
                            errorBuilder.AppendLine($"Compilation errors in destination file {finalDestinationDocument.FilePath}:");
                            foreach (var diag in destDiagnostics) {
                                errorBuilder.AppendLine($"  - {diag.GetMessage()}");
                            }
                        }
                    }

                    var sourceFilePathDisplay = finalSourceDocument?.FilePath ?? currentSourceDocument.FilePath ?? "unknown source file";
                    var destinationFilePathDisplay = finalDestinationDocument?.FilePath ?? destinationDocument.FilePath ?? "unknown destination file";

                    var locationInfo = sourceFilePathDisplay == destinationFilePathDisplay
                        ? $"within {sourceFilePathDisplay}"
                        : $"from {sourceFilePathDisplay} to {destinationFilePathDisplay}";

                    var errorInfo = errorBuilder.Length > 0 ? $"\n\nCompilation status:\n{errorBuilder}" : "";

                    return $"Successfully moved member '{memberName}' to '{fullyQualifiedDestinationTypeOrNamespaceName}' {locationInfo}.{errorInfo}";

                } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                    logger.LogError(ex, "Failed to move member {MemberName} to {DestinationName}", fullyQualifiedMemberName, fullyQualifiedDestinationTypeOrNamespaceName);
                    throw new McpException($"Failed to move member '{fullyQualifiedMemberName}' to '{fullyQualifiedDestinationTypeOrNamespaceName}': {ex.Message}", ex);
                }

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(MoveMember), cancellationToken);
    }

    #endregion
}