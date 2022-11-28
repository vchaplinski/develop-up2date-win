﻿using System;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Up2dateConsole.Helpers;
using Up2dateConsole.ServiceReference;
using Up2dateConsole.ViewService;

namespace Up2dateConsole.Dialogs.RequestCertificate
{
    public class RequestCertificateDialogViewModel : DialogViewModelBase
    {
        enum ConnectionMode
        {
            Secure,
            Test
        };

        private readonly IViewService viewService;
        private readonly IWcfClientFactory wcfClientFactory;
        private string oneTimeKey;
        private bool isInProgress;
        private ConnectionMode connectionMode;
        private string hawkbitUrl;
        private string controllerId;
        private string deviceToken;
        private bool isCertificateAvailable;

        public RequestCertificateDialogViewModel(IViewService viewService, IWcfClientFactory wcfClientFactory, bool showExplanation)
        {
            this.viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
            this.wcfClientFactory = wcfClientFactory ?? throw new ArgumentNullException(nameof(wcfClientFactory));
            ShowExplanation = showExplanation;

            RequestCommand = new RelayCommand(async (_) => await ExecuteRequestAsync(), CanRequest);
            LoadCommand = new RelayCommand(async (_) => await ExecuteLoadAsync());

            Initialize();
        }

        private void Initialize()
        {
            IWcfService service = null;
            string error = string.Empty;
            try
            {
                service = wcfClientFactory.CreateClient();
                isCertificateAvailable = service.IsCertificateAvailable();
                connectionMode = service.IsUnsafeConnection() ? ConnectionMode.Test : ConnectionMode.Secure;
                hawkbitUrl = service.GetUnsafeConnectionUrl();
                controllerId = service.GetUnsafeConnectionDeviceId();
                MachineGuid = service.GetSystemInfo().MachineGuid;
                if (string.IsNullOrEmpty(controllerId))
                {
                    controllerId = MachineGuid;
                }
                deviceToken = service.GetUnsafeConnectionToken();
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            finally
            {
                wcfClientFactory.CloseClient(service);
                IsInProgress = false;
            }
        }

        public ICommand RequestCommand { get; }

        public ICommand LoadCommand { get; }

        public string MachineGuid { get; private set; }

        public string OneTimeKey
        {
            get => oneTimeKey;
            set
            {
                if (oneTimeKey == value) return;
                oneTimeKey = value;
                OnPropertyChanged();
            }
        }

        public bool IsInProgress
        {
            get => isInProgress;
            set
            {
                if (isInProgress == value) return;
                isInProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEnabled));
            }
        }

        public bool IsEnabled => !IsInProgress;

        public bool ShowExplanation { get; }

        public bool IsSecureConnection
        {
            get => connectionMode == ConnectionMode.Secure;
            set => SetSecureConnectionMode(value);
        }

        public bool IsTestConnection
        {
            get => connectionMode == ConnectionMode.Test;
            set => SetSecureConnectionMode(!value);
        }

        public string HawkbitUrl
        {
            get => hawkbitUrl;
            set
            {
                if (hawkbitUrl == value) return;
                hawkbitUrl = value;
                OnPropertyChanged();
            }
        }

        public string ControllerId
        {
            get => controllerId;
            set
            {
                if (controllerId == value) return;
                controllerId = value;
                OnPropertyChanged();
            }
        }

        public string DeviceToken
        {
            get => deviceToken;
            set
            {
                if (deviceToken == value) return;
                deviceToken = value;
                OnPropertyChanged();
            }
        }

        private void SetSecureConnectionMode(bool value)
        {
            var newConnectionMode = value ? ConnectionMode.Secure : ConnectionMode.Test;
            if (connectionMode == newConnectionMode) return;
            connectionMode = newConnectionMode;

            OnPropertyChanged(nameof(IsTestConnection));
            OnPropertyChanged(nameof(IsSecureConnection));
        }

        private bool CanRequest(object _)
        {
            if (IsSecureConnection) return isCertificateAvailable || !string.IsNullOrWhiteSpace(OneTimeKey);
            if (IsTestConnection) return !string.IsNullOrWhiteSpace(HawkbitUrl) && !string.IsNullOrWhiteSpace(ControllerId) && !string.IsNullOrEmpty(DeviceToken);
            return false;
        }

        private async Task ExecuteRequestAsync()
        {
            await ImportAndApplyCertificateAsync();
        }

        private async Task ExecuteLoadAsync()
        {
            var certFilePath = viewService.ShowOpenDialog(viewService.GetText(Texts.LoadCertificate),
                "X.509 certificate files|*.cer|All files|*.*");
            if (string.IsNullOrWhiteSpace(certFilePath)) return;

            await ImportAndApplyCertificateAsync(certFilePath);
        }

        private async Task ImportAndApplyCertificateAsync(string certFilePath = null)
        {
            IsInProgress = true;

            IWcfService service = null;
            try
            {
                service = wcfClientFactory.CreateClient();
                if (IsTestConnection)
                {
                    await service.SetupUnsafeConnectionAsync(HawkbitUrl, ControllerId, DeviceToken);
                }
                else
                {
                    ResultOfstring result = new ResultOfstring { Success = true };
                    if (!string.IsNullOrWhiteSpace(certFilePath))
                    {
                        result = await service.ImportCertificateAsync(certFilePath);
                        if (!result.Success)
                        {
                            string message = viewService.GetText(Texts.FailedToLoadCertificate) + $"\n\n{result.ErrorMessage}";
                            viewService.ShowMessageBox(message);
                            return;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(OneTimeKey))
                    {
                        result = await service.RequestCertificateAsync(RemoveWhiteSpaces(OneTimeKey));
                        if (!result.Success)
                        {
                            string message = viewService.GetText(Texts.FailedToAcquireCertificate) + $"\n\n{result.ErrorMessage}";
                            viewService.ShowMessageBox(message);
                            return;
                        }
                    }

                    await service.SetupSecureConnectionAsync();
                }

                await service.RestartClientAsync();

                // TODO wait for actual success or exit by timeout!
                await Task.Delay(8000);
                Close(true);
            }
            catch (Exception e)
            {
                string message = viewService.GetText(Texts.ServiceAccessError) + $"\n\n{e.Message}";
                viewService.ShowMessageBox(message);
                return;
            }
            finally
            {
                wcfClientFactory.CloseClient(service);
                IsInProgress = false;
            }
        }

        private static string RemoveWhiteSpaces(string str)
        {
            return new string(str.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }
    }
}
