using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "parse-issue")
            {
                TestParseIssue.RunParseIssueTest();
            }
            else
            {
                // Run the AddMember_Stateless test by default
                TestAddMemberStateless.RunTest();
            }
        }
    }
    
    class TestParseIssue
    {
        public static void RunParseIssueTest()
        {
            // Test various code snippets to understand what causes NamespaceDeclarationSyntax
            var testSnippets = new[]
            {
                // Valid member declarations
                "public bool IsEmpty => string.IsNullOrEmpty(Name);",
                "public void TestMethod() { }",
                "private int _count;",
                
                // Potentially problematic snippets
                "namespace TestProject { }",
                "namespace TestProject; public void Test() { }",
                "public bool IsEmpty => string.IsNullOrEmpty(Name);"
            };

            foreach (var snippet in testSnippets)
            {
                Console.WriteLine($"\nTesting snippet: {snippet}");
                Console.WriteLine(new string('-', 50));
                
                try
                {
                    var member = SyntaxFactory.ParseMemberDeclaration(snippet);
                    if (member == null)
                    {
                        Console.WriteLine("Result: null (failed to parse as member)");
                    }
                    else
                    {
                        Console.WriteLine($"Result: {member.GetType().Name}");
                        Console.WriteLine($"Kind: {member.Kind()}");
                        
                        // Try to get member name like in GetMemberName
                        try
                        {
                            var name = GetMemberName(member);
                            Console.WriteLine($"Member Name: {name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"GetMemberName Error: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Parse Error: {ex.Message}");
                }
            }
            
            // Test what ParseCompilationUnit returns
            Console.WriteLine("\n\nTesting ParseCompilationUnit:");
            var compilationUnit = SyntaxFactory.ParseCompilationUnit("public bool IsEmpty => string.IsNullOrEmpty(Name);");
            Console.WriteLine($"Members count: {compilationUnit.Members.Count}");
            foreach (var member in compilationUnit.Members)
            {
                Console.WriteLine($"Member type: {member.GetType().Name}");
            }
        }
        
        static string GetMemberName(MemberDeclarationSyntax memberSyntax)
        {
            return memberSyntax switch
            {
                MethodDeclarationSyntax method => method.Identifier.Text,
                PropertyDeclarationSyntax property => property.Identifier.Text,
                FieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.Text,
                TypeDeclarationSyntax type => type.Identifier.Text,
                NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                _ => throw new NotSupportedException($"Unsupported member type: {memberSyntax.GetType().Name}")
            };
        }
    }
}