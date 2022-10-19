﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tests_Shared;
using Up2dateClient;
using Up2dateShared;

namespace Up2dateTests.Up2dateClient
{
    [TestClass]
    public class ClientTest
    {
        private WrapperMock wrapperMock;
        private SettingsManagerMock settingsManagerMock;
        private SetupManagerMock setupManagerMock;
        private LoggerMock loggerMock;
        private string certificate;
        private SystemInfo sysInfo = SystemInfo.Retrieve();

        [TestCleanup]
        public void Cleanup()
        {
            wrapperMock.ExitRun();
        }


        //
        //  General client run tests
        //

        [TestMethod]
        public void WhenCreated_ClientStatusIsStopped()
        {
            // arrange
            // act
            Client client = CreateClient();

            // assert
            Assert.IsNotNull(client.State);
            Assert.AreEqual(ClientStatus.Stopped, client.State.Status);
            Assert.IsTrue(string.IsNullOrEmpty(client.State.LastError));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        public void GivenNoCertificate_WhenRun_ThenWrapperRunIsNotExecuted_AndStatusIsNoCertificate(string certificate)
        {
            // arrange
            Client client = CreateClient();
            this.certificate = certificate;

            // act
            client.Run();

            // assert
            wrapperMock.VerifyNoOtherCalls();
            Assert.AreEqual(ClientStatus.NoCertificate, client.State.Status);
        }

        [TestMethod]
        public void WhenRun_WrapperClientRunIsCalledWithCorrectArguments()
        {
            // arrange
            Client client = CreateClient();

            // act
            StartClient(client);

            // assert
            wrapperMock.Verify(m => m.RunClient(certificate, settingsManagerMock.Object.ProvisioningUrl, settingsManagerMock.Object.XApigToken,
                wrapperMock.Dispatcher, It.IsNotNull<AuthErrorActionFunc>()));
        }

        [TestMethod]
        public void GivenClientRunning_WhenWrapperClientRunExited_ThenStatusIsReconnectingWithoutMessage_AndDispatcherIsDeleted()
        {
            // arrange
            Client client = CreateClient();
            StartClient(client);

            // act
            wrapperMock.ExitRun();
            Thread.Sleep(100);

            // assert
            Assert.AreEqual(ClientStatus.Reconnecting, client.State.Status);
            Assert.IsTrue(string.IsNullOrEmpty(client.State.LastError));
            wrapperMock.Verify(m => m.DeleteDispatcher(wrapperMock.Dispatcher));
        }

        [TestMethod]
        public void GivenClientRunning_WhenWrapperClientThrewException_ThenStatusIsReconnectingWithMessage_AndDispatcherIsDeleted()
        {
            // arrange
            Client client = CreateClient();
            string message = "exception message";
            wrapperMock.Setup(m => m.RunClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IntPtr>(), It.IsAny<AuthErrorActionFunc>()))
                .Throws(new Exception(message));

            // act
            client.Run();

            // assert
            Assert.AreEqual(ClientStatus.Reconnecting, client.State.Status);
            Assert.AreEqual(message, client.State.LastError);
            wrapperMock.Verify(m => m.DeleteDispatcher(wrapperMock.Dispatcher));
        }

        [TestMethod]
        public void WhenRun_ThenStatusIsRunning()
        {
            // arrange
            Client client = CreateClient();

            // act
            StartClient(client);

            // assert
            Assert.AreEqual(ClientStatus.Running, client.State.Status);
            Assert.IsTrue(string.IsNullOrEmpty(client.State.LastError));
        }


        //
        //  Authorization error callback tests
        //

        [TestMethod]
        public void WhenAuthErrorActionBringsErrorMessage_ThenStatusIsAuthorizationError()
        {
            // arrange
            Client client = CreateClient();
            string message = "Authorization Error message";
            StartClient(client);

            // act
            wrapperMock.AuthErrorCallback(message);

            // assert
            Assert.AreEqual(ClientStatus.AuthorizationError, client.State.Status);
            Assert.AreEqual(message, client.State.LastError);
        }


        //
        //  Config request callback tests
        //

