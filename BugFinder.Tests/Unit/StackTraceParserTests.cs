using ISCM.BugFinder.Core.Services;
using ISCM.BugFinder.Core.Models;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class StackTraceParserTests
{
    private readonly StackTraceParser _parser;

    public StackTraceParserTests()
    {
        _parser = new StackTraceParser();
    }

    [Fact]
    public void Parse_WithValidStackTrace_ExtractsFrames()
    {
        // Arrange
        var rawTrace = @"   at ISCM.Application.Scanner.ScanAsync() in C:\Repo\ISCM\Application\Scanner.cs:line 42
   at ISCM.Tests.Integration.ScannerTest.TestScan() in C:\Repo\ISCM\Tests\ScannerTest.cs:line 15";

        // Act
        var result = _parser.Parse(rawTrace);

        // Assert
        result.IsMalformed.Should().BeFalse();
        result.Frames.Count.Should().Be(2);
        result.Frames[0].MethodName.Should().Be("ScanAsync");
        result.Frames[0].FilePath.Should().Contain("Scanner.cs");
        result.Frames[0].LineNumber.Should().Be(42);
        result.Frames[0].Kind.Should().Be(FrameKind.Application);
    }

    [Fact]
    public void Parse_WithFrameworkFrame_ClassifiesAsFramework()
    {
        // Arrange
        var rawTrace = @"   at System.Threading.Tasks.Task.Execute()";

        // Act
        var result = _parser.Parse(rawTrace);

        // Assert
        result.Frames.Count.Should().Be(1);
        result.Frames[0].Kind.Should().Be(FrameKind.Framework);
    }

    [Fact]
    public void Parse_WithNullTrace_ReturnsMalformedResult()
    {
        // Act
        var result = _parser.Parse(null);

        // Assert
        result.IsMalformed.Should().BeTrue();
        result.Frames.Count.Should().Be(0);
    }

    [Fact]
    public void Parse_WithTestFrame_ClassifiesAsTest()
    {
        // Arrange
        var rawTrace = @"   at ISCM.Tests.Unit.MyTest.TestMethod() in C:\Repo\Tests\MyTest.cs:line 10";

        // Act
        var result = _parser.Parse(rawTrace);

        // Assert
        result.Frames[0].Kind.Should().Be(FrameKind.Test);
    }
}