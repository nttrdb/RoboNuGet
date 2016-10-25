﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using Newtonsoft.Json;
using Reusable;

namespace SmartNuGetPackager
{
    using Commands;
    using Data;

    internal class Program
    {
        private static Config Config { get; set; }

        private static IEnumerable<PackageNuspec> PackageNuspecs { get; set; }

        internal static bool IncrementPatchVersionEnabled { get; set; }

        private static void Main(string[] args)
        {
            EmbededAssemblyLoader.LoadEmbededAssemblies();
            Config = Config.Load();
            PackageNuspecs = GetPackageNuspecs();

            var menu = new Menu
            {
                SolutionFileName = Config.MsBuild.CurrentProjectFile,
                NuspecFileCount = PackageNuspecs.Count(),
                Execute = ExecuteCommand
            };
            menu.Start();
        }

        private static bool ExecuteCommand(string command)
        {
            var commandParts = command.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            var commandName = commandParts.ElementAt(0);
            var commandArg = commandParts.ElementAtOrDefault(1);

            if (commandName.Equals(BuildCommand.Name, StringComparison.OrdinalIgnoreCase))
            {
                return new BuildCommand
                {
                    MsBuild = Config.MsBuild
                }
                .Execute();
            }

            if (commandName.Equals(PackCommand.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (IncrementPatchVersionEnabled)
                {
                    Config.IncrementPatchVersion();
                    Config.Save();
                }

                var packResults = PackageNuspecs.Select(packageNuspec => new PackCommand
                {
                    PackageNuspec = packageNuspec,
                    Version = Config.FullVersion,
                    Outputdirectory = Config.PackageDirectoryName,
                }
                .Execute())
                .ToList();

                ConsoleColorizer.Render($"<text>&gt;<color fg=\"darkgray\">---</color></text>");

                var all = packResults.All(x => x);

                ConsoleColorizer.Render(all
                    ? $"<text>&gt;<color fg=\"green\">All packages successfuly created.</color> <color fg=\"darkyellow\">(Press Enter to continue)</color></text>"
                    : $"<text>&gt;<color fg=\"green\">Some packages could not be created.</color> <color fg=\"darkyellow\">(Press Enter to continue)</color></text>");
                Console.ReadKey();

                return all;
            }

            if (commandName.Equals(".autover", StringComparison.OrdinalIgnoreCase))
            {
                IncrementPatchVersionEnabled = commandArg == null || bool.Parse(commandArg);
                return true;
            }

            if (commandName.Equals(".exit", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(0);
            }

            return false;
        }

        private static IEnumerable<PackageNuspec> GetPackageNuspecs()
        {
            var directories = Directory.GetDirectories(Directory.GetCurrentDirectory());
            foreach (var directory in directories)
            {
                var packageNuspec = PackageNuspec.From(directory);
                if (packageNuspec == null)
                {
                    continue;
                }
                yield return packageNuspec;
            }
        }
    }

    internal static class CommandFactory
    {
        public static Command CreateCommand()
        {
            return null;
        }
    }

    internal class Menu
    {
        private string _lastCommand;

        private readonly string[] _commands =
        {
            "build",
            "pack",
            "push",
            ".exit",
            ".autover"
        };

        public string SolutionFileName { get; set; }

        public int NuspecFileCount { get; set; }

        public Func<string, bool> Execute { get; set; }

        public void Start()
        {
            do
            {
                Console.Clear();

                ConsoleColorizer.Render($"<text>&gt;<color fg=\"darkgray\">SmartNuGetPackager v1.0.2</color></text>");
                var solutionName = Path.GetFileNameWithoutExtension(SolutionFileName);
                ConsoleColorizer.Render($"<text>&gt;Solution '<color fg=\"yellow\">{solutionName}</color>' ({NuspecFileCount} nuspec{(NuspecFileCount != 1 ? "s" : string.Empty)})</text>");
                ConsoleColorizer.Render($"<text>&gt;<color fg=\"darkgray\">.autover '{Program.IncrementPatchVersionEnabled}'</color></text>");
                ConsoleColorizer.Render($"<text>&gt;<color fg=\"darkgray\">Last command '{(string.IsNullOrEmpty(_lastCommand) ? "N/A" : _lastCommand)}'</color> <color fg=\"darkyellow\">(Press Enter to reuse)</color></text>");
                Console.Write(">");

                var command = Console.ReadLine();

                if (string.IsNullOrEmpty(command))
                {
                    command = _lastCommand;
                }

                if (string.IsNullOrEmpty(command))
                {
                    ConsoleColorizer.Render($"<text>&gt;<color fg=\"red\">Command must not be empty.</color> <color fg=\"darkyellow\">(Press Enter to continue)</color></text>");
                    Console.Write(">");
                    Console.ReadKey();
                    continue;
                }

                if (!command.StartsWith("."))
                {
                    _lastCommand = command;
                }

                var commands = command.Split(' ');
                foreach (var cmd in commands)
                {
                    if (!Execute(cmd))
                    {
                        break;
                    }
                }

            } while (true);
            // ReSharper disable once FunctionNeverReturns
        }
    }

    internal class EmbededAssemblyLoader
    {
        public static void LoadEmbededAssemblies()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var executingAssembly = Assembly.GetExecutingAssembly();

                var resourceName = $"{new AssemblyName(args.Name).Name}.dll";
                var fullResourceName = executingAssembly.GetManifestResourceNames().SingleOrDefault(x => x.EndsWith(resourceName));
                using (var stream = executingAssembly.GetManifestResourceStream(fullResourceName))
                {
                    if (stream == null) { throw new ApplicationException($"Could not find resource '{fullResourceName}'."); }
                    var assemblyData = new byte[stream.Length];
                    stream.Read(assemblyData, 0, assemblyData.Length);
                    return Assembly.Load(assemblyData);
                }
            };
        }
    }
}

