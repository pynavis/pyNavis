using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

// One source, compiled into two assemblies: the loader (a machine-wide runtime) and the
// runtime (a machine-wide extension root). The loader cannot reference the runtime
// before it has found it, so each gets its own copy and namespace.
#if PYNAVIS_LOADER
namespace PyNavis.Loader
#else
namespace PyNavis.Runtime.Install
#endif
{
    /// <summary>
    /// Whether a folder under C:\ProgramData may be trusted to hold code: owned by SYSTEM,
    /// Administrators or TrustedInstaller, and writable by nobody else. By default any
    /// user can create folders in C:\ProgramData and owns what they create, and a folder
    /// an administrator makes there still inherits "Users may create files", so without
    /// this check one user could make code run in every other user's Navisworks.
    /// </summary>
    internal static class MachineFolderTrust
    {
        // WriteData/CreateFiles, AppendData/CreateDirectories, DeleteSubdirectoriesAndFiles,
        // Delete, ChangePermissions, TakeOwnership, GENERIC_ALL, GENERIC_WRITE.
        private const int WriteMask = 0x2 | 0x4 | 0x40 | 0x10000 | 0x40000 | 0x80000 | 0x10000000 | 0x40000000;

        private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

        /// <summary>False for a missing or unreadable folder.</summary>
        public static bool IsAdminOnly(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return false;
                return IsAdminOnly(Directory.GetAccessControl(directory,
                    AccessControlSections.Owner | AccessControlSections.Access));
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAdminOnly(DirectorySecurity security)
        {
            if (!(security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner) || !IsAdmin(owner))
                return false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow) continue;
                // CREATOR OWNER only reaches what its creator made, and only an admin can create here.
                if (rule.IdentityReference is SecurityIdentifier sid
                    && (IsAdmin(sid) || sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid))) continue;
                if (((int)rule.FileSystemRights & WriteMask) != 0) return false;
            }
            return true;
        }

        private static bool IsAdmin(SecurityIdentifier sid) =>
            sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
            || sid.Value == TrustedInstaller;
    }
}