        [TestMethod]
        public void WhenConfigRequested_ThenAddConfigAttributeIsCalledSupplyingSysInfoValues()
        {
            // arrange
            Client client = CreateClient();
            IntPtr responseBuilder = new IntPtr(-1);
            var callSequence = new List<(IntPtr ptr, string key, string value)>();
            wrapperMock.Setup(m => m.AddConfigAttribute(It.IsAny<IntPtr>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<IntPtr, string, string>((ptr, key, value) => { callSequence.Add((ptr, key, value)); });
            StartClient(client);

            // act
            wrapperMock.ConfigRequestFunc(responseBuilder);

            // assert
            Assert.AreEqual(7, callSequence.Count);
            CollectionAssert.Contains(callSequence, (responseBuilder, "client", "RITMS UP2DATE for Windows"));
            CollectionAssert.Contains(callSequence, (responseBuilder, "computer", sysInfo.MachineName));
            CollectionAssert.Contains(callSequence, (responseBuilder, "machine GUID", sysInfo.MachineGuid));
            CollectionAssert.Contains(callSequence, (responseBuilder, "platform", sysInfo.PlatformID.ToString()));
            CollectionAssert.Contains(callSequence, (responseBuilder, "OS type", sysInfo.Is64Bit ? "64-bit" : "32-bit"));
            CollectionAssert.Contains(callSequence, (responseBuilder, "version", sysInfo.VersionString));
            CollectionAssert.Contains(callSequence, (responseBuilder, "service pack", sysInfo.ServicePack));
        }


        //
        //  Cancel request callback tests
        //

        [TestMethod]
        public void WhenCancelRequested_ThenRequestReturnsTrue()
        {
            // arrange
            const int stopID = 1;
            Client client = CreateClient();
            StartClient(client);

            // act
            var result = wrapperMock.CancelActionFunc(stopID);

            // assert
            Assert.IsTrue(result);
        }


        //
        //  Deployment request callback tests
        //

        [TestMethod]
        public void GivenDeniedPackageType_WhenDeploymentRequested_ThenFailure()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            StartClient(client);

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo { artifactFileName = "name.ext" }, out ClientResult result);

            // assert
            Assert.AreEqual(Execution.CLOSED, result.Execution);
            Assert.AreEqual(Finished.FAILURE, result.Finished);
            Assert.IsFalse(string.IsNullOrEmpty(result.Message));
        }

        [TestMethod]
        public void GivenUnsupportedPackageType_WhenDeploymentRequested_ThenFailure()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            StartClient(client);
            setupManagerMock.IsFileSupported = false;

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo { artifactFileName = "name.msi" }, out ClientResult result);

