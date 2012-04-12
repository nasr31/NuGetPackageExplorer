﻿using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NuGet;
using NuGetPackageExplorer.Types;

namespace PackageExplorerViewModel
{
    public sealed class PublishPackageViewModel : ViewModelBase, IObserver<int>, IDisposable
    {
        private readonly MruPackageSourceManager _mruSourceManager;
        private readonly IPackageMetadata _package;
        private readonly Lazy<Stream> _packageStream;
        private readonly ISettingsManager _settingsManager;
        private bool _canPublish = true;
        private bool _hasError;
        private string _publishKey;
        private bool? _useV1Protocol = true;
        private string _selectedPublishItem;
        private bool _showProgress;
        private string _status;
        private bool _suppressReadingApiKey;
        private IGalleryServer _uploadHelper;

        public PublishPackageViewModel(
            MruPackageSourceManager mruSourceManager,
            ISettingsManager settingsManager,
            PackageViewModel viewModel)
        {
            _mruSourceManager = mruSourceManager;
            _settingsManager = settingsManager;
            _package = viewModel.PackageMetadata;
            _packageStream = new Lazy<Stream>(viewModel.GetCurrentPackageStream);
            SelectedPublishItem = _mruSourceManager.ActivePackageSource;
            UseV1Protocol = _settingsManager.UseV1ProtocolForPublish;
        }

        public string PublishKey
        {
            get { return _publishKey; }
            set
            {
                if (_publishKey != value)
                {
                    _publishKey = value;
                    OnPropertyChanged("PublishKey");
                }
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        public string PublishUrl
        {
            get { return _mruSourceManager.ActivePackageSource; }
            set
            {
                if (_mruSourceManager.ActivePackageSource != value)
                {
                    _mruSourceManager.ActivePackageSource = value;
                    OnPropertyChanged("PublishUrl");
                }
            }
        }

        public string SelectedPublishItem
        {
            get { return _selectedPublishItem; }
            set
            {
                if (_selectedPublishItem != value)
                {
                    _selectedPublishItem = value;
                    OnPropertyChanged("SelectedPublishItem");

                    if (value != null)
                    {
                        // store the selected source into settings
                        PublishUrl = value;

                        if (!_suppressReadingApiKey)
                        {
                            // when the selection change, we retrieve the API key for that source
                            string key = _settingsManager.ReadApiKey(value);
                            if (!String.IsNullOrEmpty(key))
                            {
                                PublishKey = key;
                            }
                        }
                    }
                }
            }
        }

        public ObservableCollection<string> PublishSources
        {
            get { return _mruSourceManager.PackageSources; }
        }

        public bool? UseV1Protocol
        {
            get { return _useV1Protocol; }
            set
            {
                if (_useV1Protocol != value)
                {
                    _useV1Protocol = value;
                    OnPropertyChanged("UseV1Protocol");
                }
            }
        }

        public string Id
        {
            get { return _package.Id; }
        }

        public string Version
        {
            get { return _package.Version.ToString(); }
        }

        public bool HasError
        {
            get { return _hasError; }
            set
            {
                if (_hasError != value)
                {
                    _hasError = value;
                    OnPropertyChanged("HasError");
                }
            }
        }

        public bool ShowProgress
        {
            get { return _showProgress; }
            set
            {
                if (_showProgress != value)
                {
                    _showProgress = value;
                    OnPropertyChanged("ShowProgress");
                }
            }
        }

        public bool CanPublish
        {
            get { return _canPublish; }
            set
            {
                if (_canPublish != value)
                {
                    _canPublish = value;
                    OnPropertyChanged("CanPublish");
                }
            }
        }

        public IGalleryServer GalleryServer
        {
            get
            {
                if (_uploadHelper == null ||
                    !PublishUrl.Equals(_uploadHelper.Source, StringComparison.OrdinalIgnoreCase) ||
                    (bool)UseV1Protocol != _uploadHelper.IsV1Protocol)
                {
                    _uploadHelper = GalleryServerFactory.CreateGalleryServer(
                        PublishUrl, HttpUtility.CreateUserAgentString(Constants.UserAgentClient), (bool)UseV1Protocol);
                }
                return _uploadHelper;
            }
        }

        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged("Status");
                }
            }
        }

        #region IObserver<int> Members

        public void OnCompleted()
        {
            ShowProgress = false;
            HasError = false;
            Status = (UseV1Protocol == true) ? "Package pushed successfully." : "Package published successfully.";
            _settingsManager.WriteApiKey(PublishUrl, PublishKey);
            CanPublish = true;
        }

        public void OnError(Exception error)
        {
            ShowProgress = false;
            HasError = true;
            Status = error.Message;
            CanPublish = true;
        }

        public void OnNext(int value)
        {
        }

        #endregion

        public void PushPackage()
        {
            ShowProgress = true;
            Status = "Publishing package...";
            HasError = false;
            CanPublish = false;

            // here we reuse the stream multiple times, so make sure to rewind it to beginning every time.
            _packageStream.Value.Seek(0, SeekOrigin.Begin);

            TaskScheduler uiTaskSchedulker = TaskScheduler.FromCurrentSynchronizationContext();

            Task.Factory.StartNew(
                    () => GalleryServer.PushPackage(PublishKey, _packageStream.Value, this, _package))
                .ContinueWith(task =>
                              {
                                  if (task.IsFaulted)
                                  {
                                      var webException = task.Exception.GetBaseException() as WebException;
                                      if (webException != null && webException.Status == WebExceptionStatus.Timeout)
                                      {
                                          OnError(task.Exception);
                                      }
                                  }

                                  // add the publish url to the list
                                  _mruSourceManager.NotifyPackageSourceAdded(PublishUrl);

                                  // this is to make sure the combo box doesn't goes blank after publishing
                                  try
                                  {
                                      _suppressReadingApiKey = true;
                                      SelectedPublishItem = PublishUrl;
                                  }
                                  finally
                                  {
                                      _suppressReadingApiKey = false;
                                  }
                              }, uiTaskSchedulker);
        }

        public void Dispose()
        {
            _settingsManager.UseV1ProtocolForPublish = (bool)UseV1Protocol;
        }
    }
}