using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class TestFindAndReplace
{
    static async Task Main()
    {
        Console.WriteLine("Testing FindAndReplace_Stateless");
        Console.WriteLine(new string('=', 60));

        // Test file path
        var testFilePath = Path.GetFullPath("../TestProject/TestClass.cs");
        Console.WriteLine($"Test file: {testFilePath}\n");

        // Read the original content
        string originalContent;
        try
        {
            originalContent = await File.ReadAllTextAsync(testFilePath);
            Console.WriteLine("Original file content:");
            Console.WriteLine(new string('-', 40));
            Console.WriteLine(originalContent);
            Console.WriteLine(new string('-', 40));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to read file: {ex.Message}");
            return;
        }

        // Test various regex patterns
        var testPatterns = new[]
        {
            new { Pattern = @"GetGreeting", Replacement = "GetWelcomeMessage", Description = "Simple method rename" },
            new { Pattern = @"string\.Empty", Replacement = "\"\"", Description = "Replace string.Empty with empty string" },
            new { Pattern = @"public string (\w+)", Replacement = "public string $1", Description = "No-op pattern (should not change)" }
        };

        foreach (var test in testPatterns)
        {
            Console.WriteLine($"\n\nTest: {test.Description}");
            Console.WriteLine($"Pattern: {test.Pattern}");
            Console.WriteLine($"Replacement: {test.Replacement}");
            Console.WriteLine(new string('-', 40));

            try
            {
                // Test the regex
                var regex = new Regex(test.Pattern, RegexOptions.Multiline);
                var matches = regex.Matches(originalContent);
                Console.WriteLine($"Found {matches.Count} match(es)");

                if (matches.Count > 0)
                {
                    var newContent = regex.Replace(originalContent, test.Replacement);
                    
                    if (newContent != originalContent)
                    {
                        Console.WriteLine("✓ Replacement would change the file");
                        Console.WriteLine("\nPreview of changes:");
                        var lines = newContent.Split('\n');
                        for (int i = 0; i < Math.Min(lines.Length, 15); i++)
                        {
                            Console.WriteLine($"  {lines[i]}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("✗ Replacement produces identical text");
                    }
                }
                else
                {
                    Console.WriteLine("✗ No matches found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        // Test file access permissions
        Console.WriteLine("\n\nTesting file access permissions:");
        Console.WriteLine(new string('-', 40));

        // Test write access
        var tempFile = Path.Combine(Path.GetDirectoryName(testFilePath)!, "TestWriteAccess.tmp");
        try
        {
            await File.WriteAllTextAsync(tempFile, "test");
            Console.WriteLine($"✓ Write access confirmed to directory");
            File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ No write access: {ex.Message}");
        }

        // Test modifying the actual file (create a backup first)
        var backupFile = testFilePath + ".bak";
        try
        {
            // Create backup
            File.Copy(testFilePath, backupFile, true);
            Console.WriteLine($"✓ Created backup: {backupFile}");

            // Try to modify
            var testContent = originalContent.Replace("GetGreeting", "GetGreetingModified");
            await File.WriteAllTextAsync(testFilePath, testContent);
            Console.WriteLine("✓ Successfully modified the file");

            // Restore from backup
            File.Copy(backupFile, testFilePath, true);
            File.Delete(backupFile);
            Console.WriteLine("✓ Restored from backup");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ File modification failed: {ex.Message}");
            
            // Try to restore if backup exists
            if (File.Exists(backupFile))
            {
                try
                {
                    File.Copy(backupFile, testFilePath, true);
                    File.Delete(backupFile);
                    Console.WriteLine("✓ Restored from backup after error");
                }
                catch
                {
                    Console.WriteLine("❌ Failed to restore backup");
                }
            }
        }
    }
}