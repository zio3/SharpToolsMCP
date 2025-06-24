using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTools.Tools.Services;

/// <summary>
/// Factory for creating stateless Roslyn workspaces for various contexts
/// </summary>
public class StatelessWorkspaceFactory
{
    private readonly ILogger<StatelessWorkspaceFactory> _logger;
    private readonly ProjectDiscoveryService _projectDiscovery;

    static StatelessWorkspaceFactory()
    {
        // Ensure MSBuild is registered once
        // Check both IsRegistered and CanRegister to avoid conflicts
        if (!MSBuildLocator.IsRegistered && MSBuildLocator.CanRegister)
        {
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch (InvalidOperationException ex)
            {
                // MSBuild is already registered by another component
                // This is expected when SolutionManager is already loaded
                System.Diagnostics.Debug.WriteLine($"MSBuildLocator registration skipped: {ex.Message}");
            }
        }
    }

    public StatelessWorkspaceFactory(ILogger<StatelessWorkspaceFactory> logger, ProjectDiscoveryService projectDiscovery)
    {
        _logger = logger;
        _projectDiscovery = projectDiscovery;
    }

    /// <summary>
    /// Creates a workspace for a specific project
    /// </summary>
    /// <param name="projectPath">Path to the .csproj file</param>
    /// <returns>A tuple containing the workspace and project</returns>
    public async Task<(MSBuildWorkspace workspace, Project project)> CreateForProjectAsync(string projectPath)
    {
        _logger.LogInformation("Creating workspace for project: {ProjectPath}", projectPath);
        
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true"
        });

        var hasErrors = false;
        var errorMessages = new List<string>();
        
        workspace.WorkspaceFailed += (sender, args) =>
        {
            var message = $"[{args.Diagnostic.Kind}] {args.Diagnostic.Message}";
            errorMessages.Add(message);
            _logger.LogWarning("Workspace diagnostic: {Message}", message);
            
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                hasErrors = true;
            }
        };

        try
        {
            var project = await workspace.OpenProjectAsync(projectPath);
            
            if (hasErrors)
            {
                var errorDetail = string.Join("; ", errorMessages);
                throw new InvalidOperationException($"Failed to load project '{projectPath}'. Errors: {errorDetail}");
            }
            
            if (project == null)
            {
                throw new InvalidOperationException($"Project '{projectPath}' loaded but returned null.");
            }
            
            _logger.LogInformation("Successfully loaded project: {ProjectName} with {DocumentCount} documents", 
                project.Name, project.Documents.Count());
            
            return (workspace, project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workspace for project: {ProjectPath}", projectPath);
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a workspace for a specific solution
    /// </summary>
    /// <param name="solutionPath">Path to the .sln file</param>
    /// <returns>A tuple containing the workspace and solution</returns>
    public async Task<(MSBuildWorkspace workspace, Solution solution)> CreateForSolutionAsync(string solutionPath)
    {
        _logger.LogInformation("Creating workspace for solution: {SolutionPath}", solutionPath);
        
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true"
        });

        var hasErrors = false;
        var errorMessages = new List<string>();
        
        workspace.WorkspaceFailed += (sender, args) =>
        {
            var message = $"[{args.Diagnostic.Kind}] {args.Diagnostic.Message}";
            errorMessages.Add(message);
            _logger.LogWarning("Workspace diagnostic: {Message}", message);
            
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                hasErrors = true;
            }
        };

        try
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath);
            
            if (hasErrors)
            {
                var errorDetail = string.Join("; ", errorMessages);
                throw new InvalidOperationException($"Failed to load solution '{solutionPath}'. Errors: {errorDetail}");
            }
            
            if (solution == null)
            {
                throw new InvalidOperationException($"Solution '{solutionPath}' loaded but returned null.");
            }
            
            _logger.LogInformation("Successfully loaded solution: {SolutionName} with {ProjectCount} projects", 
                Path.GetFileNameWithoutExtension(solutionPath), solution.Projects.Count());
            
            return (workspace, solution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workspace for solution: {SolutionPath}", solutionPath);
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a workspace for a specific file by discovering its containing project
    /// </summary>
    /// <param name="filePath">Path to the source file</param>
    /// <returns>A tuple containing the workspace, project, and document</returns>
    public async Task<(MSBuildWorkspace workspace, Project project, Document? document)> CreateForFileAsync(string filePath)
    {
        _logger.LogInformation("Creating workspace for file: {FilePath}", filePath);
        
        // Convert to absolute path
        var absoluteFilePath = Path.GetFullPath(filePath);
        
        // Discover the containing project
        var projectPath = await _projectDiscovery.FindContainingProjectAsync(absoluteFilePath);
        if (projectPath == null)
        {
            throw new InvalidOperationException($"No project file found containing: {absoluteFilePath}");
        }

        _logger.LogInformation("Found containing project: {ProjectPath}", projectPath);
        
        var (workspace, project) = await CreateForProjectAsync(projectPath);

        try
        {
            // Find the document in the project
            var document = project.Documents.FirstOrDefault(d =>
                string.Equals(Path.GetFullPath(d.FilePath ?? ""), absoluteFilePath, StringComparison.OrdinalIgnoreCase));

            if (document == null)
            {
                _logger.LogWarning("Document not found in project. File: {FilePath}, Project documents: {DocumentPaths}", 
                    absoluteFilePath, 
                    string.Join(", ", project.Documents.Select(d => d.FilePath ?? "unknown")));
            }

            return (workspace, project, document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find document in project. File: {FilePath}", absoluteFilePath);
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a workspace based on the context path (file, project, or solution)
    /// </summary>
    /// <param name="contextPath">Path to a file, project, or solution</param>
    /// <returns>A tuple containing the workspace and relevant context objects</returns>
    public async Task<(MSBuildWorkspace workspace, object context, ProjectDiscoveryService.ContextType contextType)> CreateForContextAsync(string contextPath)
    {
        var (contextType, resolvedPath) = await _projectDiscovery.ResolveContextAsync(contextPath);

        switch (contextType)
        {
            case ProjectDiscoveryService.ContextType.Solution:
                var (solutionWorkspace, solution) = await CreateForSolutionAsync(resolvedPath);
                return (solutionWorkspace, solution, contextType);

            case ProjectDiscoveryService.ContextType.Project:
                var (projectWorkspace, project) = await CreateForProjectAsync(resolvedPath);
                return (projectWorkspace, project, contextType);

            case ProjectDiscoveryService.ContextType.File:
                var (fileWorkspace, fileProject, document) = await CreateForFileAsync(contextPath);
                return (fileWorkspace, new { Project = fileProject, Document = document }, contextType);

            default:
                throw new InvalidOperationException($"Unknown context type: {contextType}");
        }
    }
}