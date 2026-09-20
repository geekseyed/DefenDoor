using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class CoverageReportParserTests
{
    private readonly CoverageReportParser _parser;

    public CoverageReportParserTests()
    {
        _parser = new CoverageReportParser();
    }

    [Fact]
    public void Parse_ValidJson_ReturnsCoverageSession()
    {
        // Arrange
        var json = @"
        {
          ""Modules"": [
            {
              ""Name"": ""ISCM.Application"",
              ""Classes"": [
                {
                  ""Name"": ""MyService"",
                  ""Namespace"": ""ISCM.Application.Services"",
                  ""Methods"": [
                    {
                      ""Lines"": [
                        { ""Line"": 10, ""Hits"": 5 },
                        { ""Line"": 11, ""Hits"": 0 },
                        { ""Line"": 12, ""Hits"": 3 }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }";

        // Act
        var result = _parser.Parse(json);

        // Assert
        result.Should().NotBeNull();
        result.Modules.Count.Should().Be(1);
        result.Modules[0].AssemblyName.Should().Be("ISCM.Application");
        result.Modules[0].Classes.Count.Should().Be(1);

        var cls = result.Modules[0].Classes[0];
        cls.ClassName.Should().Be("MyService");
        cls.Lines.Count.Should().Be(3);
        cls.CoveredLines.Should().Be(2); // Lines 10 and 12 have hits > 0
        cls.TotalLines.Should().Be(3);

        result.CoveredLines.Should().Be(2);
        result.TotalLines.Should().Be(3);
        result.CoveragePercentage.Should().BeApproximately(66.67, 0.1);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsEmptySession()
    {
        // Act
        var result = _parser.Parse("");

        // Assert
        result.Modules.Count.Should().Be(0);
        result.CoveragePercentage.Should().Be(0);
    }

    [Fact]
    public void Parse_NoModules_ReturnsEmptySession()
    {
        // Arrange
        var json = @"{ ""Modules"": [] }";

        // Act
        var result = _parser.Parse(json);

        // Assert
        result.Modules.Count.Should().Be(0);
    }
}