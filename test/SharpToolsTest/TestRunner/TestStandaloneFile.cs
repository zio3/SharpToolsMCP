using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class TestStandaloneFile
{
    static async Task Main()
    {
        Console.WriteLine("Testing Standalone C# File Analysis and Modification");
        Console.WriteLine(new string('=', 60));

        // Create a standalone C# file
        var standaloneFilePath = Path.GetFullPath("StandaloneClass.cs");
        var standaloneCode = @"using System;

namespace StandaloneNamespace
{
    public class StandaloneClass
    {
        public string Name { get; set; }
        
        public void PrintName()
        {
            Console.WriteLine(Name);
        }
    }
}";

        // Write the standalone file
        await File.WriteAllTextAsync(standaloneFilePath, standaloneCode);
        Console.WriteLine($"Created standalone file: {standaloneFilePath}\n");

        // Test 1: Parse and analyze without project context
        Console.WriteLine("Test 1: Analyzing standalone file without project");
        Console.WriteLine(new string('-', 50));

        var tree = CSharpSyntaxTree.ParseText(standaloneCode, path: standaloneFilePath);
        var compilation = CSharpCompilation.Create("StandaloneAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddReferences(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
            .AddSyntaxTrees(tree);

        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        // Find the class
        var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDeclaration != null)
        {
            Console.WriteLine($"✓ Found class: {classDeclaration.Identifier.Text}");
            
            // Get semantic info
            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
            if (classSymbol != null)
            {
                Console.WriteLine($"✓ Class FQN: {classSymbol.ToDisplayString()}");
            }
        }

        // Test 2: Add a member to the standalone file
        Console.WriteLine("\n\nTest 2: Adding a member to standalone file");
        Console.WriteLine(new string('-', 50));

        var memberCode = "public bool IsEmpty => string.IsNullOrEmpty(Name);";
        var memberSyntax = SyntaxFactory.ParseMemberDeclaration(memberCode);

        if (memberSyntax != null && classDeclaration != null)
        {
            Console.WriteLine($"✓ Parsed member: {memberCode}");

            // Add member using syntax manipulation
            var newClass = classDeclaration.AddMembers(memberSyntax);
            var newRoot = root.ReplaceNode(classDeclaration, newClass);
            
            // Format the result
            var workspace = new AdhocWorkspace();
            var solution = workspace.CurrentSolution;
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            
            solution = solution
                .AddProject(projectId, "StandaloneProject", "StandaloneAssembly", LanguageNames.CSharp)
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddDocument(documentId, "StandaloneClass.cs", newRoot);

            var document = solution.GetDocument(documentId);
            if (document != null)
            {
                var formattedDocument = await Formatter.FormatAsync(document);
                var formattedText = await formattedDocument.GetTextAsync();
                
                Console.WriteLine("\nFormatted result:");
                Console.WriteLine(formattedText.ToString());
                
                // Save the modified file
                var modifiedPath = Path.ChangeExtension(standaloneFilePath, ".modified.cs");
                await File.WriteAllTextAsync(modifiedPath, formattedText.ToString());
                Console.WriteLine($"\n✓ Saved modified file: {modifiedPath}");
            }
        }

        // Test 3: Alternative approach using DocumentEditor
        Console.WriteLine("\n\nTest 3: Using DocumentEditor on standalone file");
        Console.WriteLine(new string('-', 50));

        // Create an ad-hoc workspace with the file
        var workspace2 = new AdhocWorkspace();
        var solution2 = workspace2.CurrentSolution;
        var projectId2 = ProjectId.CreateNewId();
        var documentId2 = DocumentId.CreateNewId(projectId2);
        
        solution2 = solution2
            .AddProject(projectId2, "TestProject", "TestAssembly", LanguageNames.CSharp)
            .AddMetadataReference(projectId2, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(documentId2, standaloneFilePath, standaloneCode);

        var doc2 = solution2.GetDocument(documentId2);
        if (doc2 != null)
        {
            var editor = await DocumentEditor.CreateAsync(doc2);
            var root2 = await doc2.GetSyntaxRootAsync();
            var class2 = root2?.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            
            if (class2 != null)
            {
                var newMember = SyntaxFactory.ParseMemberDeclaration("public int Count { get; set; }");
                if (newMember != null)
                {
                    editor.AddMember(class2, newMember);
                    var changedDoc = editor.GetChangedDocument();
                    var changedText = await changedDoc.GetTextAsync();
                    
                    Console.WriteLine("✓ Successfully added member using DocumentEditor");
                    Console.WriteLine("\nResult preview:");
                    var lines = changedText.ToString().Split('\n').Take(20);
                    foreach (var line in lines)
                    {
                        Console.WriteLine($"  {line}");
                    }
                }
            }
        }

        // Cleanup
        File.Delete(standaloneFilePath);
        Console.WriteLine("\n✓ Cleanup completed");
    }
}