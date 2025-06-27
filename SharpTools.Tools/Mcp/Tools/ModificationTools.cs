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

using SharpTools.Tools.Mcp.Models;
using SharpTools.Tools.Mcp.Helpers;

namespace SharpTools.Tools.Mcp.Tools;

// Marker class for ILogger<T> category specific to ModificationTools
public class ModificationToolsLogCategory { }

[McpServerToolType]
public static class ModificationTools {
    private static string GetMemberTypeName(SyntaxNode node) {
        return node switch {
            MethodDeclarationSyntax => "Method",
            PropertyDeclarationSyntax => "Property",
            FieldDeclarationSyntax => "Field",
            ConstructorDeclarationSyntax => "Constructor",
            DestructorDeclarationSyntax => "Destructor",
            OperatorDeclarationSyntax => "Operator",
            ConversionOperatorDeclarationSyntax => "ConversionOperator",
            EventDeclarationSyntax => "Event",
            EventFieldDeclarationSyntax => "EventField",
            IndexerDeclarationSyntax => "Indexer",
            EnumDeclarationSyntax => "Enum",
            ClassDeclarationSyntax => "Class",
            StructDeclarationSyntax => "Struct",
            InterfaceDeclarationSyntax => "Interface",
            RecordDeclarationSyntax => "Record",
            DelegateDeclarationSyntax => "Delegate",
            NamespaceDeclarationSyntax => "Namespace",
            FileScopedNamespaceDeclarationSyntax => "FileScopedNamespace",
            _ => node.GetType().Name.Replace("DeclarationSyntax", "").Replace("Syntax", "")
        };
    }
    
    private static string GetMemberSignature(MemberDeclarationSyntax member) {
        return member switch {
            MethodDeclarationSyntax method => $"{method.Modifiers} {method.ReturnType} {method.Identifier}{method.ParameterList}".Trim(),
            PropertyDeclarationSyntax property => $"{property.Modifiers} {property.Type} {property.Identifier}".Trim(),
            FieldDeclarationSyntax field => $"{field.Modifiers} {field.Declaration}".Trim(),
            _ => member.ToString().Split('\n')[0].Trim() // First line for others
        };
    }
    
