using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PyNavis.Runtime.Install;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// A folder under C:\ProgramData is trusted only when no one but SYSTEM,
    /// Administrators or TrustedInstaller owns it or can write to it. By default any
    /// user can create folders there and owns what they create, so code found in one
    /// would otherwise run for every other user on the PC.
    /// </summary>
    public class MachineFolderTrustTests
    {
        private static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier System = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier Users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        private static readonly SecurityIdentifier AuthUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        private static readonly SecurityIdentifier CreatorOwner = new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null);
        private static readonly SecurityIdentifier SomeUser = new SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-1001");

        private const InheritanceFlags Both = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        private static DirectorySecurity Locked(SecurityIdentifier owner = null)
        {
            var security = new DirectorySecurity();
            security.SetOwner(owner ?? Admins);
            security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, Both, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, Both, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, Both, PropagationFlags.None, AccessControlType.Allow));
            return security;
        }

        [Fact]
        public void AnAdminOwnedFolder_UsersMayOnlyRead_IsTrusted()
        {
            Assert.True(MachineFolderTrust.IsAdminOnly(Locked()));
        }

        [Fact]
        public void TheProgramDataDefault_UsersMayCreateFiles_IsNotTrusted()
        {
            var security = Locked();
            security.AddAccessRule(new FileSystemAccessRule(Users,
                FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories
                | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes,
                InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));

            Assert.False(MachineFolderTrust.IsAdminOnly(security));
        }

        [Fact]
        public void AFolderOwnedByAnOrdinaryUser_IsNotTrusted()
        {
            Assert.False(MachineFolderTrust.IsAdminOnly(Locked(SomeUser)));
        }

        [Fact]
        public void AuthenticatedUsersWithModify_IsNotTrusted()
        {
            var security = Locked();
            security.AddAccessRule(new FileSystemAccessRule(AuthUsers, FileSystemRights.Modify, Both, PropagationFlags.None, AccessControlType.Allow));

            Assert.False(MachineFolderTrust.IsAdminOnly(security));
        }

        [Fact]
        public void WriteInheritedOnlyByChildren_IsStillNotTrusted()
        {
            var security = Locked();
            security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.Write, Both, PropagationFlags.InheritOnly, AccessControlType.Allow));

            Assert.False(MachineFolderTrust.IsAdminOnly(security));
        }

        [Fact]
        public void CreatorOwner_GrantsNothingByItself_AndKeepsTheFolderTrusted()
        {
            var security = Locked();
            security.AddAccessRule(new FileSystemAccessRule(CreatorOwner, FileSystemRights.FullControl, Both, PropagationFlags.InheritOnly, AccessControlType.Allow));

            Assert.True(MachineFolderTrust.IsAdminOnly(security));
        }

        [Fact]
        public void AMissingFolder_IsNotTrusted()
        {
            Assert.False(MachineFolderTrust.IsAdminOnly(Path.Combine(Path.GetTempPath(), "pynavis-absent-" + Guid.NewGuid())));
        }

        // ---- a single file, such as %PROGRAMDATA%\pyNavis\telemetry.json ---------------

        private static FileSecurity LockedFile(SecurityIdentifier owner = null)
        {
            var security = new FileSecurity();
            security.SetOwner(owner ?? Admins);
            security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            return security;
        }

        [Fact]
        public void AnAdminOwnedFile_UsersMayOnlyRead_IsTrusted()
        {
            Assert.True(MachineFolderTrust.IsAdminOnly(LockedFile()));
        }

        [Fact]
        public void AFileUsersMayModify_IsNotTrusted()
        {
            var security = LockedFile();
            security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.Modify, AccessControlType.Allow));

            Assert.False(MachineFolderTrust.IsAdminOnly(security));
        }

        [Fact]
        public void AFileOwnedByAnOrdinaryUser_IsNotTrusted()
        {
            // What a login script, which runs as the user, leaves behind.
            Assert.False(MachineFolderTrust.IsAdminOnly(LockedFile(SomeUser)));
        }

        [Fact]
        public void AMissingFile_IsNotTrusted()
        {
            Assert.False(MachineFolderTrust.IsAdminOnlyFile(Path.Combine(Path.GetTempPath(), "pynavis-absent-" + Guid.NewGuid() + ".json")));
        }

        [Fact]
        public void AFileThisUserJustWrote_IsNotTrusted()
        {
            // The test runs as an ordinary user, so the file is theirs: exactly the file
            // a user could plant to send everyone's records somewhere of their choosing.
            var path = Path.Combine(Path.GetTempPath(), "pynavis-trust-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{}");
            try
            {
                Assert.False(MachineFolderTrust.IsAdminOnlyFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
