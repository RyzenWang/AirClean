using Xunit;

namespace AirClean.Core.Tests;

/// <summary>
/// Static guard for destructive calls. Every <c>File.Delete</c>, <c>Directory.Delete</c>,
/// <c>File.Move</c> or <c>Directory.Move</c> under <c>src/</c> must carry
/// <c>// SAFE: &lt;reason&gt;</c> on the same line, so a reviewer sees what the path is
/// and why removing or moving it cannot touch user data. The reason must be concrete:
/// an empty marker fails exactly like a missing one.
/// </summary>
public sealed class SourceInvariantTests
{
    private const string SafeMarker = "// SAFE:";

    private static readonly string[] DestructiveCalls =
    [
        "File.Delete(",
        "Directory.Delete(",
        "File.Move(",
        "Directory.Move("
    ];

    [Fact]
    public void Every_delete_or_move_call_in_src_carries_a_safe_reason()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        Assert.True(Directory.Exists(sourceRoot), $"Source root not found: {sourceRoot}");

        var violations = FindViolations(sourceRoot);

        Assert.True(
            violations.Count == 0,
            "Every File.Delete/Directory.Delete/File.Move/Directory.Move call under src/ must explain itself on the " +
            "same line with '// SAFE: <reason>'." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(violation => "  " + violation)));
    }

    [Fact]
    public void The_gate_catches_all_four_destructive_call_shapes()
    {
        using var fixture = new SourceFixture();
        fixture.Write(
            "Calls.cs",
            """
            File.Delete(file);
            Directory.Delete(directory);
            File.Move(source, destination);
            Directory.Move(source, destination);
            """);

        var violations = FindViolations(fixture.Root);

        Assert.Equal(4, violations.Count);
        Assert.Equal(
            new[] { "Calls.cs:1", "Calls.cs:2", "Calls.cs:3", "Calls.cs:4" },
            violations);
    }

    [Fact]
    public void The_gate_flags_a_missing_safe_reason()
    {
        using var fixture = new SourceFixture();
        fixture.Write("Delete.cs", "File.Delete(path);");

        Assert.Equal(new[] { "Delete.cs:1" }, FindViolations(fixture.Root));
    }

    [Fact]
    public void The_gate_flags_an_empty_safe_reason()
    {
        using var fixture = new SourceFixture();
        fixture.Write("Delete.cs", "File.Delete(path); // SAFE:");

        Assert.Equal(new[] { "Delete.cs:1" }, FindViolations(fixture.Root));
    }

    [Fact]
    public void The_gate_accepts_a_concrete_reason_on_the_same_line()
    {
        using var fixture = new SourceFixture();
        fixture.Write("Delete.cs", "File.Delete(path); // SAFE: temp file written by this journal writer");

        Assert.Empty(FindViolations(fixture.Root));
    }

    [Fact]
    public void The_gate_ignores_generated_bin_and_obj_folders()
    {
        using var fixture = new SourceFixture();
        fixture.Write("bin/Generated.cs", "File.Delete(path);");
        fixture.Write("obj/Debug/Generated.cs", "Directory.Delete(path);");
        fixture.Write("Feature/bin/Nested.cs", "File.Move(a, b);");

        Assert.Empty(FindViolations(fixture.Root));
    }

    private static IReadOnlyList<string> FindViolations(string sourceRoot)
    {
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(sourceRoot))
        {
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];

                if (!DestructiveCalls.Any(call => line.Contains(call, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!HasSafeReason(line))
                {
                    var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                    violations.Add($"{relative}:{index + 1}");
                }
            }
        }

        violations.Sort(StringComparer.Ordinal);
        return violations;
    }

    private static bool HasSafeReason(string line)
    {
        var marker = line.IndexOf(SafeMarker, StringComparison.Ordinal);
        return marker >= 0 &&
               line[(marker + SafeMarker.Length)..].Trim().Length > 0;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            yield break;
        }

        var files = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments.Contains("bin") || segments.Contains("obj"))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AirClean.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate AirClean.sln above the test output directory '{AppContext.BaseDirectory}'.");
    }

    private sealed class SourceFixture : IDisposable
    {
        public SourceFixture()
        {
            Directory.CreateDirectory(Root);
        }

        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "airclean-source-gate", Guid.NewGuid().ToString("N"));

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // A leftover temp folder must not fail the gate's own tests.
            }
        }
    }
}