namespace SmartNuGetPackager.Commands
{
    using Data;

    internal abstract class Command
    {
        public abstract bool Execute();
    }

    internal abstract class StartProcessCommand : Command
    {
        protected bool RedirectStandardOutput { get; set; }

        protected bool Execute(string fileName, string arguments)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = RedirectStandardOutput ? $"/Q /C {fileName} {arguments}" : $"/Q /C pause | {fileName} {arguments}",
                RedirectStandardOutput = RedirectStandardOutput,
                UseShellExecute = !RedirectStandardOutput
            };

            var process = Process.Start(processStartInfo);
            if (RedirectStandardOutput)
            {
                Console.WriteLine(process.StandardOutput.ReadToEnd());
            }
            process.WaitForExit();
            return process.ExitCode == 0;
        }
    }

    internal class BuildCommand : StartProcessCommand
    {
        public const string Name = "build";

        public MsBuild MsBuild { get; set; }

        public override bool Execute()
        {
            return Execute("msbuild", MsBuild.ToString());
        }
    }

    internal class PackCommand : StartProcessCommand
    {
        public PackCommand()
        {
            RedirectStandardOutput = true;
        }

        public const string Name = "pack";

        public PackageNuspec PackageNuspec { get; set; }

        public string Version { get; set; }

        public string Outputdirectory { get; set; }

        public override bool Execute()
        {
            //Console.WriteLine("> PACK ---");
            //Console.Write("Packaging");
            //Console.ForegroundColor = ConsoleColor.DarkYellow;
            //Console.WriteLine("---");

            UpdatePackageNuspec(PackageNuspec, Version);

            return Execute("nuget", CreatePackCommand());
        }

        private static void UpdatePackageNuspec(PackageNuspec packageNuspec, string packagesVersion)
        {
            var directory = Path.GetDirectoryName(packageNuspec.FileName);
            var packagesConfig = PackagesConfig.From(directory);
            var csProj = CsProj.From(directory);

            foreach (var package in packagesConfig.Packages)
            {
                packageNuspec.AddDependency(package.Id, package.Version);
            }

            foreach (var projectReferenceName in csProj.ProjectReferenceNames)
            {
                packageNuspec.AddDependency(projectReferenceName, packagesVersion);
            }

            packageNuspec.SetVersion(packagesVersion);
            packageNuspec.Save();
        }

        private string CreatePackCommand()
        {
            return
                $"pack " +
                $"\"{PackageNuspec.FileName}\" " +
                $"-properties Configuration=Release " +
                $"-outputdirectory {Outputdirectory}";
        }
    }

    internal class PushCommand : StartProcessCommand
    {
        public const string Name = "push";

        public string PackagesDirectoryName { get; set; }

        public string PackageId { get; set; }

        public string Version { get; set; }

        public string NuGetConfigFileName { get; set; }

        public override bool Execute()
        {
            return Execute("nuget", CreatePushCommand());
        }

        private string CreatePushCommand()
        {
            var nupkgFileName = $"{Path.Combine(PackagesDirectoryName, $"{PackageId}.{Version}.nupkg")}";
            return
                $"push " +
                $"\"{nupkgFileName}\" " +
                $"-configfile {NuGetConfigFileName}";
        }
    }
}

namespace SmartNuGetPackager.Data
{
    internal class Config
    {
        private const string DefaultFileName = "SmartNuGetPackager.json";

        [JsonIgnore]
        public string FileName { get; private set; }

        public string PackageDirectoryName { get; set; }

        public string NuGetConfigName { get; set; }

        public string Version { get; set; }

        [JsonIgnore]
        public string FullVersion => IsPrerelease ? $"{Version}-pre" : Version;

        public bool IsPrerelease { get; set; }

        public MsBuild MsBuild { get; set; }

        public static Config Load()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var fileName = Path.Combine(currentDirectory, DefaultFileName);

            var json = File.ReadAllText(fileName);
            var config = JsonConvert.DeserializeObject<Config>(json);
            config.FileName = fileName;
            return config;
        }

