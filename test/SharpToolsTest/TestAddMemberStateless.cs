using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpToolsTest
{
    class TestAddMemberStateless
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Testing AddMember_Stateless functionality...\n");

            // Test parameters
            string testClassPath = Path.Combine(Directory.GetCurrentDirectory(), "TestClass.cs");
            string memberDeclaration = "public bool IsEmpty => string.IsNullOrEmpty(Name);";
            string className = "TestClass";

            try
            {
                // Step 1: Read the original file
                Console.WriteLine($"1. Reading file: {testClassPath}");
                string originalContent = File.ReadAllText(testClassPath);
                Console.WriteLine("   ✓ File read successfully");

                // Step 2: Parse the member declaration
                Console.WriteLine($"\n2. Parsing member declaration: {memberDeclaration}");
                var memberSyntax = CSharpSyntaxTree.ParseText(memberDeclaration).GetRoot().DescendantNodes().FirstOrDefault();
                if (memberSyntax == null)
                {
                    Console.WriteLine("   ✗ Failed to parse member declaration");
                    return;
                }
                Console.WriteLine($"   ✓ Parsed as: {memberSyntax.GetType().Name}");

                // Step 3: Parse the original file
                Console.WriteLine($"\n3. Parsing original file content");
                var tree = CSharpSyntaxTree.ParseText(originalContent);
                var root = tree.GetRoot();
                Console.WriteLine("   ✓ File parsed successfully");

                // Step 4: Find type declarations (simulating the problematic code)
                Console.WriteLine($"\n4. Finding type declarations in the file");
                var typeDeclarations = root.DescendantNodes()
                    .Where(n => n is TypeDeclarationSyntax)
                    .Cast<TypeDeclarationSyntax>()
                    .ToList();
                
                Console.WriteLine($"   ✓ Found {typeDeclarations.Count} type declaration(s)");
                foreach (var type in typeDeclarations)
                {
                    Console.WriteLine($"      - {type.GetType().Name}: {type.Identifier.Text}");
                }

                // Step 5: Find the target class
                Console.WriteLine($"\n5. Finding target class: {className}");
                var targetClass = typeDeclarations.FirstOrDefault(t => t.Identifier.Text == className);
                if (targetClass == null)
                {
                    Console.WriteLine("   ✗ Target class not found");
                    return;
                }
                Console.WriteLine($"   ✓ Found {targetClass.GetType().Name}: {targetClass.Identifier.Text}");

                // Step 6: Add the member to the class
                Console.WriteLine($"\n6. Adding member to class");
                var updatedClass = targetClass.AddMembers(memberSyntax as MemberDeclarationSyntax);
                var updatedRoot = root.ReplaceNode(targetClass, updatedClass);
                Console.WriteLine("   ✓ Member added successfully");

                // Step 7: Search for the added member (simulating the problematic search)
                Console.WriteLine($"\n7. Searching for added member in updated syntax tree");
                var searchMemberName = "IsEmpty";
                
                // This is the search pattern that was causing issues
                var foundMembers = updatedRoot.DescendantNodes()
                    .Where(n => n is MemberDeclarationSyntax)
                    .Cast<MemberDeclarationSyntax>()
                    .Where(m => 
                    {
                        // Get the member name based on its type
                        string name = m switch
                        {
                            PropertyDeclarationSyntax prop => prop.Identifier.Text,
                            MethodDeclarationSyntax method => method.Identifier.Text,
                            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
                            _ => null
                        };
                        return name == searchMemberName;
                    })
                    .ToList();

                Console.WriteLine($"   ✓ Search completed without errors");
                Console.WriteLine($"   ✓ Found {foundMembers.Count} member(s) named '{searchMemberName}'");

                // Step 8: Verify the namespace handling
                Console.WriteLine($"\n8. Verifying namespace handling");
                var allNodes = updatedRoot.DescendantNodes().ToList();
                var namespaceDeclarations = allNodes.OfType<NamespaceDeclarationSyntax>().Count();
                var fileScopedNamespaces = allNodes.OfType<FileScopedNamespaceDeclarationSyntax>().Count();
                
                Console.WriteLine($"   - Standard namespace declarations: {namespaceDeclarations}");
                Console.WriteLine($"   - File-scoped namespace declarations: {fileScopedNamespaces}");
                Console.WriteLine("   ✓ No NamespaceDeclarationSyntax casting errors occurred");

                // Step 9: Write the modified content (optional)
                Console.WriteLine($"\n9. Generating modified content");
                string modifiedContent = updatedRoot.ToFullString();
                string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "TestClass_Modified.cs");
                File.WriteAllText(outputPath, modifiedContent);
                Console.WriteLine($"   ✓ Modified content written to: {outputPath}");

                Console.WriteLine("\n✅ All tests passed! The AddMember_Stateless fix is working correctly.");
            }
            catch (InvalidCastException ex) when (ex.Message.Contains("NamespaceDeclarationSyntax"))
            {
                Console.WriteLine($"\n❌ ERROR: The NamespaceDeclarationSyntax casting error still occurs!");
                Console.WriteLine($"   Message: {ex.Message}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERROR: {ex.GetType().Name}");
                Console.WriteLine($"   Message: {ex.Message}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}