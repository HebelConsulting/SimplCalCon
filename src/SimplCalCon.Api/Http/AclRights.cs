using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Http;

/// <summary>Converts ACL rights between the flags enum and the API's kebab-case string array.</summary>
internal static class AclRights
{
    private static readonly (AclRight Right, string Name)[] Map =
    [
        (AclRight.Read, "read"),
        (AclRight.WriteContent, "write-content"),
        (AclRight.Create, "create"),
        (AclRight.Delete, "delete"),
        (AclRight.Share, "share"),
        (AclRight.Admin, "admin"),
    ];

    public static AclRight Parse(IEnumerable<string> names)
    {
        var result = AclRight.None;
        foreach (var name in names)
        {
            var normalized = name.Trim().ToLowerInvariant();
            var match = Map.FirstOrDefault(m => m.Name == normalized);
            result |= match.Right;
        }

        return result;
    }

    public static IReadOnlyList<string> Format(AclRight rights) =>
        Map.Where(m => (rights & m.Right) == m.Right).Select(m => m.Name).ToList();
}
