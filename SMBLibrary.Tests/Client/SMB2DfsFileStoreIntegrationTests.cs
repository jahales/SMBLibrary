/* LOCAL INTEGRATION HARNESS — DO NOT COMMIT.
 *
 * Manual end-to-end verification of SMB2DfsFileStore against a real DFS namespace.
 * These tests are gated on environment variables and report Inconclusive when unset,
 * so they are inert unless you deliberately configure a lab.
 *
 * Required (all tests):
 *   SMB_DFS_SERVER        DFS namespace server (name or IP), e.g. LAB-DC1.LAB.LOCAL
 *   SMB_DFS_ROOT_SHARE    DFS root share / namespace name, e.g. Files
 *   SMB_DFS_USER          username
 *   SMB_DFS_PASSWORD      password
 * Optional:
 *   SMB_DFS_DOMAIN        domain (default empty)
 *   SMB_DFS_LINK_PATH     file path under the root that crosses a DFS link, e.g. Sales\readme.txt
 *   SMB_DFS_LINK_DIR      directory path under a DFS link, e.g. Sales
 *   SMB_DFS_TARGET_SERVER \  the server the link actually resolves to (for differential test)
 *   SMB_DFS_TARGET_SHARE   > e.g. LAB-FS2 / Sales / readme.txt
 *   SMB_DFS_TARGET_PATH   /
 *
 * PowerShell example:
 *   $env:SMB_DFS_SERVER="LAB-DC1.LAB.LOCAL"; $env:SMB_DFS_ROOT_SHARE="Files"
 *   $env:SMB_DFS_USER="labadmin"; $env:SMB_DFS_PASSWORD="..."; $env:SMB_DFS_DOMAIN="LAB"
 *   $env:SMB_DFS_LINK_PATH="Sales\readme.txt"
 *   $env:SMB_DFS_TARGET_SERVER="LAB-FS2"; $env:SMB_DFS_TARGET_SHARE="Sales"; $env:SMB_DFS_TARGET_PATH="readme.txt"
 *   dotnet test SMBLibrary.Tests -f net6.0 --filter "SMB2DfsFileStoreIntegrationTests" -l "console;verbosity=detailed"
 */
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SMBLibrary.Client;
using SMBLibrary.Client.DFS;

namespace SMBLibrary.Tests.Client
{
    [TestClass]
    public class SMB2DfsFileStoreIntegrationTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void TreeConnectDfsRoot_ReturnsDfsFileStore()
        {
            LabConfig config = LabConfig.Require();

            SMB2Client client = ConnectAndLogin(config);
            try
            {
                NTStatus status;
                ISMBFileStore fileStore = client.TreeConnect(config.RootShare, out status);
                Log("TreeConnect('{0}') -> {1}, store type = {2}", config.RootShare, status, fileStore != null ? fileStore.GetType().Name : "null");
                Assert.AreEqual(NTStatus.STATUS_SUCCESS, status);

                // Confirms the SMB2_SHAREFLAG_DFS_ROOT detection + wrapping in TreeConnect actually fired.
                Assert.IsInstanceOfType(fileStore, typeof(SMB2DfsFileStore),
                    "The share is not being wrapped as a DFS root (SMB2_SHAREFLAG_DFS_ROOT not set by the server or not detected).");
                fileStore.Disconnect();
            }
            finally
            {
                Logoff(client);
            }
        }

        [TestMethod]
        public void ReadThroughDfsLink_Succeeds()
        {
            LabConfig config = LabConfig.Require();
            string linkPath = RequireEnv("SMB_DFS_LINK_PATH");

            SMB2Client client = ConnectAndLogin(config);
            try
            {
                NTStatus status;
                ISMBFileStore fileStore = client.TreeConnect(config.RootShare, out status);
                Assert.AreEqual(NTStatus.STATUS_SUCCESS, status);
                Assert.IsInstanceOfType(fileStore, typeof(SMB2DfsFileStore));

                // A non-DFS-aware client would get STATUS_PATH_NOT_COVERED here; success proves the referral was followed.
                byte[] data = ReadAllBytes(fileStore, linkPath);
                Assert.IsTrue(data.Length > 0, "Read 0 bytes through the DFS link");
                Log("First bytes: {0}", BitConverter.ToString(data, 0, Math.Min(32, data.Length)));
                fileStore.Disconnect();
            }
            finally
            {
                Logoff(client);
            }
        }

        [TestMethod]
        public void ReadThroughDfsLink_MatchesDirectTarget()
        {
            LabConfig config = LabConfig.Require();
            string linkPath = RequireEnv("SMB_DFS_LINK_PATH");
            string targetServer = RequireEnv("SMB_DFS_TARGET_SERVER");
            string targetShare = RequireEnv("SMB_DFS_TARGET_SHARE");
            string targetPath = RequireEnv("SMB_DFS_TARGET_PATH");

            // Read via the DFS namespace path (resolution happens inside SMB2DfsFileStore)
            byte[] viaDfs;
            SMB2Client nsClient = ConnectAndLogin(config);
            try
            {
                NTStatus status;
                ISMBFileStore dfsStore = nsClient.TreeConnect(config.RootShare, out status);
                Assert.AreEqual(NTStatus.STATUS_SUCCESS, status);
                Assert.IsInstanceOfType(dfsStore, typeof(SMB2DfsFileStore));
                viaDfs = ReadAllBytes(dfsStore, linkPath);
                dfsStore.Disconnect();
            }
            finally
            {
                Logoff(nsClient);
            }

            // Read the same file directly from the server the link is supposed to resolve to
            byte[] viaDirect;
            SMB2Client targetClient = ConnectAndLogin(config.WithServer(targetServer));
            try
            {
                NTStatus status;
                ISMBFileStore directStore = targetClient.TreeConnect(targetShare, out status);
                Assert.AreEqual(NTStatus.STATUS_SUCCESS, status);
                viaDirect = ReadAllBytes(directStore, targetPath);
                directStore.Disconnect();
            }
            finally
            {
                Logoff(targetClient);
            }

            Log("DFS bytes = {0}, direct bytes = {1}", viaDfs.Length, viaDirect.Length);
            CollectionAssert.AreEqual(viaDirect, viaDfs,
                "Content read through the DFS link differs from the direct target — the referral resolved to the wrong place.");
        }

