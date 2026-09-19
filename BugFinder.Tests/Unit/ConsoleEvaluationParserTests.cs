using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder; // این.namespace باید با ساختار پوشه همخوانی داشته باشد

public class ConsoleEvaluationParserTests
{
    [Fact] // دقت کنید که Fact باشد نه چیزی دیگر
    public void Parse_EvaluationLine_ExtractsSubControlAndStatus()
    {
        var parser = new ConsoleEvaluationParser();
        var line = "  EVL-001.4: Fail (Reason=Integer 0 is less than 32768.)";

        var result = parser.ParseLine(line);

        result.Should().NotBeNull();
        result.SubControlId.Should().Be("EVL-001.4");
        result.Status.Should().Be(Domain.Enums.CheckStatus.Fail);
        result.Reason.Should().Contain("less than");
    }

    [Fact]
    public void Parse_NonEvaluationLine_ReturnsNull()
    {
        var parser = new ConsoleEvaluationParser();
        var line = "=== EventLogSizeCheck - Evidence ===";

        var result = parser.ParseLine(line);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_FullOutput_ExtractsMultipleFailures()
    {
        // Arrange
        var parser = new ConsoleEvaluationParser();
        var output = @"
=== EventLogSizeCheck - Evaluation ===
  EVL-001.1: Pass (Reason=Integer 20971520 >= 65536.)
  EVL-001.2: Fail (Reason=Integer 0 is less than 131072.)
  EVL-001.3: Pass (Reason=Integer 20971520 >= 65536.)
=== RdpNlaCheck - Evaluation ===
  RDP-001.2: Fail (Reason=Integer 0 is less than 3.)
";

        // Act
        var results = parser.Parse(output);

        // Assert - کل خطوط ارزیابی شده ۴ تاست (۲ Pass و ۲ Fail)
        results.Count.Should().Be(4);

        // بررسی صحت استخراج Failها
        var failures = results.Where(r => r.Status == Domain.Enums.CheckStatus.Fail).ToList();
        failures.Count.Should().Be(2);
        failures.Should().Contain(r => r.SubControlId == "EVL-001.2");
        failures.Should().Contain(r => r.SubControlId == "RDP-001.2");
    }
}