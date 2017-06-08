using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Amoeba.Service;
using Omnius.Base;
using Omnius.Configuration;
using Omnius.Security;
using Omnius.Wpf;
using Prism.Events;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Amoeba.Interface
{
    class MainWindowViewModel : ManagerBase
    {
        private ServiceManager _serviceManager;

        private Settings _settings;

        public ReactiveCommand<string> LanguageCommand { get; private set; }
        public ReactiveCommand OptionsCommand { get; private set; }

        public ReactiveProperty<bool> IsProgressDialogOpen { get; private set; }

        public ReactiveProperty<WindowSettings> WindowSettings { get; private set; }

        public DynamicOptions DynamicOptions { get; } = new DynamicOptions();

        public CloudControlViewModel CloudControlViewModel { get; private set; }
        public ChatControlViewModel ChatControlViewModel { get; private set; }
        public StoreControlViewModel StoreControlViewModel { get; private set; }

        private CompositeDisposable _disposable = new CompositeDisposable();
        private volatile bool _disposed;

        public MainWindowViewModel()
        {
            this.Init();
        }

        private void Init()
        {
            {
                string configPath = Path.Combine(AmoebaEnvironment.Paths.ConfigPath, "Service");
                if (!Directory.Exists(configPath)) Directory.CreateDirectory(configPath);

                _serviceManager = new ServiceManager(configPath, AmoebaEnvironment.Config.Cache.BlocksPath, BufferManager.Instance);
                _serviceManager.Load();

                if (_serviceManager.BasePath == null)
                {
                    _serviceManager.BasePath = AmoebaEnvironment.Paths.DownloadsPath;
                }

                _serviceManager.Start();
            }

            {
                this.LanguageCommand = new ReactiveCommand<string>().AddTo(_disposable);
                this.LanguageCommand.Subscribe((n) => LanguagesManager.Instance.SetCurrentLanguage(n)).AddTo(_disposable);

                this.OptionsCommand = new ReactiveCommand().AddTo(_disposable);
                this.OptionsCommand.Subscribe(() => this.Options()).AddTo(_disposable);

                this.IsProgressDialogOpen = new ReactiveProperty<bool>().AddTo(_disposable);

                this.WindowSettings = new ReactiveProperty<WindowSettings>().AddTo(_disposable);
            }

            {
                SettingsManager.Instance.Load();
            }

            {
                string configPath = Path.Combine(AmoebaEnvironment.Paths.ConfigPath, "View", "MainWindow");
                if (!Directory.Exists(configPath)) Directory.CreateDirectory(configPath);

                _settings = new Settings(configPath);
                int version = _settings.Load("Version", () => 0);

                this.WindowSettings.Value = _settings.Load(nameof(WindowSettings), () => new WindowSettings());
                this.DynamicOptions.SetProperties(_settings.Load(nameof(DynamicOptions), () => Array.Empty<DynamicOptions.DynamicPropertyInfo>()));
            }

            {
                this.CloudControlViewModel = new CloudControlViewModel(_serviceManager);
                this.ChatControlViewModel = new ChatControlViewModel(_serviceManager);
                this.StoreControlViewModel = new StoreControlViewModel(_serviceManager);
            }
        }

        private void Options()
        {
            Messenger.Instance.GetEvent<OptionsWindowShowEvent>()
                .Publish(new OptionsWindowViewModel(_serviceManager));
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                this.CloudControlViewModel.Dispose();
                this.ChatControlViewModel.Dispose();
                this.StoreControlViewModel.Dispose();

                _settings.Save("Version", 0);
                _settings.Save(nameof(WindowSettings), this.WindowSettings.Value);
                _settings.Save(nameof(DynamicOptions), this.DynamicOptions.GetProperties(), true);

                _disposable.Dispose();

                SettingsManager.Instance.Save();

                _serviceManager.Stop();
                _serviceManager.Save();
                _serviceManager.Dispose();
            }
        }
    }
}
