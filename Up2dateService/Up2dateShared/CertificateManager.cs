﻿using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Up2dateShared
{
    public class CertificateManager : ICertificateManager
    {
        private const StoreName storeName = StoreName.TrustedPublisher;

        private readonly ILogger logger;
        private readonly ISettingsManager settingsManager;
        private X509Certificate2 certificate;

        public X509Certificate2 Certificate
        {
            get => certificate;
            private set
            {
                if (certificate != null)
                {
                    certificate.Dispose();
                }
                certificate = value;
            }
        }

        public string CertificateIssuerName => GetCN(certificate?.Issuer);

        public string CertificateSubjectName => GetCN(certificate?.Subject);

        public CertificateManager(ISettingsManager settingsManager, ILogger logger)
        {
            this.settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ImportCertificate(byte[] certificateData)
        {
            try
            {
                X509Certificate2 cert = new X509Certificate2(certificateData);
                ImportCertificate(cert);
                Certificate = cert;
                logger.WriteEntry($"New certificate imported; '{Certificate.Issuer}:{Certificate.Subject}'");
            }
            catch (Exception e)
            {
                logger.WriteEntry("Exception importing certificate.", e);
                throw;
            }
        }

        public void ImportCertificate(string filePath)
        {
            try
            {
                X509Certificate2 cert = new X509Certificate2(filePath);
                ImportCertificate(cert);
                Certificate = cert;
                logger.WriteEntry($"New certificate imported; '{Certificate.Issuer}:{Certificate.Subject}'");
            }
            catch (Exception e)
            {
                logger.WriteEntry("Exception importing certificate.", e);
                throw;
            }
        }

        public string GetCertificateString()
        {
            if (Certificate == null)
            {
                LoadCertificate();
            }

            if (Certificate == null) return null;

            byte[] arr = Certificate.GetRawCertData();
            return "-----BEGIN CERTIFICATE-----" + Convert.ToBase64String(arr) + "-----END CERTIFICATE-----";
        }

        public bool IsCertificateAvailable()
        {
            return Certificate != null;
        }

        private void LoadCertificate()
        {
            using (X509Store store = new X509Store(storeName, StoreLocation.LocalMachine))
            {
                Certificate = GetCertificates(store)?.OfType<X509Certificate2>().FirstOrDefault();
            }

            if (Certificate != null)
            {
                logger.WriteEntry($"Communication certificate - '{Certificate.Issuer}:{Certificate.Subject}'");
            }
        }

        private X509Certificate2Collection GetCertificates(X509Store store)
        {
            string CertificateThumbprint = settingsManager.CertificateThumbprint;

            if (string.IsNullOrEmpty(CertificateThumbprint)) return null;

            store.Open(OpenFlags.ReadOnly);
            X509Certificate2Collection certificates = store.Certificates
                    .Find(X509FindType.FindByThumbprint, CertificateThumbprint, false);
            store.Close();

            return certificates;
        }

        private void ImportCertificate(X509Certificate2 cert)
        {
            using (X509Store store = new X509Store(storeName, StoreLocation.LocalMachine))
            {
                // first remove old certificate(s)
                X509Certificate2Collection oldCertificates = GetCertificates(store);
                store.Open(OpenFlags.ReadWrite);
                if (oldCertificates != null && oldCertificates.Count > 0)
                {
                    store.RemoveRange(oldCertificates);
                }

                // now add new certificate
                store.Add(cert);
                store.Close();

                // remember new certificate
                settingsManager.CertificateThumbprint = cert.Thumbprint;
            }
        }

        private string GetCN(string fullname)
        {
            const string cnPrefix = "CN=";

            string cnPart = fullname.Split(',').FirstOrDefault(p => p.StartsWith(cnPrefix)).Trim();
            if (string.IsNullOrEmpty(cnPart)) return string.Empty;

            return cnPart.Substring(cnPrefix.Length);
        }
    }
}
