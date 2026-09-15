namespace ISCM.Application.Reporting;

/// <summary>
/// Configuration for a single section within a report template.
/// </summary>
public sealed class TemplateSectionConfig
{
    public string Key { get; init; } = string.Empty;
    public string TitleEn { get; init; } = string.Empty;
    public string TitleFa { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public int Order { get; init; }
}