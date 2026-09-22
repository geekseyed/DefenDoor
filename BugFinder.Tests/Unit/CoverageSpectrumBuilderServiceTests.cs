using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class CoverageSpectrumBuilderServiceTests
{
    private readonly CoverageSpectrumBuilderService _builder;

    public CoverageSpectrumBuilderServiceTests()
    {
        _builder = new CoverageSpectrumBuilderService();
    }

    [Fact]
    public void BuildSpectrum_CalculatesFourCounters_Correctly()
    {
        // Arrange
        // Scenario: 
        // Line A: Executed by 1 Pass, 1 Fail
        // Line B: Executed by 1 Pass, Skipped by 1 Fail
        // Line C: Skipped by 1 Pass, Executed by 1 Fail

        var inputs = new List<TestCoverageInput>
        {
            new TestCoverageInput
            {
                TestName = "Test_Pass_1",
                IsPassed = true,
                CoveredLines = new List<string> { "File.cs:10", "File.cs:20" } // Covers A, B
            },
            new TestCoverageInput
            {
                TestName = "Test_Fail_1",
                IsPassed = false,
                CoveredLines = new List<string> { "File.cs:10", "File.cs:30" } // Covers A, C
            }
        };

        // Act
        var spectra = _builder.BuildSpectrum(inputs).ToList();

        // Assert
        spectra.Should().HaveCount(3); // Lines 10, 20, 30

        // Line 10 (A): Executed by Pass(1), Fail(1). Skipped by None.
        var line10 = spectra.First(s => s.ElementId == "File.cs:10");
        line10.PassedCount.Should().Be(1);
        line10.FailedCount.Should().Be(1);
        line10.PassSkipCount.Should().Be(0);
        line10.FailSkipCount.Should().Be(0);

        // Line 20 (B): Executed by Pass(1). Skipped by Fail(1).
        var line20 = spectra.First(s => s.ElementId == "File.cs:20");
        line20.PassedCount.Should().Be(1);
        line20.FailedCount.Should().Be(0);
        line20.PassSkipCount.Should().Be(0);
        line20.FailSkipCount.Should().Be(1);

        // Line 30 (C): Executed by Fail(1). Skipped by Pass(1).
        var line30 = spectra.First(s => s.ElementId == "File.cs:30");
        line30.PassedCount.Should().Be(0);
        line30.FailedCount.Should().Be(1);
        line30.PassSkipCount.Should().Be(1);
        line30.FailSkipCount.Should().Be(0);
    }

    [Fact]
    public void BuildSpectrum_ReturnsEmpty_WhenInputIsEmpty()
    {
        // Arrange
        var inputs = new List<TestCoverageInput>();

        // Act
        var spectra = _builder.BuildSpectrum(inputs);

        // Assert
        spectra.Should().BeEmpty();
    }

    [Fact]
    public void BuildSpectrum_HandlesAllPasses_Correctly()
    {
        // Arrange
        var inputs = new List<TestCoverageInput>
        {
            new TestCoverageInput { TestName = "T1", IsPassed = true, CoveredLines = new List<string> { "File.cs:5" } },
            new TestCoverageInput { TestName = "T2", IsPassed = true, CoveredLines = new List<string> { "File.cs:5" } }
        };

        // Act
        var spectra = _builder.BuildSpectrum(inputs).ToList();

        // Assert
        var line5 = spectra.First();
        line5.PassedCount.Should().Be(2);
        line5.FailedCount.Should().Be(0);
        line5.FailSkipCount.Should().Be(0); // No failures exist in input
    }
}