        [TestMethod]
        public void EnumerateDfsLinkDirectory_Succeeds()
        {
            LabConfig config = LabConfig.Require();
            string linkDir = RequireEnv("SMB_DFS_LINK_DIR");

            SMB2Client client = ConnectAndLogin(config);
            try
            {
                NTStatus status;
                ISMBFileStore fileStore = client.TreeConnect(config.RootShare, out status);
                Assert.AreEqual(NTStatus.STATUS_SUCCESS, status);
                Assert.IsInstanceOfType(fileStore, typeof(SMB2DfsFileStore));

                object handle;
                FileStatus fileStatus;
                status = fileStore.CreateFile(out handle, out fileStatus, linkDir, AccessMask.GENERIC_READ, FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                Log("CreateFile(dir '{0}') -> {1}", linkDir, status);
                Assert.AreEqual(NTStatus.STATUS_SUCCESS, status);

                List<QueryDirectoryFileInformation> entries;
                status = fileStore.QueryDirectory(out entries, handle, "*", FileInformationClass.FileDirectoryInformation);
                fileStore.CloseFile(handle);
                Log("QueryDirectory('{0}') -> {1}, {2} entries", linkDir, status, entries != null ? entries.Count : 0);
                Assert.AreEqual(NTStatus.STATUS_SUCCESS, status);
                Assert.IsTrue(entries.Count > 0, "No directory entries returned through the DFS link");
                fileStore.Disconnect();
            }
            finally
            {
                Logoff(client);
            }
        }

        private byte[] ReadAllBytes(ISMBFileStore fileStore, string path)
        {
            object handle;
            FileStatus fileStatus;
            NTStatus status = fileStore.CreateFile(out handle, out fileStatus, path, AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE, FileAttributes.Normal, ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            Log("CreateFile('{0}') -> {1}", path, status);
            Assert.AreEqual(NTStatus.STATUS_SUCCESS, status, "CreateFile failed for " + path);

            // Use the store's MaxReadSize (the min across followed connections), not the root client's.
            int maxReadSize = (int)fileStore.MaxReadSize;
            MemoryStream stream = new MemoryStream();
            long bytesRead = 0;
            while (true)
            {
                byte[] data;
                status = fileStore.ReadFile(out data, handle, bytesRead, maxReadSize);
                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                {
                    Assert.Fail("ReadFile failed for " + path + ": " + status);
                }
                if (status == NTStatus.STATUS_END_OF_FILE || data == null || data.Length == 0)
                {
                    break;
                }
                bytesRead += data.Length;
                stream.Write(data, 0, data.Length);
            }
            fileStore.CloseFile(handle);
            Log("Read {0} bytes from '{1}'", stream.Length, path);
            return stream.ToArray();
        }

        private SMB2Client ConnectAndLogin(LabConfig config)
        {
            SMB2Client client = new SMB2Client();
            bool isConnected = client.Connect(config.Server, SMBTransportType.DirectTCPTransport);
            Assert.IsTrue(isConnected, "Failed to connect to " + config.Server);
            NTStatus status = client.Login(config.Domain, config.User, config.Password);
            Log("Login to {0} -> {1}", config.Server, status);
            Assert.AreEqual(NTStatus.STATUS_SUCCESS, status, "Login failed against " + config.Server);
            return client;
        }

        private static void Logoff(SMB2Client client)
        {
            try { client.Logoff(); } catch { }
            client.Disconnect();
        }

        private void Log(string format, params object[] args)
        {
            TestContext.WriteLine(format, args);
        }

        private static string RequireEnv(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (String.IsNullOrEmpty(value))
            {
                Assert.Inconclusive("Set {0} to run this integration test.", name);
            }
            return value;
        }

        private class LabConfig
        {
            public string Server;
            public string RootShare;
            public string Domain;
            public string User;
            public string Password;

            public static LabConfig Require()
            {
                return new LabConfig
                {
                    Server = RequireEnv("SMB_DFS_SERVER"),
                    RootShare = RequireEnv("SMB_DFS_ROOT_SHARE"),
                    User = RequireEnv("SMB_DFS_USER"),
                    Password = RequireEnv("SMB_DFS_PASSWORD"),
                    Domain = Environment.GetEnvironmentVariable("SMB_DFS_DOMAIN") ?? String.Empty
                };
            }

            public LabConfig WithServer(string server)
            {
                return new LabConfig { Server = server, RootShare = RootShare, Domain = Domain, User = User, Password = Password };
            }
        }
    }
}
