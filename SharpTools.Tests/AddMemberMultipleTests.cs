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
public class AddMemberMultipleTests {
    private string _tempDirectory;
    private StatelessWorkspaceFactory _workspaceFactory;
    private ICodeAnalysisService _codeAnalysisService;
    private Mock<ICodeModificationService> _mockModificationService;
    private Mock<IComplexityAnalysisService> _mockComplexityAnalysisService;
    private Mock<ISemanticSimilarityService> _mockSemanticSimilarityService;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup() {
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
    public void Cleanup() {
        if (Directory.Exists(_tempDirectory)) {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    public async Task AddMember_SingleMember_ShouldReturnStructuredResult() {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "TestClass.cs");
        var testContent = @"
namespace TestNamespace {
    public class TestClass {
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
        Assert.AreEqual(1, addMemberResult.AddedMembers.Count, "Should have one added member");
        Assert.AreEqual("NewMethod", addMemberResult.AddedMembers[0].Name);
        Assert.AreEqual("Method", addMemberResult.AddedMembers[0].Type);
        Assert.AreEqual(1, addMemberResult.Statistics.TotalAdded);
        Assert.AreEqual(1, addMemberResult.Statistics.MethodCount);
        Assert.AreEqual(0, addMemberResult.Statistics.PropertyCount);
    }

    [TestMethod]
    public async Task AddMember_MultipleMembers_ShouldReturnAllMembers() {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "TestClass.cs");
        var testContent = @"
namespace TestNamespace {
    public class TestClass {
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

        var memberCode = @"
public string TestMethod() { return ""test""; }

public int TestProperty { get; set; }

private bool _testField = false;
";
        
        // Debug member code
        Console.WriteLine("=== Member Code ===");
        Console.WriteLine(memberCode);
        Console.WriteLine("===================");
        
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
        
        // Debug output
        Console.WriteLine($"Success: {addMemberResult.Success}");
        Console.WriteLine($"Added members count: {addMemberResult.AddedMembers.Count}");
        Console.WriteLine($"Message: {addMemberResult.Message}");
        foreach (var member in addMemberResult.AddedMembers) {
            Console.WriteLine($"- {member.Type}: {member.Name}");
        }
        
        Assert.IsTrue(addMemberResult.Success, "Operation should succeed");
        Assert.AreEqual(3, addMemberResult.AddedMembers.Count, "Should have three added members");
        
        // Check method
        var method = addMemberResult.AddedMembers.FirstOrDefault(m => m.Type == "Method");
        Assert.IsNotNull(method);
        Assert.AreEqual("TestMethod", method.Name);
        
        // Check property
        var property = addMemberResult.AddedMembers.FirstOrDefault(m => m.Type == "Property");
        Assert.IsNotNull(property);
        Assert.AreEqual("TestProperty", property.Name);
        
        // Check field
        var field = addMemberResult.AddedMembers.FirstOrDefault(m => m.Type == "Field");
        Assert.IsNotNull(field);
        Assert.AreEqual("_testField", field.Name);
        
        // Check statistics
        Assert.AreEqual(3, addMemberResult.Statistics.TotalAdded);
        Assert.AreEqual(1, addMemberResult.Statistics.MethodCount);
        Assert.AreEqual(1, addMemberResult.Statistics.PropertyCount);
        Assert.AreEqual(1, addMemberResult.Statistics.FieldCount);
    }
}