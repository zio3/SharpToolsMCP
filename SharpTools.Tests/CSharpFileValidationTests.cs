using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;
using SharpTools.Tools.Mcp.Helpers;
using System;
using System.IO;

namespace SharpTools.Tests;

[TestClass]
public class CSharpFileValidationTests
{
    private readonly NullLogger<object> _logger = new();

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_CSharpFile_ShouldPass()
    {
        // Arrange
        var filePath = "TestFile.cs";
        var toolName = "TestTool";

        // Act & Assert - Should not throw
        CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger);
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_CSharpScriptFile_ShouldPass()
    {
        // Arrange
        var filePath = "TestScript.csx";
        var toolName = "TestTool";

        // Act & Assert - Should not throw
        CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger);
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_SolutionFile_ShouldPass()
    {
        // Arrange
        var filePath = "MySolution.sln";
        var toolName = "TestTool";

        // Act & Assert - Should not throw
        CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger);
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_ProjectFile_ShouldPass()
    {
        // Arrange
        var filePath = "MyProject.csproj";
        var toolName = "TestTool";

        // Act & Assert - Should not throw
        CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger);
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_VBProjectFile_ShouldPass()
    {
        // Arrange
        var filePath = "MyProject.vbproj";
        var toolName = "TestTool";

        // Act & Assert - Should not throw
        CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger);
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_PythonFile_ShouldThrow()
    {
        // Arrange
        var filePath = "test_script.py";
        var toolName = "TestTool";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger));

        Assert.IsTrue(ex.Message.Contains("❌ LANGUAGE_MISMATCH"));
        Assert.IsTrue(ex.Message.Contains("Python"));
        Assert.IsTrue(ex.Message.Contains("TestTool"));
        Assert.IsTrue(ex.Message.Contains(".py"));
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_JavaScriptFile_ShouldThrow()
    {
        // Arrange
        var filePath = "app.js";
        var toolName = "TestTool";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger));

        Assert.IsTrue(ex.Message.Contains("❌ LANGUAGE_MISMATCH"));
        Assert.IsTrue(ex.Message.Contains("JavaScript"));
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_TextFile_ShouldThrow()
    {
        // Arrange
        var filePath = "readme.txt";
        var toolName = "TestTool";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger));

        Assert.IsTrue(ex.Message.Contains("❌ LANGUAGE_MISMATCH"));
        Assert.IsTrue(ex.Message.Contains("Text"));
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_NoExtension_ShouldThrow()
    {
        // Arrange
        var filePath = "somefile";
        var toolName = "TestTool";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger));

        Assert.IsTrue(ex.Message.Contains("❌ LANGUAGE_MISMATCH"));
        Assert.IsTrue(ex.Message.Contains("拡張子なし"));
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_NullFilePath_ShouldThrow()
    {
        // Arrange
        string? filePath = null;
        var toolName = "TestTool";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath!, toolName, _logger));

        Assert.IsTrue(ex.Message.Contains("ファイルパスが指定されていません"));
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_EmptyFilePath_ShouldThrow()
    {
        // Arrange
        var filePath = "";
        var toolName = "TestTool";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger));

        Assert.IsTrue(ex.Message.Contains("ファイルパスが指定されていません"));
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_UnknownExtension_ShouldThrow()
    {
        // Arrange
        var filePath = "document.xyz";
        var toolName = "TestTool";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger));

        Assert.IsTrue(ex.Message.Contains("❌ LANGUAGE_MISMATCH"));
        Assert.IsTrue(ex.Message.Contains("不明な形式"));
    }

    [TestMethod]
    public void ValidateDotNetFileForRoslyn_ErrorMessage_ContainsAllRequiredElements()
    {
        // Arrange
        var filePath = "/path/to/test_module.py";
        var toolName = "GetMembers";

        // Act & Assert
        var ex = Assert.ThrowsException<McpException>(() =>
            CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, toolName, _logger));

        // エラーメッセージの必須要素を確認
        Assert.IsTrue(ex.Message.Contains("❌ LANGUAGE_MISMATCH"), "Error prefix missing");
        Assert.IsTrue(ex.Message.Contains("GetMembers"), "Tool name missing");
        Assert.IsTrue(ex.Message.Contains("test_module.py"), "File name missing");
        Assert.IsTrue(ex.Message.Contains(".py"), "Extension missing");
        Assert.IsTrue(ex.Message.Contains("Python"), "Detected language missing");
        Assert.IsTrue(ex.Message.Contains(".cs, .csx, .sln, .csproj"), "Supported formats missing");
        Assert.IsTrue(ex.Message.Contains("SharpToolsは.NET/C#プロジェクト専用"), "Purpose statement missing");
    }
}