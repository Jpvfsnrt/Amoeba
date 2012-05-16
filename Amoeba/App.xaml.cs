﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Ionic.Zip;
using Library;
using Library.Net.Amoeba;

namespace Amoeba
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        public static Version AmoebaVersion { get; private set; }
        public static Dictionary<string, string> DirectoryPaths { get; private set; }
        public static string[] UpdateSignature { get; private set; }
        public static Node[] Nodes { get; private set; }
        public static string SelectTab { get; set; }
        private FileStream _lockStream = null;

        public App()
        {
            //System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            App.AmoebaVersion = new Version(0, 1, 5);

            App.DirectoryPaths = new Dictionary<string, string>();
            App.DirectoryPaths["Base"] = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            App.DirectoryPaths["Core"] = Path.Combine(App.DirectoryPaths["Base"], "Core");
            Directory.SetCurrentDirectory(App.DirectoryPaths["Core"]);

            App.DirectoryPaths["Configuration"] = Path.Combine(App.DirectoryPaths["Base"], "Configuration");
            App.DirectoryPaths["Update"] = Path.Combine(App.DirectoryPaths["Base"], "Update");
            App.DirectoryPaths["Log"] = Path.Combine(App.DirectoryPaths["Base"], "Log");
            App.DirectoryPaths["Icons"] = Path.Combine(App.DirectoryPaths["Core"], "Icons");
            App.DirectoryPaths["Languages"] = Path.Combine(App.DirectoryPaths["Core"], "Languages");
            App.DirectoryPaths["Input"] = Path.Combine(App.DirectoryPaths["Base"], "Input");

            App.UpdateSignature = new string[] { };
            App.Nodes = new Node[] { };

            foreach (var item in App.DirectoryPaths.Values)
            {
                try
                {
                    if (!Directory.Exists(item))
                    {
                        Directory.CreateDirectory(item);
                    }
                }
                catch (Exception)
                {

                }
            }

            Thread.GetDomain().UnhandledException += new UnhandledExceptionEventHandler(App_UnhandledException);
        }

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    return text;
                }
            }
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    FileStream fs = new FileStream(path, FileMode.CreateNew);
                    return fs;
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {
                    throw;
                }
            }

            for (int index = 1, count = 0; ; index++)
            {
                string text = string.Format(
                    @"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    try
                    {
                        FileStream fs = new FileStream(text, FileMode.CreateNew);
                        return fs;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        count++;
                        if (count > 1024)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            if (exception == null)
                return;

            Log.Error(exception);
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception);
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (e.Args.Length == 2 && e.Args[0] == "Relate")
            {
                if (e.Args[1] == "on")
                {
                    try
                    {
                        string extension = ".box";
                        string commandline = "\"" + Path.Combine(App.DirectoryPaths["Core"], "Amoeba.exe") + "\" \"%1\"";
                        string fileType = "Amoeba";
                        string description = "Amoeba Box";
                        string verb = "open";
                        string iconPath = Path.Combine(App.DirectoryPaths["Icons"], "Box.ico");

                        using (var regkey = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(extension))
                        {
                            regkey.SetValue("", fileType);
                        }

                        using (var shellkey = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(fileType))
                        {
                            shellkey.SetValue("", description);

                            using (var shellkey2 = shellkey.CreateSubKey("shell\\" + verb))
                            {
                                using (var shellkey3 = shellkey2.CreateSubKey("command"))
                                {
                                    shellkey3.SetValue("", commandline);
                                    shellkey3.Close();
                                }
                            }
                        }

                        using (var iconkey = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(fileType + "\\DefaultIcon"))
                        {
                            iconkey.SetValue("", "\"" + iconPath + "\"");
                        }
                    }
                    catch (Exception)
                    {

                    }

                    this.Shutdown();

                    return;
                }
                else if (e.Args[1] == "off")
                {
                    try
                    {
                        string extension = ".box";
                        string fileType = "Amoeba";

                        Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree(extension);
                        Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree(fileType);
                    }
                    catch (Exception)
                    {

                    }

                    this.Shutdown();

                    return;
                }
            }
            else if (e.Args.Length == 1 && e.Args[0].EndsWith(".box") && File.Exists(e.Args[0]))
            {
                try
                {
                    if (Path.GetExtension(e.Args[0]).ToLower() == ".box")
                    {
                        if (!Directory.Exists(App.DirectoryPaths["Input"]))
                            Directory.CreateDirectory(App.DirectoryPaths["Input"]);

                        File.Copy(e.Args[0], App.GetUniqueFilePath(Path.Combine(App.DirectoryPaths["Input"], Path.GetRandomFileName() + "_temp.box")));
                    }
                }
                catch (Exception)
                {

                }
            }
            else if (e.Args.Length >= 1 && e.Args[0].StartsWith("Seed@"))
            {
                try
                {
                    if (!Directory.Exists(App.DirectoryPaths["Input"]))
                        Directory.CreateDirectory(App.DirectoryPaths["Input"]);

                    using (FileStream stream = App.GetUniqueFileStream(Path.Combine(App.DirectoryPaths["Input"], "seed.txt")))
                    using (StreamWriter writer = new StreamWriter(stream))
                    {
                        foreach (var item in e.Args)
                        {
                            if (item == null || !item.StartsWith("Seed@")) continue;
                            writer.WriteLine(item);
                        }
                    }
                }
                catch (Exception)
                {

                }
            }

            try
            {
                _lockStream = new FileStream(Path.Combine(App.DirectoryPaths["Configuration"], "Amoeba.lock"), FileMode.Create);
            }
            catch (IOException)
            {
                this.Shutdown();

                return;
            }

            // Update
            {
                if (Directory.Exists(App.DirectoryPaths["Update"]))
                {
                    Regex regex = new Regex(@"Amoeba ((\d*)\.(\d*)\.(\d*)).*\.zip");
                    Version version = App.AmoebaVersion;
                    string updatePath = null;

                    foreach (var path in Directory.GetFiles(App.DirectoryPaths["Update"]))
                    {
                        string name = Path.GetFileName(path);

                        if (name.StartsWith("Amoeba"))
                        {
                            var match = regex.Match(name);

                            if (match.Success)
                            {
                                var tempVersion = new Version(match.Groups[1].Value);

                                if (version < tempVersion)
                                {
                                    version = tempVersion;
                                    updatePath = path;
                                }
                            }
                        }
                    }

                    if (updatePath != null)
                    {
                        var tempPath = Path.Combine(Path.GetTempPath(), "Amoeba_Update");

                        if (Directory.Exists(tempPath))
                            Directory.Delete(tempPath, true);

                        try
                        {
                            using (ZipFile zipfile = new ZipFile(updatePath))
                            {
                                zipfile.ExtractExistingFile = ExtractExistingFileAction.OverwriteSilently;
                                zipfile.UseUnicodeAsNecessary = true;
                                zipfile.ExtractAll(tempPath);
                            }
                        }
                        catch (Exception)
                        {
                            return;
                        }
                        finally
                        {
                            if (File.Exists(updatePath))
                                File.Delete(updatePath);
                        }

                        var tempUpdateExePath = Path.Combine(Path.GetTempPath(), "Library.Update.exe");

                        if (File.Exists(tempUpdateExePath))
                            File.Delete(tempUpdateExePath);

                        File.Copy("Library.Update.exe", tempUpdateExePath);

                        ProcessStartInfo startInfo = new ProcessStartInfo();
                        startInfo.FileName = tempUpdateExePath;
                        startInfo.Arguments = string.Format("\"{0}\" \"{1}\" \"{2}\" \"{3}\"",
                            Process.GetCurrentProcess().Id,
                            Path.Combine(tempPath, "Core"),
                            Directory.GetCurrentDirectory(),
                            Path.Combine(Directory.GetCurrentDirectory(), "Amoeba.exe"));
                        startInfo.WorkingDirectory = Path.GetDirectoryName(startInfo.FileName);

                        Process.Start(startInfo);

                        this.Shutdown();

                        return;
                    }
                }
            }

            this.Setting();

            this.StartupUri = new Uri("Windows/MainWindow.xaml", UriKind.Relative);
        }

        private void Setting()
        {
            if (!File.Exists(Path.Combine(App.DirectoryPaths["Configuration"], "UpdateSignature.txt")))
            {
                using (StreamWriter writer = new StreamWriter(Path.Combine(App.DirectoryPaths["Configuration"], "UpdateSignature.txt"), false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("kbMq8T1x_bwrJ--Wzwyu");
                }
            }

            using (StreamReader reader = new StreamReader(Path.Combine(App.DirectoryPaths["Configuration"], "UpdateSignature.txt"), new UTF8Encoding(false)))
            {
                string item = null;
                List<string> list = new List<string>();

                while ((item = reader.ReadLine()) != null)
                {
                    list.Add(item);
                }

                App.UpdateSignature = list.ToArray();
            }

            if (!File.Exists(Path.Combine(App.DirectoryPaths["Configuration"], "Nodes.txt")))
            {
                using (StreamWriter writer = new StreamWriter(Path.Combine(App.DirectoryPaths["Configuration"], "Nodes.txt"), false, new UTF8Encoding(false)))
                {
                }
            }

            using (StreamReader reader = new StreamReader(Path.Combine(App.DirectoryPaths["Configuration"], "Nodes.txt"), new UTF8Encoding(false)))
            {
                string item = null;
                List<Node> list = new List<Node>();

                while ((item = reader.ReadLine()) != null)
                {
                    try
                    {
                        list.Add(AmoebaConverter.FromNodeString(item));
                    }
                    catch (Exception)
                    {

                    }
                }

                App.Nodes = list.ToArray();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            if (_lockStream != null)
            {
                _lockStream.Close();
                _lockStream = null;
            }
        }
    }
}
