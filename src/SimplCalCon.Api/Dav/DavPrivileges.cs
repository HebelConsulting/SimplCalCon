using System.Xml.Linq;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Dav;

/// <summary>
/// Maps a caller's effective <see cref="AclRight"/>s to the DAV
/// <c>current-user-privilege-set</c> reported on a collection (ADR 0007, 0023).
/// </summary>
internal static class DavPrivileges
{
    public static IEnumerable<XElement> From(AclRight rights)
    {
        var privileges = new List<XName>();

        if ((rights & AclRight.Read) == AclRight.Read)
        {
            privileges.Add(DavNames.Read);
        }

        if ((rights & AclRight.WriteContent) == AclRight.WriteContent)
        {
            privileges.Add(DavNames.Write);
            privileges.Add(DavNames.WriteContent);
            privileges.Add(DavNames.Bind);
            privileges.Add(DavNames.Unbind);
        }

        if ((rights & AclRight.Admin) == AclRight.Admin)
        {
            privileges.Add(DavNames.WriteProperties);
        }

        return privileges.Distinct().Select(p => new XElement(DavNames.Privilege, new XElement(p)));
    }
}
