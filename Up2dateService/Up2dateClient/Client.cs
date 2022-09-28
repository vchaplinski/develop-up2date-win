﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Up2dateShared;

namespace Up2dateClient
{
    public class Client
    {
        const string ClientType = "RITMS UP2DATE for Windows";

        private readonly HashSet<string> supportedTypes = new HashSet<string> { ".msi",".nupkg" }; // must be lowercase
        private readonly EventLog eventLog;
        private readonly ISettingsManager settingsManager;
        private readonly Func<string> getCertificate;
        private readonly ISetupManager setupManager;
        private readonly Func<SystemInfo> getSysInfo;
        private readonly Func<string> getDownloadLocation;
        private ClientState state;

        public Client(ISettingsManager settingsManager, Func<string> getCertificate, ISetupManager setupManager, Func<SystemInfo> getSysInfo, Func<string> getDownloadLocation, EventLog eventLog = null)
        {
            this.settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            this.getCertificate = getCertificate ?? throw new ArgumentNullException(nameof(getCertificate));
            this.setupManager = setupManager ?? throw new ArgumentNullException(nameof(setupManager));
            this.getSysInfo = getSysInfo ?? throw new ArgumentNullException(nameof(getSysInfo));
            this.getDownloadLocation = getDownloadLocation ?? throw new ArgumentNullException(nameof(getDownloadLocation));
            this.eventLog = eventLog;
        }

        public ClientState State
        {
            get => state;
            private set
            {
                if (state.Equals(value)) return;
                state = value;
                WriteLogEntry($"Status={state.Status}; {state.LastError}");
            }
        }

        public void Run()
        {
            IntPtr dispatcher = IntPtr.Zero;
            try
            {
                string cert = getCertificate();
                if (string.IsNullOrEmpty(cert))
                {
                    SetState(ClientStatus.NoCertificate);
                    return;
                }
                dispatcher = Wrapper.CreateDispatcher(OnConfigRequest, OnDeploymentAction, OnCancelAction);
                SetState(ClientStatus.Running);
                Wrapper.RunClient(cert, settingsManager.ProvisioningUrl, settingsManager.XApigToken, dispatcher, OnAuthErrorAction);
                SetState(ClientStatus.Stopped);
            }
            catch (Exception e)
            {
                SetState(ClientStatus.Stopped, e.Message);
            }
            finally
            {
                if (dispatcher != IntPtr.Zero)
                {
                    Wrapper.DeleteDispatcher(dispatcher);
                }
            }
        }

        private void SetState(ClientStatus status, string lastError = null)
        {
            State = new ClientState(status, lastError ?? string.Empty);
        }

        private IEnumerable<KeyValuePair> GetSystemInfo()
        {
            SystemInfo sysInfo = getSysInfo();
            yield return new KeyValuePair("client", ClientType);
            yield return new KeyValuePair("computer", sysInfo.MachineName);
            yield return new KeyValuePair("platform", sysInfo.PlatformID.ToString());
            yield return new KeyValuePair("OS type", sysInfo.Is64Bit ? "64-bit" : "32-bit");
            yield return new KeyValuePair("version", sysInfo.VersionString);
            yield return new KeyValuePair("service pack", sysInfo.ServicePack);
        }

        private void OnConfigRequest(IntPtr responseBuilder)
        {
            WriteLogEntry("configuration requested.");

            foreach (var attribute in GetSystemInfo())
            {
                Wrapper.AddConfigAttribute(responseBuilder, attribute.Key, attribute.Value);
            }
        }

        private void OnDeploymentAction(IntPtr artifact, DeploymentInfo info, out ClientResult result)
        {
            result = new ClientResult
            {
                Message = string.Empty,
                Success = true
            };

            WriteLogEntry("deployment requested.", info);

            if (!IsExtensionAllowed(info))
            {
                result.Message = "Package is not allowed - deployment rejected";
                WriteLogEntry(result.Message, info);
                result.Success = false;
                return;
            }

            if (!IsSupported(info))
            {
                result.Message = "not supported - deployment rejected";
                WriteLogEntry(result.Message, info);
                result.Success = false;
                return;
            }

            WriteLogEntry("downloading...", info);

            setupManager.OnDownloadStarted(info.artifactFileName);
            try
            {
                Wrapper.DownloadArtifact(artifact, getDownloadLocation());
            }
            catch(Exception)
            {
                result.Message = "download failed.";
                WriteLogEntry(result.Message, info);
                result.Success = false;
                return;
            }

            setupManager.OnDownloadFinished(info.artifactFileName);

            WriteLogEntry("download completed.", info);


            if (info.updateType == "skip")
            {
                result.Message = "skip installation - not requested";
                WriteLogEntry(result.Message, info);
                return;
            }      
            
            var filePath = Path.Combine(getDownloadLocation(), info.artifactFileName);

            WriteLogEntry("installing...", info);
            
            var installPackageStatus = setupManager.InstallPackage(info.artifactFileName);
            if (installPackageStatus != InstallPackageResult.Success && installPackageStatus != InstallPackageResult.RestartNeeded)
            {
                result.Message = "Installation failed.";
                string additionalMessage;
                switch (installPackageStatus)
                {
                    case InstallPackageResult.PackageUnavailable:
                        additionalMessage = "Package unavailable or unusable";
                        break;
                    case InstallPackageResult.FailedToInstallChocoPackage:
                        additionalMessage = "Failed to install Choco package";
                        break;
                    case InstallPackageResult.ChocoNotInstalled:
                        additionalMessage = "Chocolatey is not installed";
                        break;
                    case InstallPackageResult.GeneralInstallationError:
                        additionalMessage = "General installation error";
                        break;
                    case InstallPackageResult.SignatureVerificationFailed:
                        additionalMessage = "Signature verification for the package is failed. " + 
                            $"Requested level: {settingsManager.SignatureVerificationLevel}. Deployment rejected";
                        break;
                    case InstallPackageResult.PackageNotSupported:
                        additionalMessage = "Package of this type is not supported";
                        break;
                    case InstallPackageResult.CannotStartInstaller:
                        additionalMessage = "Failed to start installer process";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (additionalMessage != string.Empty)
                {
                    result.Message += Environment.NewLine + additionalMessage;
                }

                WriteLogEntry(result.Message, info);
                result.Success = false;
            }
            else
            {
                WriteLogEntry("installation finished.", info);
            }
        }

        private bool IsExtensionAllowed(DeploymentInfo info)
        {
            return settingsManager.PackageExtensionFilterList.Contains(Path.GetExtension(info.artifactFileName).ToLowerInvariant());
        }

        private bool IsSupported(DeploymentInfo info)
        {
            return supportedTypes.Contains(Path.GetExtension(info.artifactFileName).ToLowerInvariant());
        }

        private bool OnCancelAction(int stopId)
        {
            WriteLogEntry("cancel requested; unsupported");

            // todo
            return false;
        }

        private void OnAuthErrorAction(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage)) // means that provisioning error is fixed
            {
                SetState(ClientStatus.Running);
            }
            else
            {
                SetState(ClientStatus.CannotAccessServer, errorMessage);
            }
        }

        private void WriteLogEntry(string message, DeploymentInfo? info = null)
        {
            eventLog?.WriteEntry(info == null
                ? $"Up2date client: {message}"
                : $"Up2date client: {message} Artifact={info.Value.artifactFileName}");
        }
    }
}
