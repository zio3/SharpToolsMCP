using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class TestSimpleStandalone
{
    static async Task Main()
    {
        Console.WriteLine("Testing Simple Standalone C# File Modification");
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

        // Test: Add member without project context
        Console.WriteLine("Adding member to standalone file:");
        Console.WriteLine(new string('-', 50));

        try
        {
            // Parse the file
            var tree = CSharpSyntaxTree.ParseText(standaloneCode);
            var root = tree.GetRoot();

            // Find the class
            var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration == null)
            {
                throw new Exception("Class not found");
            }

            Console.WriteLine($"✓ Found class: {classDeclaration.Identifier.Text}");

            // Parse the new member
            var memberCode = "public bool IsEmpty => string.IsNullOrEmpty(Name);";
            var memberSyntax = SyntaxFactory.ParseMemberDeclaration(memberCode);
            if (memberSyntax == null)
            {
                throw new Exception("Failed to parse member");
            }

            Console.WriteLine($"✓ Parsed member: {memberCode}");

            // Add the member
            var newClass = classDeclaration.AddMembers(memberSyntax.NormalizeWhitespace());
            var newRoot = root.ReplaceNode(classDeclaration, newClass);

            // Format using NormalizeWhitespace instead
            var formattedRoot = newRoot.NormalizeWhitespace();
            
            // Save the result
            var modifiedCode = formattedRoot.ToFullString();
            var modifiedPath = Path.ChangeExtension(standaloneFilePath, ".modified.cs");
            await File.WriteAllTextAsync(modifiedPath, modifiedCode);
            
            Console.WriteLine($"\n✓ Saved modified file: {modifiedPath}");
            Console.WriteLine("\nModified content:");
            Console.WriteLine(new string('-', 40));
            Console.WriteLine(modifiedCode);

            // Verify compilation
            var modifiedTree = CSharpSyntaxTree.ParseText(modifiedCode);
            var compilation = CSharpCompilation.Create("Test")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                .AddSyntaxTrees(modifiedTree);

            var diagnostics = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            if (diagnostics.Any())
            {
                Console.WriteLine("\n❌ Compilation errors:");
                foreach (var diag in diagnostics)
                {
                    Console.WriteLine($"  - {diag.GetMessage()}");
                }
            }
            else
            {
                Console.WriteLine("\n✓ No compilation errors");
            }

            // Test the approach that AddMember_Stateless could use
            Console.WriteLine("\n\nProposed approach for AddMember_Stateless:");
            Console.WriteLine(new string('-', 50));
            Console.WriteLine("1. If projectPath parameter is provided:");
            Console.WriteLine("   - Use existing logic (load project, find document)");
            Console.WriteLine("2. If projectPath is null/empty:");
            Console.WriteLine("   - Parse file directly as shown above");
            Console.WriteLine("   - Add member using syntax manipulation");
            Console.WriteLine("   - Format and save");
            Console.WriteLine("3. Benefits:");
            Console.WriteLine("   - Works with any C# file");
            Console.WriteLine("   - No project/solution required");
            Console.WriteLine("   - Still provides syntax validation");

            // Cleanup
            File.Delete(standaloneFilePath);
            if (File.Exists(modifiedPath))
                File.Delete(modifiedPath);
                
            Console.WriteLine("\n✓ Test completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}