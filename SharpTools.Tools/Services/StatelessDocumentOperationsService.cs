using Microsoft.Extensions.Logging;
using SharpTools.Tools.Interfaces;

namespace SharpTools.Tools.Services;

/// <summary>
/// Stateless version of DocumentOperationsService that works without a loaded solution.
/// Uses file path context to determine solution boundaries and appropriate permissions.
/// </summary>
public class StatelessDocumentOperationsService : IDocumentOperationsService {
    private readonly ILogger<StatelessDocumentOperationsService> _logger;
    private readonly string? _contextDirectory;

    // Extensions for common code file types that can be formatted
    private static readonly HashSet<string> CodeFileExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".cs", ".csproj", ".sln", ".css", ".js", ".ts", ".jsx", ".tsx", ".html", ".cshtml", ".razor", ".yml", ".yaml",
        ".json", ".xml", ".config", ".md", ".fs", ".fsx", ".fsi", ".vb"
    };

    private static readonly HashSet<string> UnsafeDirectories = new(StringComparer.OrdinalIgnoreCase) {
        ".git", ".vs", "bin", "obj", "node_modules"
    };

    /// <summary>
    /// Creates a stateless document operations service with optional context directory
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="contextDirectory">Optional context directory to use as the solution root for path validation</param>
    public StatelessDocumentOperationsService(
        ILogger<StatelessDocumentOperationsService> logger,
        string? contextDirectory = null) {
        _logger = logger;
        _contextDirectory = contextDirectory;
    }

    public async Task<(string contents, int lines)> ReadFileAsync(string filePath, bool omitLeadingSpaces, CancellationToken cancellationToken) {
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        if (!IsPathReadable(filePath)) {
            throw new UnauthorizedAccessException($"Reading from this path is not allowed: {filePath}");
        }

        string content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        if (omitLeadingSpaces) {
            for (int i = 0; i < lines.Length; i++) {
                lines[i] = TrimLeadingSpaces(lines[i]);
            }

            content = string.Join(Environment.NewLine, lines);
        }

        return (content, lines.Length);
    }

    public async Task<bool> WriteFileAsync(string filePath, string content, bool overwriteIfExists, CancellationToken cancellationToken) {
        var pathInfo = GetPathInfo(filePath);

        if (!pathInfo.IsWritable) {
            _logger.LogWarning("Path is not writable: {FilePath}. Reason: {Reason}",
                filePath, pathInfo.WriteRestrictionReason);
            throw new UnauthorizedAccessException($"Writing to this path is not allowed: {filePath}. {pathInfo.WriteRestrictionReason}");
        }

        if (File.Exists(filePath) && !overwriteIfExists) {
            _logger.LogWarning("File already exists and overwrite not allowed: {FilePath}", filePath);
            return false;
        }

        // Ensure directory exists
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }

        // Write the content to the file
        await File.WriteAllTextAsync(filePath, content, cancellationToken);
        _logger.LogInformation("File {Operation} at {FilePath}",
            File.Exists(filePath) ? "overwritten" : "created", filePath);

        return true;
    }

    public bool FileExists(string filePath) {
        return File.Exists(filePath);
    }

    public bool IsPathReadable(string filePath) {
        var pathInfo = GetPathInfo(filePath);
        return pathInfo.IsReadable;
    }

    public bool IsPathWritable(string filePath) {
        var pathInfo = GetPathInfo(filePath);
        return pathInfo.IsWritable;
    }

    public bool IsCodeFile(string filePath) {
        if (string.IsNullOrEmpty(filePath)) {
            return false;
        }

        // Check by extension
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && CodeFileExtensions.Contains(extension);
    }

    public PathInfo GetPathInfo(string filePath) {
        if (string.IsNullOrEmpty(filePath)) {
            return new PathInfo {
                FilePath = filePath,
                Exists = false,
                IsWithinSolutionDirectory = false,
                IsReferencedBySolution = false,
                IsFormattable = false,
                WriteRestrictionReason = "Path is empty or null"
            };
        }

        bool exists = File.Exists(filePath);
        bool isWithinSolution = IsPathWithinSolutionDirectory(filePath);
        bool isReferenced = false; // In stateless mode, we can't determine this easily
        bool isFormattable = IsCodeFile(filePath);

        string? writeRestrictionReason = null;

        // Check for unsafe directories
        if (ContainsUnsafeDirectory(filePath)) {
            writeRestrictionReason = "Path contains a protected directory (bin, obj, .git, etc.)";
        }

        // Check if file is outside solution (more permissive in stateless mode)
        if (!isWithinSolution) {
            // In stateless mode, we're more permissive - allow files that are in reasonable locations
            if (!IsReasonableFilePath(filePath)) {
                writeRestrictionReason = "Path appears to be outside of a development project directory";
            }
        }

        // Check if directory is read-only
        try {
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath)) {
                var dirInfo = new DirectoryInfo(directoryPath);
                if ((dirInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly) {
                    writeRestrictionReason = "Directory is read-only";
                }
            }
        } catch {
            writeRestrictionReason = "Cannot determine directory permissions";
        }

        return new PathInfo {
            FilePath = filePath,
            Exists = exists,
            IsWithinSolutionDirectory = isWithinSolution,
            IsReferencedBySolution = isReferenced,
            IsFormattable = isFormattable,
            ProjectId = null, // Cannot determine in stateless mode
            WriteRestrictionReason = writeRestrictionReason
        };
    }

    /// <summary>
    /// Determines if a path is within the solution directory using context-based detection
    /// </summary>
    private bool IsPathWithinSolutionDirectory(string filePath) {
        if (!string.IsNullOrEmpty(_contextDirectory)) {
            // Use provided context directory
            return filePath.StartsWith(_contextDirectory, StringComparison.OrdinalIgnoreCase);
        }

        // Try to find solution directory by looking for .sln files or common project indicators
        var solutionDirectory = FindSolutionDirectory(filePath);
        if (!string.IsNullOrEmpty(solutionDirectory)) {
            return filePath.StartsWith(solutionDirectory, StringComparison.OrdinalIgnoreCase);
        }

        // Fallback: check if it's in a reasonable project-like directory structure
        return IsReasonableFilePath(filePath);
    }

    /// <summary>
    /// Attempts to find the solution directory by walking up the directory tree
    /// </summary>
    private string? FindSolutionDirectory(string filePath) {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

        while (!string.IsNullOrEmpty(directory)) {
            // Look for .sln files
            if (Directory.GetFiles(directory, "*.sln").Any()) {
                return directory;
            }

            // Look for other indicators of a solution root
            if (Directory.GetFiles(directory, "*.csproj").Any() ||
                Directory.GetFiles(directory, "*.fsproj").Any() ||
                Directory.GetFiles(directory, "*.vbproj").Any() ||
                File.Exists(Path.Combine(directory, ".gitignore")) ||
                Directory.Exists(Path.Combine(directory, ".git"))) {
                return directory;
            }

            // Move up one level
            var parent = Directory.GetParent(directory);
            if (parent == null) {
                break;
            }
            directory = parent.FullName;
        }

        return null;
    }

    /// <summary>
    /// Check if a file path appears to be in a reasonable development location
    /// </summary>
    private bool IsReasonableFilePath(string filePath) {
        var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();

        // Allow files in common development directories
        var reasonablePaths = new[] {
            "/source/", "/src/", "/projects/", "/code/", "/dev/", "/repos/",
            "/documents/", "/desktop/", "/users/"
        };

        return reasonablePaths.Any(path => normalizedPath.Contains(path));
    }

    private bool ContainsUnsafeDirectory(string filePath) {
        // Check if the path contains any unsafe directory segments
        var normalizedPath = filePath.Replace('\\', '/');
        var pathSegments = normalizedPath.Split('/');

        return pathSegments.Any(segment => UnsafeDirectories.Contains(segment));
    }

    private static string TrimLeadingSpaces(string line) {
        int i = 0;
        while (i < line.Length && char.IsWhiteSpace(line[i])) {
            i++;
        }

        return i > 0 ? line.Substring(i) : line;
    }
}
