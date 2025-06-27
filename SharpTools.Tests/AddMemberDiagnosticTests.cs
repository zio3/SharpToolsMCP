using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Mcp.Models;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Services;
using System.Text.Json;

namespace SharpTools.Tests;

[TestClass]
public class AddMemberDiagnosticTests
{
    private string _tempDirectory;
    private StatelessWorkspaceFactory _workspaceFactory;
    private ICodeAnalysisService _codeAnalysisService;
    private Mock<ICodeModificationService> _mockModificationService;
    private Mock<IComplexityAnalysisService> _mockComplexityAnalysisService;
    private Mock<ISemanticSimilarityService> _mockSemanticSimilarityService;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{System.Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        var projectDiscovery = new ProjectDiscoveryService();
        _workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, projectDiscovery);
        var mockSolutionManager = new Mock<ISolutionManager>();
        _codeAnalysisService = new CodeAnalysisService(mockSolutionManager.Object, NullLogger<CodeAnalysisService>.Instance);
        
        _mockModificationService = new Mock<ICodeModificationService>();
        _mockModificationService.Setup(m => m.FormatDocumentAsync(It.IsAny<Microsoft.CodeAnalysis.Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.CodeAnalysis.Document doc, CancellationToken ct) => doc);
        
        _mockComplexityAnalysisService = new Mock<IComplexityAnalysisService>();
        _mockSemanticSimilarityService = new Mock<ISemanticSimilarityService>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    public async Task AddMember_CleanProject_ShouldShowCleanStatus()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "TestClass.cs");
        var testContent = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string ExistingProperty { get; set; }
    }
}";
        File.WriteAllText(testFile, testContent);
        File.WriteAllText(Path.Combine(_tempDirectory, "Test.csproj"), @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

        var memberCode = @"public string NewMethod() { return ""test""; }";
        
        // Act
        var result = await ModificationTools.AddMember(
            _workspaceFactory,
            _mockModificationService.Object,
            _codeAnalysisService,
            _mockComplexityAnalysisService.Object,
            _mockSemanticSimilarityService.Object,
            NullLogger<ModificationToolsLogCategory>.Instance,
            testFile,
            memberCode,
            "TestClass");

        // Assert
        var addMemberResult = result as AddMemberResult;
        Assert.IsNotNull(addMemberResult, "Result should be AddMemberResult");
        Assert.IsTrue(addMemberResult.Success, "Operation should succeed");
        
        // Check detailed compilation status
        Assert.IsNotNull(addMemberResult.CompilationStatus, "Should have detailed compilation status");
        
        var detailed = addMemberResult.CompilationStatus;
        Assert.AreEqual(0, detailed.BeforeOperation.ErrorCount, "Should have no errors before");
        Assert.AreEqual(0, detailed.AfterOperation.ErrorCount, "Should have no errors after");
        Assert.AreEqual("clean", detailed.OperationImpact.Status, "Should have clean status");
        Assert.AreEqual(0, detailed.OperationImpact.ErrorChange, "Should have no error change");
        
        TestContext.WriteLine($"Detailed status: {JsonSerializer.Serialize(detailed, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [TestMethod]
    public async Task AddMember_ProjectWithExistingErrors_ShouldMaintainErrors()
    {
        // Arrange - Create a file with compilation errors
        var testFile = Path.Combine(_tempDirectory, "TestClass.cs");
        var testContent = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string ExistingProperty { get; set; }
        
        // This will cause a compilation error - undefined type
        public UndefinedType ErrorMethod() 
        { 
            return new UndefinedType(); 
        }
    }
}";
        File.WriteAllText(testFile, testContent);
        File.WriteAllText(Path.Combine(_tempDirectory, "Test.csproj"), @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

        var memberCode = @"public string NewMethod() { return ""test""; }";
        
        // Act
        var result = await ModificationTools.AddMember(
            _workspaceFactory,
            _mockModificationService.Object,
            _codeAnalysisService,
            _mockComplexityAnalysisService.Object,
            _mockSemanticSimilarityService.Object,
            NullLogger<ModificationToolsLogCategory>.Instance,
            testFile,
            memberCode,
            "TestClass");

        // Assert
        var addMemberResult = result as AddMemberResult;
        Assert.IsNotNull(addMemberResult, "Result should be AddMemberResult");
        Assert.IsTrue(addMemberResult.Success, "Operation should succeed despite existing errors");
        
        // Check detailed compilation status
        Assert.IsNotNull(addMemberResult.CompilationStatus, "Should have detailed compilation status");
        
        var detailed = addMemberResult.CompilationStatus;
        Assert.IsTrue(detailed.BeforeOperation.ErrorCount > 0, "Should have errors before operation");
        Assert.IsTrue(detailed.AfterOperation.ErrorCount > 0, "Should still have errors after operation");
        Assert.AreEqual(0, detailed.OperationImpact.ErrorChange, "Should have no new errors");
        Assert.AreEqual("clean", detailed.OperationImpact.Status, "Should have clean status - no new errors");
        
        TestContext.WriteLine($"Before errors: {detailed.BeforeOperation.ErrorCount}");
        TestContext.WriteLine($"After errors: {detailed.AfterOperation.ErrorCount}");
        TestContext.WriteLine($"Status: {detailed.OperationImpact.Status}");
    }

    [TestMethod]
    public async Task AddMember_CausesNewError_ShouldShowAttentionRequired()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "TestClass.cs");
        var testContent = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string ExistingProperty { get; set; }
    }
}";
        File.WriteAllText(testFile, testContent);
        File.WriteAllText(Path.Combine(_tempDirectory, "Test.csproj"), @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

        // Member code that will cause a compilation error
        var memberCode = @"public UndefinedReturnType NewMethod() { return new UndefinedReturnType(); }";
        
        // Act
        var result = await ModificationTools.AddMember(
            _workspaceFactory,
            _mockModificationService.Object,
            _codeAnalysisService,
            _mockComplexityAnalysisService.Object,
            _mockSemanticSimilarityService.Object,
            NullLogger<ModificationToolsLogCategory>.Instance,
            testFile,
            memberCode,
            "TestClass");

        // Assert
        var addMemberResult = result as AddMemberResult;
        Assert.IsNotNull(addMemberResult, "Result should be AddMemberResult");
        Assert.IsTrue(addMemberResult.Success, "Operation should succeed (member added)");
        
        // Check detailed compilation status
        Assert.IsNotNull(addMemberResult.CompilationStatus, "Should have detailed compilation status");
        
        var detailed = addMemberResult.CompilationStatus;
        Assert.AreEqual(0, detailed.BeforeOperation.ErrorCount, "Should have no errors before");
        Assert.IsTrue(detailed.AfterOperation.ErrorCount > 0, "Should have errors after");
        Assert.IsTrue(detailed.OperationImpact.ErrorChange > 0, "Should have positive error change");
        Assert.AreEqual("attention_required", detailed.OperationImpact.Status, "Should require attention");
        Assert.IsTrue(detailed.OperationImpact.Note.Contains("新規エラー"), "Note should mention new errors");
        
        TestContext.WriteLine($"Error change: {detailed.OperationImpact.ErrorChange}");
        TestContext.WriteLine($"Status: {detailed.OperationImpact.Status}");
        TestContext.WriteLine($"Note: {detailed.OperationImpact.Note}");
    }

    [TestMethod]
    public async Task AddMember_CausesWarningOnly_ShouldShowWarningOnlyStatus()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "TestClass.cs");
        var testContent = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string ExistingProperty { get; set; }
    }
}";
        File.WriteAllText(testFile, testContent);
        File.WriteAllText(Path.Combine(_tempDirectory, "Test.csproj"), @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

        // Member code that might cause a warning (e.g., unused variable)
        var memberCode = @"
public string MethodWithWarning() 
{ 
    string unusedVariable = ""unused"";
    return ""test""; 
}";
        
        // Act
        var result = await ModificationTools.AddMember(
            _workspaceFactory,
            _mockModificationService.Object,
            _codeAnalysisService,
            _mockComplexityAnalysisService.Object,
            _mockSemanticSimilarityService.Object,
            NullLogger<ModificationToolsLogCategory>.Instance,
            testFile,
            memberCode,
            "TestClass");

        // Assert
        var addMemberResult = result as AddMemberResult;
        Assert.IsNotNull(addMemberResult, "Result should be AddMemberResult");
        Assert.IsTrue(addMemberResult.Success, "Operation should succeed");
        
        // Check detailed compilation status
        if (addMemberResult.CompilationStatus != null)
        {
            var detailed = addMemberResult.CompilationStatus;
            Assert.AreEqual(0, detailed.OperationImpact.ErrorChange, "Should have no error change");
            
            // If warnings increased, status should be warning_only
            if (detailed.OperationImpact.WarningChange > 0)
            {
                Assert.AreEqual("warning_only", detailed.OperationImpact.Status, "Should be warning only status");
            }
            else
            {
                Assert.AreEqual("clean", detailed.OperationImpact.Status, "Should be clean if no warnings");
            }
            
            TestContext.WriteLine($"Warning change: {detailed.OperationImpact.WarningChange}");
            TestContext.WriteLine($"Status: {detailed.OperationImpact.Status}");
        }
    }
}