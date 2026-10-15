#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

// Docs gate (shared template). Checks every relative link and heading anchor in the repository's Markdown,
// including HTML href/src attributes: the target file or directory must exist, and a #fragment must match a
// heading (GitHub's slug rules) or an explicit HTML id in the target document. External links (http:, https:,
// mailto:) are not fetched.
//
// Usage: dotnet run scripts/check-docs.cs -- [repo-root]   (default: current directory)
// Exit codes: 0 pass, 1 broken links, 2 usage error.

using System.Text;
using System.Text.RegularExpressions;

if (args.Length > 1)
{
    Console.Error.WriteLine("usage: check-docs.cs [repo-root]");
    return 2;
}

string root = Path.GetFullPath(args.Length == 1 ? args[0] : ".");
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"not a directory: {root}");
    return 2;
}

HashSet<string> skippedDirectories = new(StringComparer.OrdinalIgnoreCase)
{
    ".git", ".vs", ".cache", "bin", "obj", "node_modules", "TestResults", "coverage", "publish",
};

Regex inlineLink = new(@"\]\(\s*(<[^>]*>|[^)\s]+)(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)", RegexOptions.Compiled);
Regex referenceDefinition = new(@"^ {0,3}\[[^\]]+\]:\s*(<[^>]*>|\S+)", RegexOptions.Compiled);
Regex atxHeading = new(@"^ {0,3}#{1,6}\s+(.*?)\s*#*\s*$", RegexOptions.Compiled);
Regex setextUnderline = new(@"^ {0,3}(=+|-+)\s*$", RegexOptions.Compiled);
Regex htmlLink = new(@"<(?:a|img|source)\s[^>]*?\b(?:href|src)\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
Regex htmlAnchor = new(@"<a\s[^>]*\b(?:id|name)\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
Regex inlineCode = new(@"(`+).+?\1", RegexOptions.Compiled);
Regex htmlComment = new(@"<!--.*?-->", RegexOptions.Compiled);
Regex scheme = new(@"^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.Compiled);

Dictionary<string, Document> documents = new(StringComparer.Ordinal);
List<string> files = [.. EnumerateMarkdown(root).Order(StringComparer.Ordinal)];
bool githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
int links = 0;
int broken = 0;

foreach (string file in files)
{
    Document document = Parse(file);
    foreach ((int line, string target) in document.Links)
    {
        links++;
        string? problem = Check(file, target);
        if (problem is not null)
        {
            broken++;
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            Console.WriteLine($"{relative}:{line}: {problem}");
            if (githubActions)
            {
                Console.WriteLine($"::error file={relative},line={line},title=Broken link::{problem}");
            }
        }
    }
}

Console.WriteLine($"Checked {links} links in {files.Count} Markdown files: {(broken == 0 ? "all good" : $"{broken} broken")}.");
return broken == 0 ? 0 : 1;

string? Check(string sourceFile, string rawTarget)
{
    string target = rawTarget.Trim('<', '>');
    if (scheme.IsMatch(target) || target.StartsWith("//", StringComparison.Ordinal))
    {
        return null; // external
    }

    int hash = target.IndexOf('#', StringComparison.Ordinal);
    string pathPart = hash >= 0 ? target[..hash] : target;
    string fragment = hash >= 0 ? Uri.UnescapeDataString(target[(hash + 1)..]) : "";
    int query = pathPart.IndexOf('?', StringComparison.Ordinal);
    if (query >= 0)
    {
        pathPart = pathPart[..query];
    }

    pathPart = Uri.UnescapeDataString(pathPart);
    string resolved = pathPart.Length == 0
        ? sourceFile
        : Path.GetFullPath(pathPart.StartsWith('/')
            ? Path.Combine(root, pathPart.TrimStart('/'))
            : Path.Combine(Path.GetDirectoryName(sourceFile)!, pathPart));

    if (!File.Exists(resolved) && !Directory.Exists(resolved))
    {
        return $"'{rawTarget}': target not found";
    }

    if (fragment.Length == 0 || !resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
    {
        return null;
    }

    Document targetDocument = Parse(resolved);
    return targetDocument.Anchors.Contains(fragment) || targetDocument.Anchors.Contains(fragment.ToLowerInvariant())
        ? null
        : $"'{rawTarget}': no heading or anchor '#{fragment}' in {Path.GetFileName(resolved)}";
}

Document Parse(string file)
{
    if (documents.TryGetValue(file, out Document? cached))
    {
        return cached;
    }

    Document document = new();
    Dictionary<string, int> slugCounts = new(StringComparer.Ordinal);
    string[] lines = File.ReadAllLines(file);
    string? fence = null;
    bool inComment = false;
    string previous = "";

    for (int i = 0; i < lines.Length; i++)
    {
        string line = lines[i];
        string trimmed = line.TrimStart();

        if (fence is not null)
        {
            if (trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.Trim().All(c => c == fence[0]))
            {
                fence = null;
            }

            previous = "";
            continue;
        }

        // Any indentation: fences nested in list items sit 4+ spaces in. (Indented code blocks that contain a
        // fence-like line are the one case this misreads; the docs don't use indented code blocks.)
        if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
        {
            fence = new string(trimmed[0], trimmed.TakeWhile(c => c == trimmed[0]).Count());
            previous = "";
            continue;
        }

        if (inComment)
        {
            int end = line.IndexOf("-->", StringComparison.Ordinal);
            if (end < 0)
            {
                continue;
            }

            line = line[(end + 3)..];
            inComment = false;
        }

        line = htmlComment.Replace(line, "");
        int open = line.IndexOf("<!--", StringComparison.Ordinal);
        if (open >= 0)
        {
            line = line[..open];
            inComment = true;
        }

        Match heading = atxHeading.Match(line);
        if (heading.Success)
        {
            AddAnchor(Slug(heading.Groups[1].Value));
        }
        else if (setextUnderline.IsMatch(line) && previous.Trim().Length > 0 && !IsBlockStart(previous))
        {
            AddAnchor(Slug(previous.Trim()));
        }

        foreach (Match anchor in htmlAnchor.Matches(line))
        {
            document.Anchors.Add(anchor.Groups[1].Value);
        }

        string code = inlineCode.Replace(line, "");
        foreach (Match link in inlineLink.Matches(code))
        {
            document.Links.Add((i + 1, link.Groups[1].Value));
        }

        foreach (Match link in htmlLink.Matches(code))
        {
            document.Links.Add((i + 1, link.Groups[1].Value));
        }

        Match definition = referenceDefinition.Match(code);
        if (definition.Success)
        {
            document.Links.Add((i + 1, definition.Groups[1].Value));
        }

        previous = line;
    }

    documents[file] = document;
    return document;

    void AddAnchor(string slug)
    {
        int count = slugCounts.GetValueOrDefault(slug);
        slugCounts[slug] = count + 1;
        document.Anchors.Add(count == 0 ? slug : $"{slug}-{count}");
    }
}

// GitHub's heading ids come from the rendered text: lower-case, punctuation and symbols dropped, spaces become
// hyphens. So links and HTML reduce to their text, and underscore emphasis (__x__, _x_) loses its markers,
// while code spans keep their underscores. (Asterisks are dropped as punctuation anyway.)
static string Slug(string heading)
{
    string text = Regex.Replace(heading, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
    text = Regex.Replace(text, "<[^>]+>", "");
    text = Regex.Replace(
        text,
        @"(`+)(.+?)\1|(?<![\p{L}\p{N}_])(__?)(?=\S)(.+?)(?<=\S)\3(?![\p{L}\p{N}_])",
        m => m.Groups[1].Success ? m.Groups[2].Value : m.Groups[4].Value);
    StringBuilder slug = new(text.Length);
    foreach (char c in text.ToLowerInvariant())
    {
        if (char.IsLetterOrDigit(c) || c is '-' or '_')
        {
            slug.Append(c);
        }
        else if (c == ' ')
        {
            slug.Append('-');
        }
    }

    return slug.ToString();
}

static bool IsBlockStart(string line)
{
    string t = line.TrimStart();
    return t.Length == 0 || t[0] is '#' or '|' or '-' or '*' or '+' or '>' || char.IsDigit(t[0]) && t.Contains(". ", StringComparison.Ordinal);
}

IEnumerable<string> EnumerateMarkdown(string directory)
{
    foreach (string file in Directory.EnumerateFiles(directory, "*.md"))
    {
        yield return file;
    }

    foreach (string sub in Directory.EnumerateDirectories(directory))
    {
        if (skippedDirectories.Contains(Path.GetFileName(sub)))
        {
            continue;
        }

        foreach (string file in EnumerateMarkdown(sub))
        {
            yield return file;
        }
    }
}

sealed class Document
{
    public HashSet<string> Anchors { get; } = new(StringComparer.Ordinal);

    public List<(int Line, string Target)> Links { get; } = [];
}