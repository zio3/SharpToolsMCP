using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class TestOverwriteMember
{
    static async Task Main()
    {
        // Register MSBuild
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var testProjectPath = Path.GetFullPath("../TestProject/TestProject.csproj");
        var testClassPath = Path.GetFullPath("../TestProject/TestClass.cs");
        
        Console.WriteLine($"Test Project: {testProjectPath}");
        Console.WriteLine($"Test Class: {testClassPath}");
        Console.WriteLine();

        // Test symbol search patterns
        var searchPatterns = new[]
        {
            "TestMethod",
            "TestProject.TestClass.TestMethod",
            "GetGreeting",
            "TestProject.TestClass.GetGreeting"
        };

        using var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(testProjectPath);
        var compilation = await project.GetCompilationAsync();
        
        if (compilation == null)
        {
            Console.WriteLine("Failed to get compilation");
            return;
        }

        Console.WriteLine("Testing symbol search patterns:");
        Console.WriteLine(new string('-', 50));

        foreach (var pattern in searchPatterns)
        {
            Console.WriteLine($"\nSearching for: '{pattern}'");
            
            ISymbol? foundSymbol = null;
            
            // Method 1: Search through all syntax trees
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();
                
                var declarations = root.DescendantNodes()
                    .Where(n => n is MemberDeclarationSyntax || n is TypeDeclarationSyntax);
                
                foreach (var node in declarations)
                {
                    var declaredSymbol = semanticModel.GetDeclaredSymbol(node);
                    if (declaredSymbol != null)
                    {
                        // Test different display formats
                        var displayString = declaredSymbol.ToDisplayString();
                        var metadataName = declaredSymbol.MetadataName;
                        var name = declaredSymbol.Name;
                        
                        Console.WriteLine($"  Found symbol: Name='{name}', MetadataName='{metadataName}', DisplayString='{displayString}'");
                        
                        if (displayString == pattern || name == pattern || 
                            displayString.EndsWith("." + pattern))
                        {
                            foundSymbol = declaredSymbol;
                            Console.WriteLine($"  ✓ MATCHED!");
                            break;
                        }
                    }
                }
                
                if (foundSymbol != null) break;
            }
            
            if (foundSymbol == null)
            {
                Console.WriteLine($"  ✗ Symbol not found");
            }
        }

        // Test adding GetGreeting method to understand the actual FQN format
        Console.WriteLine("\n\nAnalyzing existing methods:");
        Console.WriteLine(new string('-', 50));
        
        var testClassFile = compilation.SyntaxTrees.FirstOrDefault(t => t.FilePath?.EndsWith("TestClass.cs") == true);
        if (testClassFile != null)
        {
            var semanticModel = compilation.GetSemanticModel(testClassFile);
            var root = await testClassFile.GetRootAsync();
            
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method);
                if (symbol != null)
                {
                    Console.WriteLine($"Method: {method.Identifier.Text}");
                    Console.WriteLine($"  ToDisplayString(): {symbol.ToDisplayString()}");
                    Console.WriteLine($"  ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat): {symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
                    Console.WriteLine($"  ContainingType: {symbol.ContainingType?.ToDisplayString()}");
                }
            }
        }
    }
}