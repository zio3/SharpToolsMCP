using System.IO;
using System.Threading.Tasks;

namespace SharpTools.Tools.Services;

/// <summary>
/// Service for discovering project and solution files from file paths
/// </summary>
public class ProjectDiscoveryService
{
    /// <summary>
    /// Defines the type of context for resolution
    /// </summary>
    public enum ContextType
    {
        File,
        Project,
        Solution
    }

    /// <summary>
    /// Finds the containing .csproj file for a given file path
    /// </summary>
    /// <param name="filePath">The file path to start searching from</param>
    /// <returns>The path to the containing project file, or null if not found</returns>
    public async Task<string?> FindContainingProjectAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            // Convert to absolute path if relative
            var absolutePath = Path.GetFullPath(filePath);
            
            var directory = File.Exists(absolutePath) 
                ? Path.GetDirectoryName(absolutePath) 
                : absolutePath;

            while (!string.IsNullOrEmpty(directory))
            {
                try
                {
                    var csprojFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
                    if (csprojFiles.Length > 0)
                    {
                        // Return the full absolute path
                        return Path.GetFullPath(csprojFiles[0]);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we can't access
                }

                var parent = Directory.GetParent(directory);
                if (parent == null) break;
                directory = parent.FullName;
            }

            return null;
        });
    }

    /// <summary>
    /// Finds the containing .sln file for a given project path
    /// </summary>
    /// <param name="projectPath">The project path to start searching from</param>
    /// <returns>The path to the containing solution file, or null if not found</returns>
    public async Task<string?> FindContainingSolutionAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            // Convert to absolute path if relative
            var absolutePath = Path.GetFullPath(projectPath);
            
            var directory = File.Exists(absolutePath) 
                ? Path.GetDirectoryName(absolutePath) 
                : absolutePath;

            while (!string.IsNullOrEmpty(directory))
            {
                try
                {
                    var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly);
                    if (slnFiles.Length > 0)
                    {
                        // Return the full absolute path
                        return Path.GetFullPath(slnFiles[0]);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we can't access
                }

                var parent = Directory.GetParent(directory);
                if (parent == null) break;
                directory = parent.FullName;
            }

            return null;
        });
    }

    /// <summary>
    /// Resolves the context type and path from a given path
    /// </summary>
    /// <param name="contextPath">The path to resolve</param>
    /// <returns>A tuple containing the context type and resolved path</returns>
    public async Task<(ContextType contextType, string resolvedPath)> ResolveContextAsync(string contextPath)
    {
        // Convert to absolute path if relative
        var absolutePath = Path.GetFullPath(contextPath);
        
        if (!File.Exists(absolutePath) && !Directory.Exists(absolutePath))
        {
            throw new FileNotFoundException($"Path not found: {absolutePath} (original: {contextPath})");
        }

        var extension = Path.GetExtension(absolutePath).ToLowerInvariant();

        switch (extension)
        {
            case ".sln":
                return (ContextType.Solution, absolutePath);

            case ".csproj":
                return (ContextType.Project, absolutePath);

            case ".cs":
            case ".vb":
                // For source files, find the containing project
                var projectPath = await FindContainingProjectAsync(absolutePath);
                if (projectPath != null)
                {
                    return (ContextType.Project, projectPath);
                }
                throw new InvalidOperationException($"No project file found containing: {absolutePath}");

            default:
                if (Directory.Exists(absolutePath))
                {
                    try
                    {
                        // Try to find a project in the directory
                        var csprojFiles = Directory.GetFiles(absolutePath, "*.csproj", SearchOption.TopDirectoryOnly);
                        if (csprojFiles.Length > 0)
                        {
                            return (ContextType.Project, Path.GetFullPath(csprojFiles[0]));
                        }

                        // Try to find a solution in the directory
                        var slnFiles = Directory.GetFiles(absolutePath, "*.sln", SearchOption.TopDirectoryOnly);
                        if (slnFiles.Length > 0)
                        {
                            return (ContextType.Solution, Path.GetFullPath(slnFiles[0]));
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        throw new InvalidOperationException($"Access denied to directory: {absolutePath}");
                    }
                }

                throw new InvalidOperationException($"Unable to determine context type for: {absolutePath}");
        }
    }
}