﻿using System.Collections.Generic;

namespace Up2dateShared
{
    public interface ISetupManager
    {
        List<Package> GetAvaliablePackages();
        InstallPackageResult InstallPackage(string packageFile);
        void InstallPackages(IEnumerable<Package> packages);
        void OnDownloadStarted(string artifactFileName);
        void OnDownloadFinished(string artifactFileName);
        bool IsFileSupported(string artifactFileName);

        /// <summary>
        /// Gets collection of package extensions suppoted by SetupManager
        /// </summary>
        IEnumerable<string> SupportedExtensions { get; }
    }
}