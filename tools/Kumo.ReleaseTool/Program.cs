using Kumo.Core.Updates;

// Release workflow helpers that must follow the updater's own rules instead of a second implementation in script.
//   previous-tag <tag>        reads tags from stdin, prints the previous release tag of the same platform (or nothing)
//   check-repository <owner/name>   exits with an error unless the value is a valid update repository
if (args is ["previous-tag", var current])
{
    if (!ReleaseTag.TryParse(current, out var platform, out var version))
        return Fail($"'{current}' is not a release tag.");
    var tags = new List<string>();
    while (Console.In.ReadLine() is { } line)
        tags.Add(line);
    Console.WriteLine(ReleaseTag.FindPrevious(platform, version, tags) ?? string.Empty);
    return 0;
}
if (args is ["check-repository", var repository])
    return RepositoryIdentity.TryParse(repository, out _) ? 0 : Fail($"'{repository}' is not an owner/name GitHub repository.");

return Fail("Usage: Kumo.ReleaseTool previous-tag <tag> | check-repository <owner/name>");

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
