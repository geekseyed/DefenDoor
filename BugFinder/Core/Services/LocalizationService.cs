using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-03.2: Localization Engine
/// Analyzes parsed stack frames to determine the primary failure location.
/// </summary>
public class LocalizationService
{
    /// <summary>
    /// Determines the primary location and confidence level from a list of stack frames.
    /// Strategy: Find the first Application frame that is called by a Test frame.
    /// </summary>
    public FailureLocalization? Localize(List<StackFrame> frames)
    {
        if (frames == null || frames.Count == 0) return null;

        // Strategy: Iterate from top (index 0) down.
        // Look for the first frame that is 'Application' code.
        // Skip Framework, External, and Test frames at the very top.

        var appFrame = frames.FirstOrDefault(f => f.Kind == FrameKind.Application);

        if (appFrame == null)
        {
            // Fallback: If no Application frame found, return the first non-framework frame available
            appFrame = frames.FirstOrDefault(f => f.Kind != FrameKind.Framework && f.Kind != FrameKind.External);
        }

        if (appFrame == null) return null;

        var confidence = CalculateConfidence(appFrame);

        // Collect other application frames as candidates
        var candidates = frames
            .Where(f => f.Kind == FrameKind.Application && f.Index != appFrame.Index)
            .Select(f => new SourceLocation
            {
                FilePath = f.FilePath,
                LineNumber = f.LineNumber,
                MethodName = f.MethodName
            }).ToList();

        return new FailureLocalization
        {
            MethodName = appFrame.MethodName,
            PrimaryFilePath = appFrame.FilePath,
            PrimaryLineNumber = appFrame.LineNumber,
            Confidence = confidence,
            CandidateLocations = candidates
        };
    }

    private LocalizationConfidence CalculateConfidence(StackFrame frame)
    {
        if (!string.IsNullOrEmpty(frame.FilePath) && frame.LineNumber.HasValue)
            return LocalizationConfidence.High;

        if (!string.IsNullOrEmpty(frame.FilePath))
            return LocalizationConfidence.Medium;

        if (!string.IsNullOrEmpty(frame.MethodName))
            return LocalizationConfidence.Low;

        return LocalizationConfidence.Unknown;
    }
}