    private static string GetAccessibility(MemberDeclarationSyntax member) {
        var modifiers = member.Modifiers;
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return "Public";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword))) return "Private";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword))) {
            if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return "ProtectedInternal";
            return "Protected";
        }
        if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return "Internal";
        return "Private"; // Default for C#
    }
    
    private static string GetReturnType(MemberDeclarationSyntax member) {
        return member switch {
            MethodDeclarationSyntax method => method.ReturnType?.ToString() ?? "void",
            PropertyDeclarationSyntax property => property.Type?.ToString() ?? "unknown",
            FieldDeclarationSyntax field => field.Declaration.Type?.ToString() ?? "unknown",
            EventDeclarationSyntax evt => evt.Type?.ToString() ?? "unknown",
            EventFieldDeclarationSyntax evtField => evtField.Declaration.Type?.ToString() ?? "unknown",
            _ => ""
        };
    }
    
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
        // For methods with parameter specification (e.g., "Process(int)" or "Process(System.Int32)")
        if (symbol is IMethodSymbol methodSymbol && fullyQualifiedName.Contains("(")) {
            // Try to match with parameter types
            var methodSignature = BuildMethodSignature(methodSymbol);
            if (methodSignature == fullyQualifiedName)
                return true;
            
            // Also try with fully qualified parameter types
            var fullMethodSignature = BuildMethodSignature(methodSymbol, useFullyQualifiedTypes: true);
            if (fullMethodSignature == fullyQualifiedName)
                return true;
            
            // Special handling for C# keyword types (System.String -> string, etc.)
            var normalizedFullyQualifiedName = fullyQualifiedName
                .Replace("System.String", "string")
                .Replace("System.Int32", "int")
                .Replace("System.Int64", "long")
                .Replace("System.Boolean", "bool")
                .Replace("System.Double", "double")
                .Replace("System.Single", "float")
                .Replace("System.Decimal", "decimal")
                .Replace("System.Byte", "byte")
                .Replace("System.Char", "char")
                .Replace("System.Object", "object");
            
            // Try matching with normalized names
            if (methodSignature == normalizedFullyQualifiedName)
                return true;
            if (fullMethodSignature == normalizedFullyQualifiedName)
                return true;
            
            // Try matching with containing type
            var fqnWithType = BuildFullyQualifiedName(symbol) + GetMethodParameters(methodSymbol);
            if (fqnWithType == fullyQualifiedName || fqnWithType == normalizedFullyQualifiedName)
                return true;
            
            // Also try with fully qualified parameter types for FQN
            var fqnWithFullParams = BuildFullyQualifiedName(symbol) + GetMethodParameters(methodSymbol, useFullyQualifiedTypes: true);
            if (fqnWithFullParams == fullyQualifiedName || fqnWithFullParams == normalizedFullyQualifiedName)
                return true;
                
            // Try partial matches (e.g., "TestClass.Process(string)" for "TestProject.TestClass.Process")
            var parts = BuildFullyQualifiedName(symbol).Split('.');
            for (int i = 0; i < parts.Length - 1; i++) {
                var partialFqn = string.Join(".", parts.Skip(i));
                var partialWithParams = partialFqn + GetMethodParameters(methodSymbol);
                if (partialWithParams == fullyQualifiedName || partialWithParams == normalizedFullyQualifiedName)
                    return true;
                    
                var partialWithFullParams = partialFqn + GetMethodParameters(methodSymbol, useFullyQualifiedTypes: true);
                if (partialWithFullParams == fullyQualifiedName || partialWithFullParams == normalizedFullyQualifiedName)
                    return true;
            }
        }
        
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
    
    // Helper method to build method signature with parameter types
    private static string BuildMethodSignature(IMethodSymbol method, bool useFullyQualifiedTypes = false) {
        var signature = method.Name;
        var parameters = method.Parameters.Select(p => {
            if (useFullyQualifiedTypes) {
                return p.Type.ToDisplayString();
            } else {
                // Use simple type names
                var typeName = p.Type.Name;
                if (p.Type is IArrayTypeSymbol arrayType) {
                    typeName = arrayType.ElementType.Name + "[]";
                }
                return typeName;
            }
        });
        
        signature += "(" + string.Join(", ", parameters) + ")";
        return signature;
    }
    
    // Helper method to get method parameters for matching
    private static string GetMethodParameters(IMethodSymbol method, bool useFullyQualifiedTypes = false) {
        var parameters = method.Parameters.Select(p => {
            if (useFullyQualifiedTypes) {
                return p.Type.ToDisplayString();
            } else {
                // Use simple type names
                var typeName = p.Type.Name;
                if (p.Type is IArrayTypeSymbol arrayType) {
                    typeName = arrayType.ElementType.Name + "[]";
                }
                return typeName;
            }
        });
        return "(" + string.Join(", ", parameters) + ")";
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

    // Helper method to adjust node indentation
    private static SyntaxNode AdjustNodeIndentation(SyntaxNode node, SyntaxTrivia indentationTrivia) {
        var lines = node.ToFullString().Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var adjustedLines = new List<string>();
        
        for (int i = 0; i < lines.Length; i++) {
            if (i == 0) {
                // First line: use as is (it will receive the indentation from replacement)
                adjustedLines.Add(lines[i]);
            } else if (!string.IsNullOrWhiteSpace(lines[i])) {
                // Other non-empty lines: add the base indentation
                adjustedLines.Add(indentationTrivia.ToString() + lines[i].TrimStart());
            } else {
                // Empty lines: keep as is
                adjustedLines.Add(lines[i]);
            }
        }
        
        var adjustedText = string.Join(Environment.NewLine, adjustedLines);
        var adjustedTree = SyntaxFactory.ParseSyntaxTree(adjustedText);
        var adjustedRoot = adjustedTree.GetRoot();
        
        // Try to find the corresponding node in the adjusted tree
        if (node is MemberDeclarationSyntax) {
            var member = adjustedRoot.DescendantNodes().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            if (member != null) return member;
        } else if (node is TypeDeclarationSyntax) {
            var type = adjustedRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (type != null) return type;
        }
        
        // Fallback: parse as member declaration
        return SyntaxFactory.ParseMemberDeclaration(adjustedText) ?? node;
    }
    // Helper method to check if a string is a valid C# identifier
    private static bool IsValidCSharpIdentifier(string name) {
        return SyntaxFacts.IsValidIdentifier(name);
    }
    
    // Helper method to apply access modifiers from old member to new member if missing
    private static MemberDeclarationSyntax ApplyAccessModifiersIfMissing(MemberDeclarationSyntax oldMember, MemberDeclarationSyntax newMember) {
        var oldModifiers = oldMember.Modifiers;
        var newModifiers = newMember.Modifiers;
        
        // Check if new member has any access modifier
        bool hasAccessModifier = newModifiers.Any(m => 
            m.IsKind(SyntaxKind.PublicKeyword) ||
            m.IsKind(SyntaxKind.PrivateKeyword) ||
            m.IsKind(SyntaxKind.ProtectedKeyword) ||
            m.IsKind(SyntaxKind.InternalKeyword));
        
        // If no access modifier, copy from old member
        if (!hasAccessModifier) {
            var accessModifiers = oldModifiers.Where(m =>
                m.IsKind(SyntaxKind.PublicKeyword) ||
                m.IsKind(SyntaxKind.PrivateKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword) ||
                m.IsKind(SyntaxKind.InternalKeyword)).ToList();
            
            if (accessModifiers.Any()) {
                // Create new modifiers with proper spacing
                var newModifierTokens = new List<SyntaxToken>();
                
                // Add access modifiers first
                foreach (var modifier in accessModifiers) {
                    var modToken = SyntaxFactory.Token(modifier.Kind());
                    // Add trailing space to the modifier
                    modToken = modToken.WithTrailingTrivia(SyntaxFactory.Space);
                    newModifierTokens.Add(modToken);
                }
                
                // Add existing modifiers (if any)
                foreach (var existingModifier in newModifiers) {
                    newModifierTokens.Add(existingModifier);
                }
                
                // Apply the new modifiers without affecting leading trivia
                newMember = newMember.WithModifiers(SyntaxFactory.TokenList(newModifierTokens));
            }
        }
        
        // Also copy other important modifiers if missing
        var importantModifiers = new[] {
            SyntaxKind.StaticKeyword,
            SyntaxKind.VirtualKeyword,
            SyntaxKind.OverrideKeyword,
            SyntaxKind.AbstractKeyword,
            SyntaxKind.SealedKeyword,
            SyntaxKind.AsyncKeyword,
            SyntaxKind.ReadOnlyKeyword,
            SyntaxKind.PartialKeyword,
            SyntaxKind.ExternKeyword
        };
        
        foreach (var modifierKind in importantModifiers) {
            if (oldModifiers.Any(m => m.IsKind(modifierKind)) && !newModifiers.Any(m => m.IsKind(modifierKind))) {
                var modToken = SyntaxFactory.Token(modifierKind).WithTrailingTrivia(SyntaxFactory.Space);
                
                // Find the correct position to insert the modifier
                var existingModifiers = newMember.Modifiers.ToList();
                var insertIndex = 0;
                
                // Insert after access modifiers but before other modifiers
                for (int i = 0; i < existingModifiers.Count; i++) {
                    if (existingModifiers[i].IsKind(SyntaxKind.PublicKeyword) ||
                        existingModifiers[i].IsKind(SyntaxKind.PrivateKeyword) ||
                        existingModifiers[i].IsKind(SyntaxKind.ProtectedKeyword) ||
                        existingModifiers[i].IsKind(SyntaxKind.InternalKeyword)) {
                        insertIndex = i + 1;
                    }
                }
                
                existingModifiers.Insert(insertIndex, modToken);
                newMember = newMember.WithModifiers(SyntaxFactory.TokenList(existingModifiers));
            }
        }
        
        return newMember;
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
    [Description("🔍 .NET専用 - .cs/.csprojファイルのみ対応。クラスにメンバー（メソッド、プロパティ、フィールド）を追加")]
    public static async Task<object> AddMember(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ICodeAnalysisService codeAnalysisService,
        IComplexityAnalysisService complexityAnalysisService,
        ISemanticSimilarityService semanticSimilarityService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("C#ファイル(.cs)の絶対パス")] string filePath,
        [Description("Complete member definition(s). Can contain multiple members separated by proper C# syntax")] string memberCode,
        [Description("Target class name ('auto' for single-class files)")] string className = "auto",
        [Description("Insertion position ('end', 'beginning', line number, or 'after:MemberName')")] string insertPosition = "end",
        [Description("File name hint for partial types ('auto' to determine automatically)")] string fileNameHint = "auto",
        [Description("追加するusing文のリスト（重複は自動的にスキップされます）")] string[]? appendUsings = null,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, nameof(AddMember), logger);
            
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(memberCode, "memberCode", logger);
            memberCode = memberCode.TrimBackslash();

            if (!File.Exists(filePath)) {
                throw new McpException($"📁 ファイルが見つかりません: {filePath}\n💡 確認方法:\n• パスが正しいかを確認（絶対パス推奨）\n• {ToolHelpers.SharpToolPrefix}ReadTypesFromRoslynDocument で構造を確認");
            }

            logger.LogInformation("Executing '{AddMember}' for file: {FilePath}", nameof(AddMember), filePath);

            // Create a workspace for the file
            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try {
                if (document == null) {
                    throw new McpException($"File {filePath} not found in the project");
                }

                // Parse the member code - support multiple members
                List<MemberDeclarationSyntax> memberSyntaxList = new();
                try {
                    // Always try to parse as multiple members by wrapping in a class
                    var wrappedCode = $"class TempClass {{ {memberCode} }}";
                    var compilationUnit = SyntaxFactory.ParseCompilationUnit(wrappedCode);
                    var tempClass = compilationUnit.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                    
                    if (tempClass != null && tempClass.Members.Any()) {
                        memberSyntaxList.AddRange(tempClass.Members);
                    } else {
                        // If that fails, try to parse as a single member
                        var singleMember = SyntaxFactory.ParseMemberDeclaration(memberCode);
                        if (singleMember != null) {
                            memberSyntaxList.Add(singleMember);
                        }
                    }
                    
                    if (!memberSyntaxList.Any()) {
                        throw new McpException("Failed to parse member code as valid member declaration(s).");
                    }
                } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                    logger.LogError(ex, "Failed to parse member code");
                    throw new McpException($"Invalid C# syntax in member code: {ex.Message}");
                }

                // Get the semantic model
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null) {
                    throw new McpException("Failed to get semantic model for the document");
                }

                // Capture before operation diagnostics
                var beforeCompilation = await project.GetCompilationAsync(cancellationToken);
                var beforeDiagnostics = beforeCompilation != null 
                    ? await DiagnosticHelper.CaptureDiagnosticsAsync(
                        beforeCompilation, 
                        "操作前から存在していた診断", 
                        cancellationToken)
                    : null;

                // Find the target type in the document
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null) {
                    throw new McpException("Failed to get syntax root for the document");
                }

                // We'll process member names individually later
                // string memberName = GetMemberName(memberSyntax);

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
                    // Multiple types - check if className is provided
                    if (string.IsNullOrWhiteSpace(className) || className == "auto") {
                        var typeNames = string.Join(", ", typeDeclarations.Select(t => t.Identifier.Text));
                        throw new McpException($"複数の型定義が見つかりました: {typeNames}\n対応方法:\n• className パラメータで対象の型を指定してください\n• 例: className: \"MyClass\"\n• {ToolHelpers.SharpToolPrefix}ReadTypesFromRoslynDocument で型一覧を確認");
                    }
                    
                    // Find the target type by name
                    targetTypeNode = typeDeclarations.FirstOrDefault(t => t.Identifier.Text == className)!;
                    if (targetTypeNode == null) {
                        var availableTypes = string.Join(", ", typeDeclarations.Select(t => t.Identifier.Text));
                        throw new McpException($"指定された型 '{className}' が見つかりません\n利用可能な型: {availableTypes}");
                    }
                    
                    var symbol = semanticModel.GetDeclaredSymbol(targetTypeNode);
                    if (symbol is not INamedTypeSymbol namedTypeSymbol) {
                        throw new McpException($"Could not get type symbol for {targetTypeNode.Identifier.Text}");
                    }
                    targetTypeSymbol = namedTypeSymbol;
                }

                // Check for duplicate members for each member
                foreach (var memberSyntax in memberSyntaxList) {
                    var memberName = GetMemberName(memberSyntax);
                    if (!IsMemberAllowed(targetTypeSymbol, memberSyntax, memberName, cancellationToken)) {
                        throw new McpException($"A member with the name '{memberName}' already exists in '{targetTypeSymbol.ToDisplayString()}'" +
                            (memberSyntax is MethodDeclarationSyntax ? " with the same parameter signature." : "."));
                    }
                }

                // Add the members using DocumentEditor
                var editor = await DocumentEditor.CreateAsync(document, cancellationToken);
                var addedMembers = new List<AddedMember>();
                var originalRoot = await document.GetSyntaxRootAsync(cancellationToken);
                
                // Process each member
                foreach (var memberSyntax in memberSyntaxList) {
                    var memberName = GetMemberName(memberSyntax);
                    var formattedMember = memberSyntax.NormalizeWhitespace();

                    // Special handling for interface members
                    bool wasBodyRemoved = false;
                    if (targetTypeNode is InterfaceDeclarationSyntax && memberSyntax is MethodDeclarationSyntax methodDecl) {
                        // Interface methods should not have bodies
                        if (methodDecl.Body != null || methodDecl.ExpressionBody != null) {
                            // Remove body and expression body, add semicolon
                            formattedMember = methodDecl
                                .WithBody(null)
                                .WithExpressionBody(null)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                                .NormalizeWhitespace();
                            wasBodyRemoved = true;
                            logger.LogWarning("Method body was removed because interface methods cannot have implementations");
                        }
                    }

                    // Add the member to the type
                    editor.AddMember(targetTypeNode, formattedMember);
                    
                    // Create AddedMember info (we'll update with more details after changes are applied)
                    var addedMember = new AddedMember {
                        Name = memberName,
                        Type = GetMemberTypeName(memberSyntax),
                        Signature = GetMemberSignature(memberSyntax),
                        Accessibility = GetAccessibility(memberSyntax),
                        ReturnType = GetReturnType(memberSyntax)
                    };
                    
                    // Add parameters for methods
                    if (memberSyntax is MethodDeclarationSyntax method) {
                        foreach (var param in method.ParameterList.Parameters) {
                            addedMember.Parameters.Add(new ParameterDetail {
                                Name = param.Identifier.Text,
                                Type = param.Type?.ToString() ?? "unknown",
                                IsOptional = param.Default != null,
                                DefaultValue = param.Default?.Value?.ToString()
                            });
                        }
                    }
                    
                    addedMembers.Add(addedMember);
                }

                // Get the changed document first (with members added)
                var changedDocument = editor.GetChangedDocument();

                // Handle using directives if specified
                var addedUsings = new List<string>();
                var usingConflicts = new List<string>();

                if (appendUsings != null && appendUsings.Length > 0) {
                    // Create a new editor for the document with members already added
                    var usingEditor = await DocumentEditor.CreateAsync(changedDocument, cancellationToken);
                    var currentRoot = await changedDocument.GetSyntaxRootAsync(cancellationToken);
                    
                    if (currentRoot is CompilationUnitSyntax compilationUnit) {
                        // Get existing using directives
                        var existingUsings = compilationUnit.Usings.Select(u => u.Name?.ToString()).Where(u => u != null).ToHashSet();
                        
                        // Process each using to add
                        foreach (var usingToAdd in appendUsings) {
                            if (string.IsNullOrWhiteSpace(usingToAdd)) continue;
                            
                            // Normalize the using (remove "using" keyword and semicolon if present)
                            var normalizedUsing = usingToAdd.Trim();
                            if (normalizedUsing.StartsWith("using ")) {
                                normalizedUsing = normalizedUsing.Substring(6).Trim();
                            }
                            if (normalizedUsing.EndsWith(";")) {
                                normalizedUsing = normalizedUsing.TrimEnd(';').Trim();
                            }
                            
                            // Check if already exists
                            if (existingUsings.Contains(normalizedUsing)) {
                                usingConflicts.Add(normalizedUsing);
                                logger.LogDebug("Using directive '{Using}' already exists, skipping", normalizedUsing);
                            } else {
                                // Create the using directive
                                var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(normalizedUsing))
                                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                                
                                // Add using in sorted order
                                var inserted = false;
                                for (int i = 0; i < compilationUnit.Usings.Count; i++) {
                                    var existingUsing = compilationUnit.Usings[i];
                                    if (string.Compare(normalizedUsing, existingUsing.Name?.ToString(), StringComparison.Ordinal) < 0) {
                                        usingEditor.InsertBefore(existingUsing, usingDirective);
                                        inserted = true;
                                        break;
                                    }
                                }
                                
                                if (!inserted) {
                                    if (compilationUnit.Usings.Any()) {
                                        usingEditor.InsertAfter(compilationUnit.Usings.Last(), usingDirective);
                                    } else {
                                        // No existing usings - add before first member or extern alias
                                        var firstNode = compilationUnit.ChildNodes().FirstOrDefault(n => 
                                            n is MemberDeclarationSyntax || n is ExternAliasDirectiveSyntax);
                                        if (firstNode != null) {
                                            usingEditor.InsertBefore(firstNode, usingDirective);
                                        }
                                    }
                                }
                                
                                addedUsings.Add(normalizedUsing);
                                logger.LogInformation("Added using directive: {Using}", normalizedUsing);
                            }
                        }
                        
                        // Get the document with usings added
                        if (addedUsings.Any()) {
                            changedDocument = usingEditor.GetChangedDocument();
                        }
                    }
                }

                // Format the final document
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

                // Get updated document for analysis
                var updatedSemanticModel = await updatedDocument.GetSemanticModelAsync(cancellationToken);
                var updatedRoot = await updatedDocument.GetSyntaxRootAsync(cancellationToken);
                
                // Update line numbers for added members
                if (updatedRoot != null) {
                    var updatedTypeNode = updatedRoot.DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault(t => t.Identifier.Text == targetTypeNode.Identifier.Text);
                    
                    if (updatedTypeNode != null) {
                        foreach (var addedMember in addedMembers) {
                            var memberNode = updatedTypeNode.Members
                                .FirstOrDefault(m => GetMemberName(m) == addedMember.Name);
                            
                            if (memberNode != null) {
                                var lineSpan = memberNode.GetLocation().GetLineSpan();
                                addedMember.InsertedAtLine = lineSpan.StartLinePosition.Line + 1;
                            }
                        }
                    }
                }

                // Create statistics
                var statistics = new MemberStatistics {
                    TotalAdded = addedMembers.Count,
                    MethodCount = addedMembers.Count(m => m.Type == "Method"),
                    PropertyCount = addedMembers.Count(m => m.Type == "Property"),
                    FieldCount = addedMembers.Count(m => m.Type == "Field"),
                    EventCount = addedMembers.Count(m => m.Type == "Event" || m.Type == "EventField")
                };
                
                // Return structured result
                var result = new AddMemberResult {
                    Success = true,
                    TargetClass = targetTypeSymbol.ToDisplayString(),
                    FilePath = filePath,
                    AddedMembers = addedMembers,
                    Statistics = statistics,
                    InsertPosition = insertPosition,
                    Message = $"{statistics.TotalAdded}個のメンバーを正常に追加しました",
                    AddedUsings = addedUsings,
                    UsingConflicts = usingConflicts
                };

                // Add detailed compilation status
                if (beforeCompilation != null && compilation != null) {
                    result.CompilationStatus = await DiagnosticHelper.CreateDetailedStatusAsync(
                        beforeCompilation,
                        compilation,
                        cancellationToken);
                }

                // Note: Complexity analysis removed from AddMember result as per new specification

                return result;

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(AddMember), cancellationToken);
    }


    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(OverwriteMember), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("🔍 .NET専用 - .cs/.csprojファイルのみ対応。[DESTRUCTIVE] 既存メンバーを完全に置換")]
    public static async Task<object> OverwriteMember(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ICodeAnalysisService codeAnalysisService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("C#ファイル(.cs)の絶対パス")] string filePath,
        [Description("Member name or FQN to replace")] string memberNameOrFqn,
        [Description("New C# code for the member (include attributes and XML docs if present)")] string newMemberCode,
        [Description("危険な操作の実行確認（正確に \"Yes\" と入力）")] string? userConfirmResponse = null,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<object, ModificationToolsLogCategory>(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, nameof(OverwriteMember), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(memberNameOrFqn, "memberNameOrFqn", logger);
            ErrorHandlingHelpers.ValidateStringParameter(newMemberCode, "newMemberCode", logger);
            newMemberCode = newMemberCode.TrimBackslash();

            if (!File.Exists(filePath)) {
                throw new McpException($"📁 ファイルが見つかりません: {filePath}\n💡 確認方法:\n• パスが正しいかを確認（絶対パス推奨）\n• {ToolHelpers.SharpToolPrefix}ReadTypesFromRoslynDocument で構造を確認");
            }

            logger.LogInformation("Executing '{OverwriteMember}' for: {MemberName} in {FilePath}",
                nameof(OverwriteMember), memberNameOrFqn, filePath);

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

                // Capture before operation diagnostics
                var beforeDiagnostics = await DiagnosticHelper.CaptureDiagnosticsAsync(
                    compilation, 
                    "操作前から存在していた診断", 
                    cancellationToken);

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
                                if (fieldSymbol != null && IsSymbolMatch(fieldSymbol, memberNameOrFqn)) {
                                    symbol = fieldSymbol;
                                    break;
                                }
                            }
                        } else {
                            var declaredSymbol = semanticModel.GetDeclaredSymbol(node);
                            if (declaredSymbol != null && IsSymbolMatch(declaredSymbol, memberNameOrFqn)) {
                                symbol = declaredSymbol;
                                break;
                            }
                        }

                        if (symbol != null) break;
                    }

                    if (symbol != null) break;
                }

                if (symbol == null) {
                    throw new McpException($"Symbol '{memberNameOrFqn}' not found");
                }

                if (!symbol.DeclaringSyntaxReferences.Any()) {
                    throw new McpException($"Symbol '{memberNameOrFqn}' has no declaring syntax references.");
                }

                var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault(sr => sr.SyntaxTree.FilePath == filePath);
                if (syntaxRef == null) {
                    throw new McpException($"Symbol '{memberNameOrFqn}' is not declared in file {filePath}");
                }

                var oldNode = await syntaxRef.GetSyntaxAsync(cancellationToken);

                if (oldNode is not MemberDeclarationSyntax && oldNode is not TypeDeclarationSyntax) {
                    throw new McpException($"Symbol '{memberNameOrFqn}' does not represent a replaceable member or type.");
                }

                // Check for dangerous operation before proceeding
                if (userConfirmResponse?.Trim().Equals("Yes", StringComparison.Ordinal) != true) {
                    var dangerousResult = DangerousOperationDetector.CreateDangerousOperationResult(
                        null, // No pattern for OverwriteMember
                        1,    // Single member overwrite
                        1,    // Single file affected
                        isDestructive: true);

                    // For OverwriteMember, always require confirmation since it's irreversible
                    dangerousResult.DangerousOperationDetected = true;
                    dangerousResult.RiskLevel = RiskLevels.High;
                    dangerousResult.RiskType = RiskTypes.DestructiveOperation;
                    dangerousResult.Message = $"🚨 破壊的操作: '{memberNameOrFqn}' を完全に置き換えます。元のコードは失われます。";
                    dangerousResult.Recommendation = "元のコードをGetMethodSignatureで確認してから操作を実行してください";
                    dangerousResult.RequiredConfirmationText = "Yes";
                    dangerousResult.ConfirmationPrompt = "この操作を実行するには、userConfirmResponse パラメータに正確に \"Yes\" と入力してください";
                    dangerousResult.Details = new DangerousOperationDetails {
                        Pattern = null,
                        EstimatedReplacements = 1,
                        AffectedFiles = 1,
                        RiskFactors = new List<string> { RiskTypes.DestructiveOperation }
                    };
                    
                    return dangerousResult;
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

                    }
                    
                    var deleteResult = new OverwriteMemberResult
                    {
                        Success = true,
                        Message = $"シンボルを正常に削除しました: {memberNameOrFqn}",
                        MemberName = memberNameOrFqn,
                        MemberType = "削除済み",
                        TargetClass = symbol?.ContainingType?.Name ?? symbol?.Name ?? "不明",
                        FilePath = filePath
                    };
                    
                    if (updatedDocument != null) {
                        var updatedCompilation = await updatedDocument.Project.GetCompilationAsync(cancellationToken);
                        
                        // Add detailed compilation status
                        if (updatedCompilation != null) {
                            deleteResult.CompilationStatus = await DiagnosticHelper.CreateDetailedStatusAsync(
                                compilation,
                                updatedCompilation,
                                cancellationToken);
                        }
                    }
                    
                    deleteResult.Notes.Add("メンバーが削除されました");
                    return deleteResult;
                }

                SyntaxNode? newNode;
                try {
                    // Check if the code starts with XML documentation comments to avoid duplication issues
                    var trimmedCode = newMemberCode.TrimStart();
                    bool startsWithXmlDoc = trimmedCode.StartsWith("///");
                    
                    // Parse the member code and handle access modifiers properly
                    if (oldNode is MemberDeclarationSyntax oldMemberForAccess) {
                        // Extract all modifiers from old member (access, static, etc.)
                        var oldModifiers = oldMemberForAccess.Modifiers;
                        var accessModifiers = oldModifiers
                            .Where(m => m.IsKind(SyntaxKind.PublicKeyword) ||
                                       m.IsKind(SyntaxKind.PrivateKeyword) ||
                                       m.IsKind(SyntaxKind.ProtectedKeyword) ||
                                       m.IsKind(SyntaxKind.InternalKeyword))
                            .ToList();
                        
                        var hasStaticModifier = oldModifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                        
                        // Check if the new code lacks access modifiers
                        // More robust check - parse the code to see if it has modifiers
                        var tempParsed = SyntaxFactory.ParseMemberDeclaration(newMemberCode);
                        bool lacksAccessModifier = tempParsed != null && 
                                                  !tempParsed.Modifiers.Any(m => 
                                                      m.IsKind(SyntaxKind.PublicKeyword) ||
                                                      m.IsKind(SyntaxKind.PrivateKeyword) ||
                                                      m.IsKind(SyntaxKind.ProtectedKeyword) ||
                                                      m.IsKind(SyntaxKind.InternalKeyword));
                        
                        // If old had access modifier and new lacks it, prepend it to the method signature
                        if (accessModifiers.Any() && lacksAccessModifier) {
                            // Build modifier string - only add modifiers that were in the original
                            var modifierString = string.Join(" ", accessModifiers.Select(m => m.Text));
                            
                            // Only preserve static if the new code doesn't already specify it and the old had it
                            bool newHasStatic = tempParsed != null && tempParsed.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                            if (hasStaticModifier && !newHasStatic) {
                                // We need to handle static separately to ensure correct order
                                // The correct order is: access modifier, then static, then return type
                                // But we'll add it with the signature rewriting below
                            }
                            
                            // Find where to insert the access modifier (after XML docs)
                            var lines = newMemberCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            var modifiedLines = new List<string>();
                            bool foundSignature = false;
                            
                            foreach (var line in lines) {
                                var trimmedLine = line.TrimStart();
                                if (!foundSignature && !trimmedLine.StartsWith("///") && !trimmedLine.StartsWith("//") && 
                                    !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("[")) {
                                    // This is the first non-comment, non-attribute line - insert access modifier here
                                    var leadingWhitespace = line.Substring(0, line.Length - line.TrimStart().Length);
                                    
                                    // Check if we need to add static as well
                                    var fullModifierString = modifierString;
                                    if (hasStaticModifier && !newHasStatic && !trimmedLine.Contains("static")) {
                                        fullModifierString += " static";
                                    }
                                    
                                    modifiedLines.Add(leadingWhitespace + fullModifierString + " " + trimmedLine);
                                    foundSignature = true;
                                } else {
                                    modifiedLines.Add(line);
                                }
                            }
                            
                            newMemberCode = string.Join(Environment.NewLine, modifiedLines);
                        } else if (hasStaticModifier && tempParsed != null && !tempParsed.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) {
                            // Handle the case where we have static but no access modifier changes needed
                            // This is for cases where the new code has access modifiers but is missing static
                            var lines = newMemberCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            var modifiedLines = new List<string>();
                            bool foundSignature = false;
                            
                            foreach (var line in lines) {
                                var trimmedLine = line.TrimStart();
                                if (!foundSignature && !trimmedLine.StartsWith("///") && !trimmedLine.StartsWith("//") && 
                                    !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("[")) {
                                    // Find where to insert static (after access modifiers)
                                    var leadingWhitespace = line.Substring(0, line.Length - line.TrimStart().Length);
                                    
                                    // Check if line starts with access modifier
                                    if (trimmedLine.StartsWith("public ") || trimmedLine.StartsWith("private ") || 
                                        trimmedLine.StartsWith("protected ") || trimmedLine.StartsWith("internal ")) {
                                        // Find the end of access modifier
                                        var spaceIndex = trimmedLine.IndexOf(' ');
                                        var beforeModifier = trimmedLine.Substring(0, spaceIndex + 1);
                                        var afterModifier = trimmedLine.Substring(spaceIndex + 1);
                                        modifiedLines.Add(leadingWhitespace + beforeModifier + "static " + afterModifier);
                                    } else {
                                        // No access modifier, add static at the beginning
                                        modifiedLines.Add(leadingWhitespace + "static " + trimmedLine);
                                    }
                                    foundSignature = true;
                                } else {
                                    modifiedLines.Add(line);
                                }
                            }
                            
                            newMemberCode = string.Join(Environment.NewLine, modifiedLines);
                        }
                    }
                    
                    // First try parsing as a member declaration (methods, properties, fields, etc.)
                    // This is the primary parsing method for most cases
                    newNode = SyntaxFactory.ParseMemberDeclaration(newMemberCode);
                    
                    if (newNode is null) {
                        // If ParseMemberDeclaration fails, check if it's a type declaration
                        // Try parsing the code to check for syntax errors first
                        var syntaxTree = CSharpSyntaxTree.ParseText(newMemberCode);
                        var syntaxDiagnostics = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

                        if (syntaxDiagnostics.Any()) {
                            var errorMessages = string.Join("\n", syntaxDiagnostics.Select(d => $"  - {d.GetMessage()}"));
                            
                            // Provide specific guidance for common errors
                            string additionalGuidance = "";
                            if (errorMessages.Contains("expected") || errorMessages.Contains("Invalid token")) {
                                additionalGuidance = "\n\n💡 よくある問題:\n" +
                                                   "• メソッド全体を提供してください（修飾子からボディまで）\n" +
                                                   "• 例: public void MyMethod() { /* implementation */ }\n" +
                                                   "• XMLコメントがある場合は含めてください\n" +
                                                   "• 不完全なコードは受け付けません";
                            }
                            
                            throw new McpException($"構文エラーが検出されました:\n{errorMessages}{additionalGuidance}");
                        }
                        
                        // If no syntax errors, it might be a type declaration
                        // Try parsing as a compilation unit (for namespace-level types)
                        var parsedCode = SyntaxFactory.ParseCompilationUnit(newMemberCode);
                        newNode = parsedCode.Members.FirstOrDefault();
                        
                        if (newNode is null) {
                            throw new McpException("コードを有効なメンバーまたは型宣言として解析できませんでした。\n💡 ヒント:\n• 完全なメソッド定義を提供してください\n• XMLコメントがある場合は含めてください\n• 例:\n/// <summary>\n/// メソッドの説明\n/// </summary>\npublic void MyMethod()\n{\n    // 実装\n}");
                        }
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

                // Don't apply access modifiers automatically if the parsed node already has proper structure
                // The access modifier handling is now done during string manipulation before parsing
                
                // Use DocumentEditor to replace the node
                var editor2 = await DocumentEditor.CreateAsync(document, cancellationToken);
                
                // Replace node without preserving all trivia
                editor2.ReplaceNode(oldNode, newNode);

                var changedDocument2 = editor2.GetChangedDocument();
                
                // Format the document to fix indentation
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

                }
                
                var result = new OverwriteMemberResult
                {
                    Success = true,
                    Message = $"シンボルを正常に置換しました: {memberNameOrFqn}",
                    MemberName = memberNameOrFqn,
                    MemberType = GetMemberTypeName(newNode),
                    TargetClass = symbol?.ContainingType?.Name ?? symbol?.Name ?? "不明",
                    FilePath = filePath
                };
                
                if (finalDocument != null) {
                    var finalCompilation = await finalDocument.Project.GetCompilationAsync(cancellationToken);
                    
                    // Add detailed compilation status
                    if (finalCompilation != null) {
                        result.CompilationStatus = await DiagnosticHelper.CreateDetailedStatusAsync(
                            compilation,
                            finalCompilation,
                            cancellationToken);
                    }
                }
                
                // Add notes about the operation
                if (oldNode is MemberDeclarationSyntax oldMember && newNode is MemberDeclarationSyntax newMember) {
                    var oldModifiers = oldMember.Modifiers.Select(m => m.Text).ToList();
                    var newModifiers = newMember.Modifiers.Select(m => m.Text).ToList();
                    
                    if (!oldModifiers.SequenceEqual(newModifiers)) {
                        result.Notes.Add($"修飾子が変更されました: {string.Join(" ", oldModifiers)} → {string.Join(" ", newModifiers)}");
                    }
                }
                
                return result;

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(OverwriteMember), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(UpdateParameterDescription), Idempotent = false, Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("🔍 .NET専用 - .cs/.csprojファイルのみ対応。メソッドパラメータのDescription属性を更新")]
    public static async Task<string> UpdateParameterDescription(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("C#ファイル(.cs)の絶対パス")] string filePath,
        [Description("Method name containing the parameter")] string methodName,
        [Description("Parameter name to update")] string parameterName,
        [Description("New description text (without [Description(...)] wrapper)")] string newDescription,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, nameof(UpdateParameterDescription), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(filePath, nameof(filePath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(methodName, nameof(methodName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(parameterName, nameof(parameterName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(newDescription, nameof(newDescription), logger);

            if (!File.Exists(filePath)) {
                throw new McpException($"📁 ファイルが見つかりません: {filePath}\n💡 確認方法:\n• パスが正しいかを確認（絶対パス推奨）\n• {ToolHelpers.SharpToolPrefix}ReadTypesFromRoslynDocument で構造を確認");
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
                
                // Check if System.ComponentModel using is present
                var compilationUnit = newRoot as CompilationUnitSyntax;
                if (compilationUnit != null) {
                    var hasComponentModelUsing = compilationUnit.Usings.Any(u => 
                        u.Name?.ToString() == "System.ComponentModel");
                    
                    if (!hasComponentModelUsing) {
                        // Add using directive
                        var newUsing = SyntaxFactory.UsingDirective(
                            SyntaxFactory.ParseName("System.ComponentModel"))
                            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                        
                        compilationUnit = compilationUnit.AddUsings(newUsing);
                        newDocument = newDocument.WithSyntaxRoot(compilationUnit);
                    }
                }
                
                var formattedDocument = await modificationService.FormatDocumentAsync(newDocument, cancellationToken);

                if (!workspace.TryApplyChanges(formattedDocument.Project.Solution)) {
                    throw new McpException("Failed to apply changes to the workspace");
                }

                return $"✅ Successfully updated Description attribute for parameter '{parameterName}' in method '{methodName}' in {filePath}\n💡 注: using System.ComponentModel; が自動追加されました（必要な場合）";
            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(UpdateParameterDescription), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(RenameSymbol), Idempotent = true, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("🔍 .NET専用 - .cs/.csprojファイルのみ対応。シンボルの名前を変更し、すべての参照を更新")]
    public static async Task<object> RenameSymbol(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        ICodeAnalysisService codeAnalysisService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("C#ファイル(.cs)の絶対パス")] string filePath,
        [Description("The old name of the symbol.")] string oldName,
        [Description("The new name for the symbol.")] string newName,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, nameof(RenameSymbol), logger);
            
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(oldName, "oldName", logger);
            ErrorHandlingHelpers.ValidateStringParameter(newName, "newName", logger);

            if (!File.Exists(filePath)) {
                throw new McpException($"📁 ファイルが見つかりません: {filePath}\n💡 確認方法:\n• パスが正しいかを確認（絶対パス推奨）\n• {ToolHelpers.SharpToolPrefix}ReadTypesFromRoslynDocument で構造を確認");
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

                // Capture before operation diagnostics
                var beforeCompilation = await project.GetCompilationAsync(cancellationToken);
                var beforeDiagnostics = beforeCompilation != null 
                    ? await DiagnosticHelper.CaptureDiagnosticsAsync(
                        beforeCompilation, 
                        "操作前から存在していた診断", 
                        cancellationToken)
                    : null;

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

                // Get changed files
                var changedFiles = new List<string>();
                foreach (var projectChange in changeset.GetProjectChanges()) {
                    foreach (var docId in projectChange.GetChangedDocuments()) {
                        var doc = newSolution.GetDocument(docId);
                        if (doc?.FilePath != null) {
                            changedFiles.Add(doc.FilePath);
                        }
                    }
                }

                // Determine symbol type
                string symbolType = symbolToRename.Kind.ToString();
                if (symbolToRename is IMethodSymbol) symbolType = "Method";
                else if (symbolToRename is IPropertySymbol) symbolType = "Property";
                else if (symbolToRename is IFieldSymbol) symbolType = "Field";
                else if (symbolToRename is INamedTypeSymbol) symbolType = "Type";
                else if (symbolToRename is INamespaceSymbol) symbolType = "Namespace";
                else if (symbolToRename is IParameterSymbol) symbolType = "Parameter";
                else if (symbolToRename is ILocalSymbol) symbolType = "Local";

                // Count total references (approximate)
                var totalReferences = nodes.Count();

                // Create structured result
                var result = new RenameSymbolResult {
                    Success = true,
                    Message = $"シンボル '{oldName}' を '{newName}' に正常にリネームしました",
                    OldName = oldName,
                    NewName = newName,
                    SymbolType = symbolType,
                    ChangedFileCount = changedDocumentCount,
                    ChangedFiles = changedFiles,
                    TotalReferences = totalReferences,
                    FilePath = filePath
                };

                // Check for compilation errors
                var updatedDocument = workspace.CurrentSolution.GetDocument(document.Id);
                if (updatedDocument != null) {
                    var finalCompilation = await updatedDocument.Project.GetCompilationAsync(cancellationToken);
                    var diagnostics = finalCompilation?.GetDiagnostics(cancellationToken)
                        .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error || d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                        .ToList() ?? new List<Diagnostic>();

                    // Add detailed compilation status
                    if (beforeCompilation != null && finalCompilation != null) {
                        result.CompilationStatus = await DiagnosticHelper.CreateDetailedStatusAsync(
                            beforeCompilation,
                            finalCompilation,
                            cancellationToken);
                    }
                }

                return result;

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(RenameSymbol), cancellationToken);
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(FindAndReplace), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("🔍 .NET専用 - .cs/.csprojファイルのみ対応。正規表現による検索置換を実行")]
    public static async Task<object> FindAndReplace(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        IDocumentOperationsService documentOperations,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("C#ファイル(.cs)の絶対パス")] string filePath,
        [Description("Regex pattern in multiline mode. Use `\\s*` for unknown indentation. Remember to escape for JSON.")] string regexPattern,
        [Description("Replacement text, which can include regex groups ($1, ${name}, etc.)")] string replacementText,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, nameof(FindAndReplace), logger);
            
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(regexPattern, "regexPattern", logger);

            if (!File.Exists(filePath)) {
                throw new McpException($"📁 ファイルが見つかりません: {filePath}\n💡 確認方法:\n• パスが正しいかを確認（絶対パス推奨）\n• {ToolHelpers.SharpToolPrefix}ReadTypesFromRoslynDocument で構造を確認");
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
                    var matchCount = regex.Matches(originalContent).Count;
                    
                    return new FindAndReplaceResult {
                        Success = true,
                        Message = $"{matchCount}件の置換を正常に実行しました (非コードファイル)",
                        ReplacementCount = matchCount,
                        AffectedMembers = new List<string>(), // Non-code files don't have members
                        Diff = diff,
                        FilePath = filePath
                    };
                } else {
                    // Check if pattern exists in file
                    var matchCount = regex.Matches(originalContent).Count;
                    if (matchCount == 0) {
                        throw new McpException($"❌ パターンが見つかりません: '{regexPattern}'\n" +
                                             $"📁 ファイル: {filePath}\n" +
                                             $"💡 確認事項:\n" +
                                             $"• 正規表現パターンが正しいか確認\n" +
                                             $"• 大文字・小文字の区別を確認\n" + 
                                             $"• エスケープが必要な文字（.[]()など）を確認");
                    } else {
                        throw new McpException($"⚠️ {matchCount}件のマッチが見つかりましたが、置換後のテキストが同じです\n" +
                                             $"💡 置換テキストが元のテキストと同じ可能性があります");
                    }
                }
            }

            // Handle code file using Roslyn
            var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

            try {
                if (document == null) {
                    throw new McpException($"File {filePath} not found in the project");
                }

                // Capture before operation diagnostics
                var beforeCompilation = await project.GetCompilationAsync(cancellationToken);
                var beforeDiagnostics = beforeCompilation != null 
                    ? await DiagnosticHelper.CaptureDiagnosticsAsync(
                        beforeCompilation, 
                        "操作前から存在していた診断", 
                        cancellationToken)
                    : null;

                // Get the original text for comparison
                var originalText = await document.GetTextAsync(cancellationToken);
                var originalSolution = workspace.CurrentSolution;

                // Perform find and replace directly
                var regex = new Regex(regexPattern, RegexOptions.Multiline);
                var sourceText = await document.GetTextAsync(cancellationToken);
                var newText = regex.Replace(sourceText.ToString(), replacementText);

                if (newText == sourceText.ToString()) {
                    // Check if pattern exists in file
                    var matchCount = regex.Matches(sourceText.ToString()).Count;
                    if (matchCount == 0) {
                        throw new McpException($"❌ パターンが見つかりません: '{regexPattern}'\n" +
                                             $"📁 ファイル: {filePath}\n" +
                                             $"💡 確認事項:\n" +
                                             $"• 正規表現パターンが正しいか確認\n" +
                                             $"• 大文字・小文字の区別を確認\n" + 
                                             $"• エスケープが必要な文字（.[]()など）を確認\n" +
                                             $"• {ToolHelpers.SharpToolPrefix}GetMembers でファイル内容を確認");
                    } else {
                        throw new McpException($"⚠️ {matchCount}件のマッチが見つかりましたが、置換後のテキストが同じです\n" +
                                             $"💡 置換テキストが元のテキストと同じ可能性があります");
                    }
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

                    var matchCount = regex.Matches(originalTextContent.ToString()).Count;

                    // Get affected members
                    var affectedMembers = new List<string>();
                    var syntaxRoot = await newDoc.GetSyntaxRootAsync(cancellationToken);
                    if (syntaxRoot != null) {
                        // Find members that contain replacements
                        var modifiedPositions = new List<int>();
                        var match = regex.Match(originalTextContent.ToString());
                        while (match.Success) {
                            modifiedPositions.Add(match.Index);
                            match = match.NextMatch();
                        }

                        foreach (var pos in modifiedPositions) {
                            var token = syntaxRoot.FindToken(pos);
                            var memberNode = token.Parent?.AncestorsAndSelf()
                                .FirstOrDefault(n => n is MemberDeclarationSyntax);
                            if (memberNode is MemberDeclarationSyntax member) {
                                var memberName = GetMemberName(member);
                                if (!affectedMembers.Contains(memberName)) {
                                    affectedMembers.Add(memberName);
                                }
                            }
                        }
                    }

                    // Create structured result
                    var result = new FindAndReplaceResult {
                        Success = true,
                        Message = $"{matchCount}件の置換を正常に実行しました",
                        ReplacementCount = matchCount,
                        AffectedMembers = affectedMembers,
                        Diff = diff,
                        FilePath = filePath
                    };

                    // Check for compilation errors
                    var compilation = await newDoc.Project.GetCompilationAsync(cancellationToken);
                    var diagnostics = compilation?.GetDiagnostics(cancellationToken)
                        .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error || d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                        .ToList() ?? new List<Diagnostic>();

                    // Add detailed compilation status
                    if (beforeCompilation != null && compilation != null) {
                        result.CompilationStatus = await DiagnosticHelper.CreateDetailedStatusAsync(
                            beforeCompilation,
                            compilation,
                            cancellationToken);
                    }

                    return result;
                }

                return new FindAndReplaceResult {
                    Success = true,
                    Message = "置換を実行しました",
                    ReplacementCount = 0,
                    FilePath = filePath
                };

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(FindAndReplace), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(MoveMember), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("🔍 .NET専用 - .sln/.csprojファイルのみ対応。メンバーを別の型/名前空間に移動")]
    public static async Task<object> MoveMember(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeModificationService modificationService,
        IFuzzyFqnLookupService fuzzyLookup,
        ILogger<ModificationToolsLogCategory> logger,
        [Description(".NETソリューション(.sln)ファイルのパス")] string solutionPath,
        [Description("FQN of the member to move")] string memberNameOrFqn,
        [Description("FQN of the destination type or namespace")] string destinationTypeOrNamespace,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(solutionPath, nameof(MoveMember), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(solutionPath, nameof(solutionPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(memberNameOrFqn, nameof(memberNameOrFqn), logger);
            ErrorHandlingHelpers.ValidateStringParameter(destinationTypeOrNamespace, nameof(destinationTypeOrNamespace), logger);

            // Validate solution file exists and has correct extension
            if (!File.Exists(solutionPath)) {
                throw new McpException($"Solution file not found: {solutionPath}");
            }
            if (!Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase)) {
                throw new McpException($"File '{solutionPath}' is not a .sln file.");
            }

            logger.LogInformation("Executing '{MoveMember}' moving {MemberName} to {DestinationName} in solution {SolutionPath}",
                nameof(MoveMember), memberNameOrFqn, destinationTypeOrNamespace, solutionPath);

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

                // Capture before operation diagnostics
                var beforeCompilation = await solution.Projects.FirstOrDefault()?.GetCompilationAsync(cancellationToken);
                var beforeDiagnostics = beforeCompilation != null 
                    ? await DiagnosticHelper.CaptureDiagnosticsAsync(
                        beforeCompilation, 
                        "操作前から存在していた診断", 
                        cancellationToken)
                    : null;

                // Find the source member symbol using fuzzy lookup
                var tempSolutionManager = new StatelessSolutionManager(solution);
                var sourceMemberMatches = await fuzzyLookup.FindMatchesAsync(memberNameOrFqn, tempSolutionManager, cancellationToken);
                var sourceMemberMatch = sourceMemberMatches.FirstOrDefault();
                if (sourceMemberMatch == null) {
                    throw new McpException($"No symbol found matching '{memberNameOrFqn}'.");
                }

                var sourceMemberSymbol = sourceMemberMatch.Symbol;
                if (sourceMemberSymbol == null) {
                    throw new McpException($"Could not find symbol '{memberNameOrFqn}' in the workspace.");
                }

                if (sourceMemberSymbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol or INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate })) {
                    throw new McpException($"Symbol '{memberNameOrFqn}' is not a movable member type. Only fields, properties, methods, events, and nested types can be moved.");
                }

                // Find the destination symbol
                var destinationMatches = await fuzzyLookup.FindMatchesAsync(destinationTypeOrNamespace, tempSolutionManager, cancellationToken);
                var destinationMatch = destinationMatches.FirstOrDefault();
                if (destinationMatch == null) {
                    throw new McpException($"No symbol found matching '{destinationTypeOrNamespace}'.");
                }

                var destinationSymbol = destinationMatch.Symbol;

                if (destinationSymbol is not (INamedTypeSymbol or INamespaceSymbol)) {
                    throw new McpException($"Destination '{destinationTypeOrNamespace}' must be a type or namespace.");
                }

                // Get syntax references
                var sourceSyntaxRef = sourceMemberSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (sourceSyntaxRef == null) {
                    throw new McpException($"Could not find syntax reference for member '{memberNameOrFqn}'.");
                }

                var sourceMemberNode = await sourceSyntaxRef.GetSyntaxAsync(cancellationToken);
                if (sourceMemberNode is not MemberDeclarationSyntax memberDeclaration) {
                    throw new McpException($"Source member '{memberNameOrFqn}' is not a valid member declaration.");
                }

                Document currentSourceDocument = ToolHelpers.GetDocumentFromSyntaxNodeOrThrow(solution, sourceMemberNode);
                Document destinationDocument;
                INamedTypeSymbol? destinationTypeSymbol = null;
                INamespaceSymbol? destinationNamespaceSymbol = null;

                if (destinationSymbol is INamedTypeSymbol typeSym) {
                    destinationTypeSymbol = typeSym;
                    var destSyntaxRef = typeSym.DeclaringSyntaxReferences.FirstOrDefault();
                    if (destSyntaxRef == null) {
                        throw new McpException($"Could not find syntax reference for destination type '{destinationTypeOrNamespace}'.");
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
                    throw new McpException($"Source and destination are the same. Member '{memberNameOrFqn}' is already in '{destinationTypeOrNamespace}'.");
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
                    throw new McpException($"A member with the name '{memberName}' already exists in destination type '{destinationTypeOrNamespace}'.");
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

                    // Create structured result
                    var result = new MoveMemberResult {
                        Success = true,
                        Message = $"メンバー '{memberName}' を '{destinationTypeOrNamespace}' に正常に移動しました",
                        MemberName = memberName,
                        SourceClass = sourceMemberSymbol.ContainingType?.Name ?? "不明",
                        TargetClass = destinationTypeOrNamespace,
                        TargetFile = destinationFilePathDisplay,
                        FilePath = sourceFilePathDisplay
                    };

                    // Add notes
                    if (sourceFilePathDisplay == destinationFilePathDisplay) {
                        result.Notes.Add($"同一ファイル内で移動しました: {sourceFilePathDisplay}");
                    } else {
                        result.Notes.Add($"異なるファイル間で移動しました");
                        result.Notes.Add($"移動元: {sourceFilePathDisplay}");
                        result.Notes.Add($"移動先: {destinationFilePathDisplay}");
                    }

                    // Check compilation status
                    if (finalSourceDocument != null || finalDestinationDocument != null) {
                        var finalProject = finalSourceDocument?.Project ?? finalDestinationDocument?.Project;
                        if (finalProject != null) {
                            var finalCompilation = await finalProject.GetCompilationAsync(cancellationToken);
                            var diagnostics = finalCompilation?.GetDiagnostics(cancellationToken)
                                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error || d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                                .ToList() ?? new List<Diagnostic>();

                            // Add detailed compilation status
                            if (beforeCompilation != null && finalCompilation != null) {
                                result.CompilationStatus = await DiagnosticHelper.CreateDetailedStatusAsync(
                                    beforeCompilation,
                                    finalCompilation,
                                    cancellationToken);
                            }
                        }
                    }

                    return result;

                } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                    logger.LogError(ex, "Failed to move member {MemberName} to {DestinationName}", memberNameOrFqn, destinationTypeOrNamespace);
                    throw new McpException($"Failed to move member '{memberNameOrFqn}' to '{destinationTypeOrNamespace}': {ex.Message}", ex);
                }

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(MoveMember), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ReplaceAcrossFiles), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("🔍 .NET専用 - .sln/.csprojファイルのみ対応。[DESTRUCTIVE] プロジェクト全体で正規表現による検索・置換を実行")]
    public static async Task<object> ReplaceAcrossFiles(
        StatelessWorkspaceFactory workspaceFactory,
        ILogger<ModificationToolsLogCategory> logger,
        [Description(".NETソリューション(.sln)またはプロジェクト(.csproj)ファイルのパス")] string solutionOrProjectPath,
        [Description("検索する正規表現パターン")] string regexPattern,
        [Description("置換文字列（正規表現グループ参照可能: $1, $2など）")] string replacementText,
        [Description("対象ファイルの拡張子フィルター（デフォルト: .cs）")] string fileExtensions = ".cs",
        [Description("実際に変更せずプレビューのみ（デフォルト: false）")] bool dryRun = false,
        [Description("大文字小文字を区別するか（デフォルト: false）")] bool caseSensitive = false,
        [Description("最大処理ファイル数（デフォルト: 1000）")] int maxFiles = 1000,
        [Description("危険な操作の実行確認（正確に \"Yes\" と入力）")] string? userConfirmResponse = null,
        CancellationToken cancellationToken = default) {
        
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<object, ModificationToolsLogCategory>(async () => {
            // 🔍 .NET関連ファイル検証（最優先実行）
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(solutionOrProjectPath, nameof(ReplaceAcrossFiles), logger);
            
            ErrorHandlingHelpers.ValidateStringParameter(solutionOrProjectPath, nameof(solutionOrProjectPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(regexPattern, nameof(regexPattern), logger);
            ErrorHandlingHelpers.ValidateStringParameter(replacementText, nameof(replacementText), logger);

            logger.LogInformation("Executing '{ReplaceAcrossFiles}' with pattern: {Pattern} in {FilePath}",
                nameof(ReplaceAcrossFiles), regexPattern, solutionOrProjectPath);

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

                // Parse file extensions
                var allowedExtensions = fileExtensions.Split(',')
                    .Select(ext => ext.Trim().StartsWith(".") ? ext.Trim() : "." + ext.Trim())
                    .ToHashSet();

                // Collect target documents
                var targetDocuments = solution.Projects
                    .SelectMany(p => p.Documents)
                    .Where(d => d.FilePath != null && 
                                allowedExtensions.Contains(Path.GetExtension(d.FilePath)))
                    .Take(maxFiles)
                    .ToList();

                if (targetDocuments.Count == 0) {
                    return new {
                        pattern = regexPattern,
                        replacement = replacementText,
                        dryRun = dryRun,
                        totalFilesProcessed = 0,
                        totalMatches = 0,
                        affectedFiles = new List<object>(),
                        summary = new {
                            affectedFileCount = 0,
                            totalReplacements = 0,
                            errorCount = 0,
                            filesUpdated = 0
                        },
                        message = "対象ファイルが見つかりませんでした"
                    };
                }

                // Setup regex
                var regexOptions = RegexOptions.Multiline;
                if (!caseSensitive)
                    regexOptions |= RegexOptions.IgnoreCase;

                Regex regex;
                try {
                    regex = new Regex(regexPattern, regexOptions);
                } catch (ArgumentException ex) {
                    throw new McpException($"無効な正規表現パターン: {ex.Message}");
                }

                // Check for dangerous operations before processing
                if (userConfirmResponse?.Trim().Equals("Yes", StringComparison.Ordinal) != true && !dryRun) {
                    // Count total matches across all files
                    int totalMatches = 0;
                    int filesWithMatches = 0;
                    
                    foreach (var document in targetDocuments) {
                        try {
                            var sourceText = await document.GetTextAsync(cancellationToken);
                            var matches = regex.Matches(sourceText.ToString());
                            if (matches.Count > 0) {
                                totalMatches += matches.Count;
                                filesWithMatches++;
                            }
                        } catch {
                            // Ignore errors during counting
                        }
                    }

                    // Check if operation is dangerous
                    var dangerousResult = DangerousOperationDetector.CreateDangerousOperationResult(
                        regexPattern, 
                        totalMatches, 
                        filesWithMatches, 
                        isDestructive: true);

                    if (dangerousResult.DangerousOperationDetected) {
                        dangerousResult.RequiredConfirmationText = "Yes";
                        dangerousResult.ConfirmationPrompt = "この操作を実行するには、userConfirmResponse パラメータに正確に \"Yes\" と入力してください";
                        return dangerousResult;
                    }
                }

                var replacementResults = new List<FileReplacementResult>();

                // Process each file
                foreach (var document in targetDocuments) {
                    try {
                        var sourceText = await document.GetTextAsync(cancellationToken);
                        var originalContent = sourceText.ToString();
                        
                        var matches = regex.Matches(originalContent);
                        if (matches.Count == 0) continue;
                        
                        var newContent = regex.Replace(originalContent, replacementText);
                        
                        var result = new FileReplacementResult {
                            FilePath = document.FilePath ?? "",
                            MatchCount = matches.Count,
                            Changes = matches.Cast<Match>().Select(m => new {
                                line = sourceText.Lines.GetLineFromPosition(m.Index).LineNumber + 1,
                                originalText = m.Value,
                                replacedText = regex.Replace(m.Value, replacementText),
                                startPosition = m.Index,
                                length = m.Length
                            }).Cast<object>().ToList()
                        };
                        
                        if (!dryRun && document.FilePath != null) {
                            // Actually update the file
                            await File.WriteAllTextAsync(document.FilePath, newContent, Encoding.UTF8, cancellationToken);
                            result.Updated = true;
                        }
                        
                        replacementResults.Add(result);
                    } catch (Exception ex) {
                        // Log error and continue processing
                        logger.LogError(ex, "Error processing file: {FilePath}", document.FilePath);
                        replacementResults.Add(new FileReplacementResult { 
                            FilePath = document.FilePath ?? "",
                            Error = ex.Message 
                        });
                    }
                }

                // Get compilation status if changes were made
                CompilationStatusInfo? compilationStatus = null;
                if (!dryRun && replacementResults.Any(r => r.Updated)) {
                    try {
                        // Reload solution to get updated compilation
                        var (newWorkspace, newContext, newContextType) = await workspaceFactory.CreateForContextAsync(solutionOrProjectPath);
                        Solution newSolution;
                        if (newContext is Solution newSol) {
                            newSolution = newSol;
                        } else if (newContext is Project newProj) {
                            newSolution = newProj.Solution;
                        } else {
                            var dynamicNewContext = (dynamic)newContext;
                            newSolution = ((Project)dynamicNewContext.Project).Solution;
                        }
                        var compilation = await newSolution.Projects.FirstOrDefault()?.GetCompilationAsync(cancellationToken);
                        if (compilation != null) {
                            var diagnostics = await DiagnosticHelper.CaptureDiagnosticsAsync(
                                compilation, 
                                "置換後の診断", 
                                cancellationToken);
                            compilationStatus = new CompilationStatusInfo {
                                ErrorCount = diagnostics.ErrorCount,
                                WarningCount = diagnostics.WarningCount,
                                Status = diagnostics.ErrorCount > 0 ? "errors" : "clean"
                            };
                        }
                        newWorkspace.Dispose();
                    } catch (Exception ex) {
                        logger.LogWarning(ex, "Failed to get compilation status after replacement");
                    }
                }

                return new ReplaceAcrossFilesResult {
                    Pattern = regexPattern,
                    Replacement = replacementText,
                    DryRun = dryRun,
                    TotalFilesProcessed = targetDocuments.Count,
                    TotalMatches = replacementResults.Sum(r => r.MatchCount),
                    AffectedFiles = replacementResults.Where(r => r.MatchCount > 0).ToList(),
                    Summary = new ReplaceAcrossFilesSummary {
                        AffectedFileCount = replacementResults.Count(r => r.MatchCount > 0),
                        TotalReplacements = replacementResults.Sum(r => r.MatchCount),
                        ErrorCount = replacementResults.Count(r => !string.IsNullOrEmpty(r.Error)),
                        FilesUpdated = dryRun ? 0 : replacementResults.Count(r => r.Updated)
                    },
                    CompilationStatus = compilationStatus
                };

            } finally {
                workspace.Dispose();
            }
        }, logger, nameof(ReplaceAcrossFiles), cancellationToken);
    }

    #endregion
}