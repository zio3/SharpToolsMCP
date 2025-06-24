using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

class TestSymbolSearch
{
    static void Main()
    {
        Console.WriteLine("Testing Symbol Search Logic for OverwriteMember_Stateless");
        Console.WriteLine(new string('=', 60));

        // Test code from TestClass.cs
        var code = @"
namespace TestProject;

public class TestClass
{
    public string Name { get; set; } = string.Empty;
    
    public string GetGreeting()
    {
        return $""Hello, {Name}!"";
    }
}";

        // Parse the code
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);

        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        // Test search patterns (simulating what OverwriteMember_Stateless might receive)
        var searchPatterns = new[]
        {
            "GetGreeting",
            "TestClass.GetGreeting", 
            "TestProject.TestClass.GetGreeting",
            "global::TestProject.TestClass.GetGreeting"
        };

        Console.WriteLine("\nAnalyzing all symbols in the code:");
        Console.WriteLine(new string('-', 60));

        // Get all declared symbols
        var allDeclarations = root.DescendantNodes()
            .Where(n => n is MemberDeclarationSyntax || n is TypeDeclarationSyntax);

        foreach (var node in allDeclarations)
        {
            var symbol = semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                Console.WriteLine($"\nSymbol: {symbol.Name}");
                Console.WriteLine($"  Kind: {symbol.Kind}");
                Console.WriteLine($"  ToDisplayString(): {symbol.ToDisplayString()}");
                Console.WriteLine($"  ToDisplayString(FullyQualifiedFormat): {symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
                Console.WriteLine($"  MetadataName: {symbol.MetadataName}");
                
                if (symbol.ContainingType != null)
                {
                    Console.WriteLine($"  ContainingType: {symbol.ContainingType.ToDisplayString()}");
                }
            }
        }

        Console.WriteLine("\n\nTesting symbol search with different patterns:");
        Console.WriteLine(new string('-', 60));

        foreach (var pattern in searchPatterns)
        {
            Console.WriteLine($"\nSearching for: '{pattern}'");
            
            bool found = false;
            foreach (var node in allDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(node);
                if (symbol != null)
                {
                    // Test various matching strategies
                    var displayString = symbol.ToDisplayString();
                    var fullyQualifiedString = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var name = symbol.Name;

                    if (displayString == pattern || 
                        fullyQualifiedString == pattern ||
                        name == pattern ||
                        displayString.EndsWith("." + pattern) ||
                        fullyQualifiedString.EndsWith("." + pattern))
                    {
                        Console.WriteLine($"  ✓ FOUND! Matched with: {displayString}");
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                Console.WriteLine($"  ✗ NOT FOUND");
            }
        }

        // Test the improved search logic
        Console.WriteLine("\n\nTesting improved search logic:");
        Console.WriteLine(new string('-', 60));

        var improvedSearchPattern = "TestProject.TestClass.GetGreeting";
        Console.WriteLine($"Searching for: '{improvedSearchPattern}'");

        ISymbol? foundSymbol = null;
        foreach (var node in allDeclarations)
        {
            var symbol = semanticModel.GetDeclaredSymbol(node);
            if (symbol != null && IsSymbolMatch(symbol, improvedSearchPattern))
            {
                foundSymbol = symbol;
                Console.WriteLine($"✓ Found using improved logic: {symbol.ToDisplayString()}");
                break;
            }
        }

        if (foundSymbol == null)
        {
            Console.WriteLine("✗ Not found with improved logic");
        }
    }

    static bool IsSymbolMatch(ISymbol symbol, string fullyQualifiedName)
    {
        // Direct name match
        if (symbol.Name == fullyQualifiedName)
            return true;

        // Full display string match
        if (symbol.ToDisplayString() == fullyQualifiedName)
            return true;

        // Check various display formats
        var formats = new[]
        {
            SymbolDisplayFormat.FullyQualifiedFormat,
            SymbolDisplayFormat.CSharpErrorMessageFormat,
            SymbolDisplayFormat.MinimallyQualifiedFormat
        };

        foreach (var format in formats)
        {
            if (symbol.ToDisplayString(format) == fullyQualifiedName)
                return true;
        }

        // Build FQN manually and compare
        var fqn = BuildFullyQualifiedName(symbol);
        if (fqn == fullyQualifiedName)
            return true;

        // Check if the pattern matches the end of any format
        return symbol.ToDisplayString().EndsWith("." + fullyQualifiedName) ||
               fqn.EndsWith("." + fullyQualifiedName);
    }

    static string BuildFullyQualifiedName(ISymbol symbol)
    {
        var parts = new System.Collections.Generic.List<string>();
        
        // Add symbol name
        parts.Add(symbol.Name);
        
        // Add containing types
        var container = symbol.ContainingSymbol;
        while (container != null)
        {
            if (container is INamespaceSymbol ns && ns.IsGlobalNamespace)
                break;
                
            parts.Insert(0, container.Name);
            container = container.ContainingSymbol;
        }
        
        return string.Join(".", parts);
    }
}