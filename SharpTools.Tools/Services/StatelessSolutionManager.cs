using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SharpTools.Tools.Interfaces;
using System.Diagnostics.CodeAnalysis;

namespace SharpTools.Tools.Services;

/// <summary>
/// A minimal implementation of ISolutionManager for stateless operations
/// </summary>
internal class StatelessSolutionManager : ISolutionManager
{
    private readonly Solution _solution;

    public StatelessSolutionManager(Solution solution)
    {
        _solution = solution ?? throw new ArgumentNullException(nameof(solution));
    }

    public bool IsSolutionLoaded => true;

    public MSBuildWorkspace? CurrentWorkspace => null;
    public Solution? CurrentSolution => _solution;

    public void Dispose() { }

    public Task<ISymbol?> FindRoslynSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Use IFuzzyFqnLookupService.FindMatchesAsync instead for stateless operations");
    }

    public Task<INamedTypeSymbol?> FindRoslynNamedTypeSymbolAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Use IFuzzyFqnLookupService.FindMatchesAsync instead for stateless operations");
    }

    public Task<Type?> FindReflectionTypeAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Reflection operations are not supported in stateless mode");
    }

    public IEnumerable<Project> GetProjects()
    {
        return _solution.Projects;
    }

    public Project? GetProjectByName(string projectName)
    {
        return _solution.Projects.FirstOrDefault(p => p.Name == projectName);
    }

    public Task<SemanticModel?> GetSemanticModelAsync(DocumentId documentId, CancellationToken cancellationToken)
    {
        var document = _solution.GetDocument(documentId);
        return document?.GetSemanticModelAsync(cancellationToken) ?? Task.FromResult<SemanticModel?>(null);
    }

    public Task<Compilation?> GetCompilationAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        var project = _solution.GetProject(projectId);
        return project?.GetCompilationAsync(cancellationToken) ?? Task.FromResult<Compilation?>(null);
    }

    public Task LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Solution loading is not supported in stateless mode");
    }

    public void RefreshCurrentSolution()
    {
        // No-op for stateless
    }

    public Task ReloadSolutionFromDiskAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Solution reloading is not supported in stateless mode");
    }

    public Task<IEnumerable<Type>> SearchReflectionTypesAsync(string regexPattern, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Reflection operations are not supported in stateless mode");
    }

    public void UnloadSolution()
    {
        // No-op for stateless
    }
}