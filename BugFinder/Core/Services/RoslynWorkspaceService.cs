using ISCM.BugFinder.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-13.1: Roslyn Integration Service
/// Stage 1: Initialize Workspace (Adhoc = in-memory, MSBuild = real solution)
/// Stage 2: Load Solution via MSBuildWorkspace
/// Stage 3: Load Projects (workspace snapshot)
/// Stage 4: Access Compilation (compilation snapshot)
/// </summary>
public class RoslynWorkspaceService : IDisposable
{
    private Workspace? _workspace;
    private static bool _msBuildRegistered;

    public Workspace? CurrentWorkspace => _workspace;

    // Stage 1 — Adhoc (fast, no MSBuild, used by unit tests)
    public AdhocWorkspace CreateAdhocWorkspace()
    {
        var ws = new AdhocWorkspace();
        _workspace?.Dispose();
        _workspace = ws;
        return ws;
    }

    // نسخه‌ی اصلی — بدون تغییر امضا؛ همه‌ی تست‌های قبلی همین را صدا می‌زنند
    public Project AddInMemoryCSharpProject(
        AdhocWorkspace ws, string projectName, params (string Name, string Source)[] files)
    {
        return AddInMemoryCSharpProjectWithReferences(ws, projectName, null, files);
    }

    // نسخه‌ی جدید — فقط برای پروژه‌های دارای ProjectReference (BF-13.6 Stage 1)
    public Project AddInMemoryCSharpProjectWithReferences(
        AdhocWorkspace ws,
        string projectName,
        IReadOnlyList<ProjectReference>? projectReferences,
        params (string Name, string Source)[] files)
    {
        var projectId = ProjectId.CreateNewId();
        var version = VersionStamp.Create();

        var documents = files.Select(f => DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name: f.Name,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(f.Source), version)))).ToList();

        var projectInfo = ProjectInfo.Create(
            projectId,
            version,
            name: projectName,
            assemblyName: projectName,
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            documents: documents,
            projectReferences: projectReferences,
            metadataReferences: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        return ws.AddProject(projectInfo);
    }

    // Stage 2 — Load real solution (fail-fast BEFORE touching MSBuild)
    public async Task<RoslynWorkspaceSnapshot> LoadSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
            throw new ArgumentException("Solution path is required.", nameof(solutionPath));

        var fullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Solution file not found.", fullPath);

        EnsureMsBuildRegistered();

        var msWorkspace = MSBuildWorkspace.Create();
        _workspace?.Dispose();
        _workspace = msWorkspace;

        var solution = await msWorkspace.OpenSolutionAsync(fullPath, null, ct);

        return BuildSnapshot(RoslynWorkspaceKind.MSBuild, solution);
    }

    // Stage 3 — Project inventory of the CURRENT workspace
    public RoslynWorkspaceSnapshot GetWorkspaceSnapshot()
    {
        if (_workspace is null)
            throw new InvalidOperationException(
                "No workspace initialized. Call CreateAdhocWorkspace() or LoadSolutionAsync() first.");

        var kind = _workspace.Kind == WorkspaceKind.MSBuild
            ? RoslynWorkspaceKind.MSBuild
            : RoslynWorkspaceKind.Adhoc;

        return BuildSnapshot(kind, _workspace.CurrentSolution);
    }

    // Stage 4 — Compilation access
    public async Task<RoslynCompilationSnapshot> GetCompilationSnapshotAsync(Project project, CancellationToken ct = default)
    {
        var compilation = await project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Compilation unavailable for project '{project.Name}'.");

        return new RoslynCompilationSnapshot
        {
            AssemblyName = compilation.AssemblyName ?? string.Empty,
            Language = compilation.Language,
            SyntaxTreeCount = compilation.SyntaxTrees.Count(),
            MetadataReferenceCount = compilation.References.Count()
        };
    }

    // Must run BEFORE any MSBuild assembly loads (required by MSBuildWorkspace)
    public static void EnsureMsBuildRegistered()
    {
        if (_msBuildRegistered) return;
        try
        {
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException)
        {
            // MSBuild assemblies already loaded — registration impossible/unnecessary
        }
        _msBuildRegistered = true;
    }

    private static RoslynWorkspaceSnapshot BuildSnapshot(RoslynWorkspaceKind kind, Solution solution)
    {
        var snapshot = new RoslynWorkspaceSnapshot
        {
            Kind = kind,
            SolutionPath = solution.FilePath,
            SolutionName = string.IsNullOrEmpty(solution.FilePath)
                ? solution.Id.Id.ToString()
                : Path.GetFileName(solution.FilePath),
            ProjectCount = solution.ProjectIds.Count
        };

        foreach (var project in solution.Projects)
        {
            snapshot.Projects.Add(new RoslynProjectSnapshot
            {
                Name = project.Name,
                FilePath = project.FilePath,
                Language = project.Language,
                DocumentCount = project.DocumentIds.Count,
                DocumentNames = project.Documents.Select(d => d.Name).ToList()
            });
        }

        return snapshot;
    }

    public void Dispose() => _workspace?.Dispose();
}