        public void Save()
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(FileName, json);
        }

        public void IncrementPatchVersion()
        {
            Version = Regex.Replace(Version, @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)", m =>
                $"{m.Groups["major"].Value}." +
                $"{m.Groups["minor"]}." +
                $"{int.Parse(m.Groups["patch"].Value) + 1}");
        }
    }

    internal class MsBuild
    {
        public string Target { get; set; }

        public bool NoLogo { get; set; }

        public Dictionary<string, string> Properties { get; set; }

        public string ProjectFile { get; set; }

        [JsonIgnore]
        public string CurrentProjectFile
        {
            get
            {
                if (!string.IsNullOrEmpty(ProjectFile))
                {
                    return ProjectFile;
                }

                var sln = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.sln").SingleOrDefault();
                if (string.IsNullOrEmpty(sln))
                {
                    throw new InvalidOperationException($"Solution file not found in \"{Directory.GetCurrentDirectory()}\".");
                }

                return sln;
            }
        }

        public override string ToString()
        {
            var arguments = new List<string>();
            if (!string.IsNullOrEmpty(Target))
            {
                arguments.Add($"/target:{Target}");
            }

            if (NoLogo)
            {
                arguments.Add("/nologo");
            }

            foreach (var property in Properties)
            {
                arguments.Add($"/property:{property.Key}=\"{property.Value}\"");
            }

            arguments.Add(ProjectFile);

            return string.Join(" ", arguments);
        }

        public static implicit operator string(MsBuild msBuild)
        {
            return msBuild.ToString();
        }
    }

    internal class PackageNuspec
    {
        private readonly XDocument _packageNuspec;

        public PackageNuspec(string fileName)
        {
            _packageNuspec = XDocument.Load(FileName = fileName);
            RemoveDependencies();
        }

        public static PackageNuspec From(string dirName)
        {
            var packageNuspecFileName = Directory.GetFiles(dirName, "*.nuspec").SingleOrDefault();
            return packageNuspecFileName == null ? null : new PackageNuspec(packageNuspecFileName);
        }

        public string FileName { get; }

        public string Id
        {
            get
            {
                var xId = ((IEnumerable)_packageNuspec.XPathEvaluate(@"package/metadata/id")).Cast<XElement>().Single();
                return xId.Value;
            }
        }

        public void SetVersion(string id)
        {
            var xVersion = ((IEnumerable)_packageNuspec.XPathEvaluate(@"package/metadata/version")).Cast<XElement>().Single();
            xVersion.Value = id;
        }

        public void AddDependency(string id, string version)
        {
            var xDependencies = ((IEnumerable)_packageNuspec.XPathEvaluate(@"package/metadata/dependencies")).Cast<XElement>().SingleOrDefault();
            if (xDependencies == null)
            {
                var xMetadata = ((IEnumerable)_packageNuspec.XPathEvaluate(@"package/metadata")).Cast<XElement>().Single();
                xMetadata.Add(xDependencies = new XElement("dependencies"));
            }
            xDependencies.Add(new XElement("dependency", new XAttribute("id", id), new XAttribute("version", version)));
        }

        private void RemoveDependencies()
        {
            var xDependencies = ((IEnumerable)_packageNuspec.XPathEvaluate(@"package/metadata/dependencies")).Cast<XElement>().SingleOrDefault();
            xDependencies?.Remove();
        }

        public void Save()
        {
            _packageNuspec.Save(FileName, SaveOptions.None);
        }
    }

    internal class PackagesConfig
    {
        private PackagesConfig(string fileName)
        {
            if (!File.Exists(fileName))
            {
                Packages = Enumerable.Empty<PackageElement>();
                return;
            }

            var packagesConfig = XDocument.Load(FileName = fileName);

            var packages = ((IEnumerable)packagesConfig.XPathEvaluate(@"packages/package"))?.Cast<XElement>();
            Packages = packages.Select(x => new PackageElement
            {
                Id = x.Attribute("id").Value,
                Version = x.Attribute("version").Value
            }).ToList();
        }

        private string FileName { get; }

        public IEnumerable<PackageElement> Packages { get; }

        public static PackagesConfig From(string dirName)
        {
            var packagesConfigFileName = Path.Combine(dirName, "packages.config");
            return new PackagesConfig(packagesConfigFileName);
        }

        internal class PackageElement
        {
            public string Id { get; set; }
            public string Version { get; set; }
        }
    }

    internal class CsProj
    {
        private CsProj(IEnumerable<string> projectReferenceNames)
        {
            ProjectReferenceNames = projectReferenceNames;
        }

        public IEnumerable<string> ProjectReferenceNames { get; }

        public static CsProj From(string dirName)
        {
            var csprojFileName = Directory.GetFiles(dirName, "*.csproj").Single();
            var csproj = XDocument.Load(csprojFileName);
            var projectReferenceNames =
                ((IEnumerable)csproj.XPathEvaluate("//*[contains(local-name(), 'ProjectReference')]"))
                .Cast<XElement>()
                .Select(x => x.Element(XName.Get("Name", csproj.Root.GetDefaultNamespace().NamespaceName)).Value)
                .ToList();

            return new CsProj(projectReferenceNames);
        }
    }
}
