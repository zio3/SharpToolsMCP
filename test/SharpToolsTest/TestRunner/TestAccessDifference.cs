using System;
using System.IO;
using System.Threading.Tasks;

class TestAccessDifference
{
    static async Task Main()
    {
        Console.WriteLine("Testing Access Control Difference between Stateful and Stateless");
        Console.WriteLine(new string('=', 70));

        // Create test files in different locations
        var testScenarios = new[]
        {
            new { Location = "../../../TestFile.cs", Description = "File outside project directory" },
            new { Location = "TestFile.cs", Description = "File in current directory" },
            new { Location = Path.GetTempFileName() + ".cs", Description = "Temp directory file" }
        };

        foreach (var scenario in testScenarios)
        {
            Console.WriteLine($"\nTest: {scenario.Description}");
            Console.WriteLine($"Path: {scenario.Location}");
            Console.WriteLine(new string('-', 50));

            try
            {
                // Create test file
                var testCode = @"
namespace Test
{
    public class TestClass
    {
        public string TestField = ""original"";
    }
}";
                await File.WriteAllTextAsync(scenario.Location, testCode);
                Console.WriteLine("✓ File created successfully");

                // Get absolute path
                var absolutePath = Path.GetFullPath(scenario.Location);
                Console.WriteLine($"Absolute path: {absolutePath}");

                // Check if it's within solution directory
                var currentDir = Directory.GetCurrentDirectory();
                var solutionDir = Path.GetFullPath(Path.Combine(currentDir, "../../../.."));
                var isWithinSolution = absolutePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"Within solution directory: {isWithinSolution}");

                // Try to read and modify
                var content = await File.ReadAllTextAsync(scenario.Location);
                var modified = content.Replace("original", "modified");
                await File.WriteAllTextAsync(scenario.Location, modified);
                Console.WriteLine("✓ Direct file access successful");

                // Cleanup
                File.Delete(scenario.Location);
                Console.WriteLine("✓ File deleted");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                // Try cleanup anyway
                try { File.Delete(scenario.Location); } catch { }
            }
        }

        Console.WriteLine("\n\nKey Differences:");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine("1. Stateful (FindAndReplace):");
        Console.WriteLine("   - Checks pathInfo.IsWritable before processing");
        Console.WriteLine("   - Skips restricted files with warning (no error)");
        Console.WriteLine("   - Continues processing other files");
        Console.WriteLine("\n2. Stateless (FindAndReplace_Stateless):");
        Console.WriteLine("   - Directly calls ReadFileAsync");
        Console.WriteLine("   - Throws exception for restricted files");
        Console.WriteLine("   - Fails immediately");
        
        Console.WriteLine("\n3. Why the difference?");
        Console.WriteLine("   - Stateful processes multiple files, needs graceful handling");
        Console.WriteLine("   - Stateless processes single file, fails fast");
        Console.WriteLine("   - Different error handling philosophies");
    }
}