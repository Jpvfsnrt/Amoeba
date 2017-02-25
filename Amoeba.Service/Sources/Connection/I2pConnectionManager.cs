﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Amoeba.Core;
using Omnius.Base;
using Omnius.Configuration;
using Omnius.Net;
using Omnius.Net.I2p;

namespace Amoeba.Service
{
    class I2pConnectionManager : StateManagerBase, ISettings
    {
        private CoreManager _coreManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private string _samBridgeUri;

        private Location _myLocation;

        private string _oldSamBridgeUri;
        private SamManager _samManager;

        private Regex _regex = new Regex(@"(.*?):(.*):(\d*)");

        private Thread _watchThread;

        private volatile ManagerState _state = ManagerState.Stop;

        private readonly object _lockObject = new object();
        private volatile bool _disposed;

        public I2pConnectionManager(string configPath, CoreManager coreManager, BufferManager bufferManager)
        {
            _coreManager = coreManager;
            _bufferManager = bufferManager;

            _settings = new Settings(configPath);

            _coreManager.ConnectCapEvent = (_, uri) => this.ConnectCap(uri);
            _coreManager.AcceptCapEvent = (_) => this.AcceptCap();
        }

        public Location MyLocation
        {
            get
            {
                lock (_lockObject)
                {
                    return _myLocation;
                }
            }
        }

        public string SamBridgeUri
        {
            get
            {
                lock (_lockObject)
                {
                    return _samBridgeUri;
                }
            }
            set
            {
                lock (_lockObject)
                {
                    _samBridgeUri = value;
                }
            }
        }

        public Cap ConnectCap(string uri)
        {
            if (_disposed) return null;
            if (this.State == ManagerState.Stop) return null;

            if (!uri.StartsWith("i2p:")) return null;

            try
            {
                string scheme = null;
                string host = null;

                {
                    var regex = new Regex(@"(.*?):(.*)");
                    var match = regex.Match(uri);

                    if (match.Success)
                    {
                        scheme = match.Groups[1].Value;
                        host = match.Groups[2].Value;
                    }
                }

                if (host == null) return null;

                {
                    string proxyScheme = null;
                    string proxyHost = null;
                    int proxyPort = -1;

                    {
                        var regex = new Regex(@"(.*?):(.*):(\d*)");
                        var match = regex.Match(this.SamBridgeUri);

                        if (match.Success)
                        {
                            proxyScheme = match.Groups[1].Value;
                            proxyHost = match.Groups[2].Value;
                            proxyPort = int.Parse(match.Groups[3].Value);
                        }
                    }

                    if (proxyHost == null) return null;

                    if (scheme == "i2p")
                    {
                        Socket socket = null;

                        try
                        {
                            socket = _samManager.Connect(host);
                        }
                        catch (Exception ex)
                        {
                            if (socket != null)
                            {
                                socket.Dispose();
                                socket = null;
                            }

                            throw;
                        }

                        return new SocketCap(socket);
                    }
                }
            }
            catch (Exception)
            {

            }

            return null;
        }

        public Cap AcceptCap()
        {
            if (_disposed) return null;
            if (this.State == ManagerState.Stop) return null;

            Socket socket = null;

            try
            {
                string base32Address;

                socket = _samManager.Accept(out base32Address);
            }
            catch (SamException)
            {
                if (socket != null) socket.Dispose();

                return null;
            }

            return new SocketCap(socket);
        }

        private void WatchThread()
        {
            var checkSamStopwatch = new Stopwatch();
            checkSamStopwatch.Start();

            for (;;)
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                if (!checkSamStopwatch.IsRunning || checkSamStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    checkSamStopwatch.Restart();

                    if ((_samManager == null || !_samManager.IsConnected)
                        || _oldSamBridgeUri != this.SamBridgeUri)
                    {
                        string i2pUri = null;

                        try
                        {
                            var match = _regex.Match(this.SamBridgeUri);
                            if (!match.Success) throw new Exception();

                            if (match.Groups[1].Value == "tcp")
                            {
                                {
                                    if (_samManager != null)
                                    {
                                        _samManager.Dispose();
                                        _samManager = null;
                                    }

                                    var host = match.Groups[2].Value;
                                    var port = int.Parse(match.Groups[3].Value);

                                    _samManager = new SamManager(host, port, "Amoeba");
                                }

                                var base32Address = _samManager.Start();

                                if (base32Address != null)
                                {
                                    i2pUri = string.Format("i2p:{0}", base32Address);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            if (_samManager != null)
                            {
                                _samManager.Dispose();
                                _samManager = null;
                            }
                        }

                        lock (_lockObject)
                        {
                            _myLocation = new Location(new string[] { i2pUri });
                            _oldSamBridgeUri = this.SamBridgeUri;
                        }
                    }
                }
            }
        }

        public override ManagerState State
        {
            get
            {
                return _state;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            lock (_stateLock)
            {
                lock (_lockObject)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    _watchThread = new Thread(this.WatchThread);
                    _watchThread.Priority = ThreadPriority.Lowest;
                    _watchThread.Name = "I2pConnectionManager_WatchThread";
                    _watchThread.Start();
                }
            }
        }

        public override void Stop()
        {
            lock (_stateLock)
            {
                lock (_lockObject)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;
                }

                _watchThread.Join();
                _watchThread = null;

                if (_samManager != null)
                {
                    _samManager.Dispose();
                    _samManager = null;
                }
            }
        }

        #region ISettings

        public void Load()
        {
            lock (_lockObject)
            {
                int version = _settings.Load("Version", () => 0);

                _samBridgeUri = _settings.Load<string>("SamBridgeUri");
            }
        }

        public void Save()
        {
            lock (_lockObject)
            {
                _settings.Save("Version", 0);

                _settings.Save("SamBridgeUri", _samBridgeUri);
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_samManager != null)
                {
                    _samManager.Dispose();
                    _samManager = null;
                }
            }
        }
    }

    [Serializable]
    class OverlayNetworkManagerException : ManagerException
    {
        public OverlayNetworkManagerException() : base() { }
        public OverlayNetworkManagerException(string message) : base(message) { }
        public OverlayNetworkManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