            // assert
            Assert.AreEqual(Execution.CLOSED, result.Execution);
            Assert.AreEqual(Finished.FAILURE, result.Finished);
            Assert.IsFalse(string.IsNullOrEmpty(result.Message));
        }

        [TestMethod]
        public void GivenCancelWasRequested_WhenDeploymentRequested_ThenResultIsCanceled()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const int reqID = 1;
            StartClient(client);
            wrapperMock.CancelActionFunc(reqID);

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo { artifactFileName = "name.msi", id = reqID }, out ClientResult result);

            // assert
            Assert.AreEqual(Execution.CANCELED, result.Execution);
            Assert.AreEqual(Finished.NONE, result.Finished);
            Assert.IsFalse(string.IsNullOrEmpty(result.Message));
        }

        [TestMethod]
        public void WhenDeploymentRequested_ThenDownloadMethodIsCorrectlyCalled()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo { artifactFileName = fileName }, out ClientResult result);

            // assert
            setupManagerMock.Verify(m => m.DownloadPackage(fileName, It.IsAny<string>(), It.IsNotNull<Action<string>>()), Times.Once);
        }

        [TestMethod]
        public void GivenFileIsAlreadyInstalled_WhenDeploymentRequested_ThenResultIsSuccess()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);
            setupManagerMock.IsPackageInstalled = true;

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo { artifactFileName = fileName }, out ClientResult result);

            // assert
            setupManagerMock.VerifyExecution(fileName, download: true, install: false);
            Assert.AreEqual(Execution.CLOSED, result.Execution);
            Assert.AreEqual(Finished.SUCCESS, result.Finished);
        }

        [TestMethod]
        public void GivenSkipUpdateInMaintenanceWindow_WhenDeploymentRequested_ThenDownloadIsExecuted_AndInstallationIsNotExecuted_AndResultIsSuccess()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo { 
                artifactFileName = fileName, 
                isInMaintenanceWindow = true,
                updateType = "skip" 
            }, out ClientResult result);

            // assert
            setupManagerMock.VerifyExecution(fileName, download: true, install: false);
            Assert.AreEqual(Execution.CLOSED, result.Execution);
            Assert.AreEqual(Finished.SUCCESS, result.Finished);
        }

        [TestMethod]
        public void GivenSkipUpdateOutOfMaintenanceWindow_WhenDeploymentRequested_ThenDownloadIsExecuted_AndInstallationIsNotExecuted_AndResultIsDownloaded()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo
            {
                artifactFileName = fileName,
                isInMaintenanceWindow = false,
                updateType = "skip"
            }, out ClientResult result);

            // assert
            setupManagerMock.VerifyExecution(fileName, download: true, install: false);
            Assert.AreEqual(Execution.DOWNLOADED, result.Execution);
            Assert.AreEqual(Finished.NONE, result.Finished);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void GivenAttemptUpdateAndPackageIsDownloaded_WhenDeploymentRequested_ThenDownloadIsExecuted_AndInstallationIsNotExecuted_AndPackageIsSuggested_AndResultIsDownloaded(
            bool inMaintenanceWindow)
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);
            setupManagerMock.PackageStatus = PackageStatus.Downloaded;

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo
            {
                artifactFileName = fileName,
                isInMaintenanceWindow = inMaintenanceWindow,
                updateType = "attempt"
            }, out ClientResult result);

            // assert
            setupManagerMock.VerifyExecution(fileName, download: true, install: false);
            setupManagerMock.Verify(m => m.MarkPackageAsSuggested(fileName), Times.AtLeastOnce);
            Assert.AreEqual(Execution.DOWNLOADED, result.Execution);
            Assert.AreEqual(Finished.NONE, result.Finished);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void GivenAttemptUpdateAndPackageStatusIsFailed_WhenDeploymentRequested_ThenDownloadIsExecuted_AndInstallationIsNotExecuted_AndResultIsFailed(
            bool inMaintenanceWindow)
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);
            setupManagerMock.PackageStatus = PackageStatus.Failed;

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo
            {
                artifactFileName = fileName,
                isInMaintenanceWindow = inMaintenanceWindow,
                updateType = "attempt"
            }, out ClientResult result);

            // assert
            setupManagerMock.VerifyExecution(fileName, download: true, install: false);
            Assert.AreEqual(Execution.CLOSED, result.Execution);
            Assert.AreEqual(Finished.FAILURE, result.Finished);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void GivenForcedUpdateAndPackageIsInstalled_WhenDeploymentRequested_ThenDownloadIsExecuted_AndInstallationIsExecuted_AndResultIsSuccess(
            bool inMaintenanceWindow)
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);
            setupManagerMock.PackageStatus = PackageStatus.Installed;

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo
            {
                artifactFileName = fileName,
                isInMaintenanceWindow = inMaintenanceWindow,
                updateType = "forced"
            }, out ClientResult result);

            // assert
            setupManagerMock.VerifyExecution(fileName, download: true, install: true);
            Assert.AreEqual(Execution.CLOSED, result.Execution);
            Assert.AreEqual(Finished.SUCCESS, result.Finished);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void GivenForcedUpdateAndPackageStatusIsFailed_WhenDeploymentRequested_ThenDownloadIsExecuted_AndInstallationIsExecuted_AndResultIsFailed(
            bool inMaintenanceWindow)
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);
            setupManagerMock.PackageStatus = PackageStatus.Failed;

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo
            {
                artifactFileName = fileName,
                isInMaintenanceWindow = inMaintenanceWindow,
                updateType = "forced"
            }, out ClientResult result);

            // assert
            setupManagerMock.VerifyExecution(fileName, download: true, install: true);
            Assert.AreEqual(Execution.CLOSED, result.Execution);
            Assert.AreEqual(Finished.FAILURE, result.Finished);
        }

        [TestMethod]
        public void GivenUnknownUpdateMode_WhenDeploymentRequested_ResultIsRejected()
        {
            // arrange
            Client client = CreateClient();
            IntPtr artifact = new IntPtr(-1);
            const string fileName = "name.msi";
            StartClient(client);

            // act
            wrapperMock.DeploymentActionFunc(artifact, new DeploymentInfo
            {
                artifactFileName = fileName,
                updateType = "something_unknown"
            }, out ClientResult result);

            // assert
            Assert.AreEqual(Execution.REJECTED, result.Execution);
            Assert.AreEqual(Finished.FAILURE, result.Finished);
        }

        private void StartClient(Client client)
        {
            Task.Run(client.Run);
            Thread.Sleep(100);
        }

        private Client CreateClient()
        {
            wrapperMock = new WrapperMock();
            settingsManagerMock = new SettingsManagerMock();
            setupManagerMock = new SetupManagerMock();
            loggerMock = new LoggerMock();
            certificate = "certificate body";
            Client client = new Client(wrapperMock.Object, settingsManagerMock.Object, () => certificate, setupManagerMock.Object, () => sysInfo, loggerMock.Object);

            return client;
        }
    }
}
