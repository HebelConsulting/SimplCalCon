using System.Text.RegularExpressions;

namespace SimplCalCon.UnitTests;

/// <summary>
/// Guards the exception-hierarchy convention (CLAUDE.md, ADR 0009): every API error is
/// a dedicated intent-named subclass, so a bare <c>new ApiException(...)</c> must never
/// reappear in the source tree.
/// </summary>
public sealed class NoBareApiExceptionTests
{
    [Fact]
    public void No_bare_ApiException_is_constructed_anywhere_in_src()
    {
        var srcRoot = FindSrcRoot();
        var pattern = new Regex(@"new\s+ApiException\s*\(");

        var offenders = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            // The abstract base's own file names the type in an XML-doc example.
            .Where(path => Path.GetFileName(path) != "ApiException.cs")
            .Where(path => pattern.IsMatch(StripComments(File.ReadAllText(path))))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, $"Bare `new ApiException(...)` found in: {string.Join(", ", offenders)}");
    }

    // Drop // and /* */ comments so an example in a doc comment isn't a false positive.
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(source, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }

    private static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the 'src' directory from the test output path.");
    }
}
