using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

class TestFieldSearch
{
    static void Main()
    {
        Console.WriteLine("Testing Field Symbol Search");
        Console.WriteLine(new string('=', 60));

        // Test code with various field types
        var code = @"
namespace OppSites
{
    public class CstoolsTest
    {
        private string _testField = ""initial"";
        public const int DEFAULT_TIMEOUT_MS = 5000;
        private readonly object _lockObject = new object();
        public static string StaticField = ""static"";
        
        public string TestProperty { get; set; }
        
        public void TestMethod()
        {
            Console.WriteLine(_testField);
        }
    }
}";

        // Parse the code
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);

        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        Console.WriteLine("Analyzing all fields in the code:");
        Console.WriteLine(new string('-', 60));

        // Get all field declarations
        var fieldDeclarations = root.DescendantNodes().OfType<FieldDeclarationSyntax>();

        foreach (var fieldDecl in fieldDeclarations)
        {
            Console.WriteLine($"\nField Declaration: {fieldDecl.Declaration}");
            
            foreach (var variable in fieldDecl.Declaration.Variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable);
                if (symbol != null)
                {
                    Console.WriteLine($"  Variable: {variable.Identifier.Text}");
                    Console.WriteLine($"  Symbol Name: {symbol.Name}");
                    Console.WriteLine($"  Symbol Kind: {symbol.Kind}");
                    Console.WriteLine($"  ToDisplayString(): {symbol.ToDisplayString()}");
                    Console.WriteLine($"  FullyQualifiedName: {BuildFullyQualifiedName(symbol)}");
                    Console.WriteLine($"  ContainingType: {symbol.ContainingType?.ToDisplayString()}");
                }
            }
        }

        // Test search patterns
        Console.WriteLine("\n\nTesting field search patterns:");
        Console.WriteLine(new string('-', 60));

        var searchPatterns = new[]
        {
            "_testField",
            "OppSites.CstoolsTest._testField",
            "DEFAULT_TIMEOUT_MS",
            "OppSites.CstoolsTest.DEFAULT_TIMEOUT_MS",
            "_lockObject",
            "StaticField"
        };

        var allDeclarations = root.DescendantNodes()
            .Where(n => n is MemberDeclarationSyntax || n is TypeDeclarationSyntax);

        foreach (var pattern in searchPatterns)
        {
            Console.WriteLine($"\nSearching for: '{pattern}'");
            
            bool found = false;
            foreach (var node in allDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(node);
                if (symbol != null && IsSymbolMatch(symbol, pattern))
                {
                    Console.WriteLine($"  ✓ FOUND as {symbol.Kind}: {symbol.ToDisplayString()}");
                    found = true;
                    break;
                }
            }

            // Also search in field declarations specifically
            if (!found)
            {
                foreach (var fieldDecl in fieldDeclarations)
                {
                    foreach (var variable in fieldDecl.Declaration.Variables)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(variable);
                        if (symbol != null && IsSymbolMatch(symbol, pattern))
                        {
                            Console.WriteLine($"  ✓ FOUND in field declarations: {symbol.ToDisplayString()}");
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }
            }

            if (!found)
            {
                Console.WriteLine($"  ✗ NOT FOUND");
            }
        }
    }

    static bool IsSymbolMatch(ISymbol symbol, string fullyQualifiedName)
    {
        // Direct name match
        if (symbol.Name == fullyQualifiedName)
            return true;

        // Build FQN and compare
        var fqn = BuildFullyQualifiedName(symbol);
        if (fqn == fullyQualifiedName)
            return true;

        // For methods, try without parentheses
        if (symbol is IMethodSymbol)
        {
            var displayString = symbol.ToDisplayString();
            var displayWithoutParens = displayString.Replace("()", "");
            if (displayWithoutParens == fullyQualifiedName)
                return true;
        }

        // Check if pattern matches the end
        if (fqn.EndsWith("." + fullyQualifiedName))
            return true;

        return false;
    }

    static string BuildFullyQualifiedName(ISymbol symbol)
    {
        var parts = new System.Collections.Generic.List<string>();
        
        parts.Add(symbol.Name);
        
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