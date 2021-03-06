using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using JetBrains.Annotations;
using Reusable.Commander;
using Reusable.CommandLine;
using Reusable.ConsoleColorizer;
using Reusable.MarkupBuilder.Html;
using Reusable.OmniLog;
using RoboNuGet.Files;

namespace RoboNuGet.Commands
{
    [UsedImplicitly]
    internal class List : ConsoleCommand
    {
        private readonly RoboNuGetFile _roboNuGetFile;
        private readonly IFileSearch _fileSearch;

        public List(ILoggerFactory loggerFactory, RoboNuGetFile roboNuGetFile, IFileSearch fileSearch) : base(loggerFactory)
        {
            _roboNuGetFile = roboNuGetFile;
            _fileSearch = fileSearch;
        }

        [Parameter, Alias("s")]
        public bool Short { get; set; }

        public override Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var solutionFileName = _fileSearch.FindSolutionFile();
            var nuspecFiles = _fileSearch.FindNuspecFiles();

            foreach (var nuspecFile in nuspecFiles.OrderBy(x => x.FileName))
            {
                var nuspecDirectoryName = Path.GetDirectoryName(nuspecFile.FileName);
                var packagesConfig = PackagesConfigFile.Load(nuspecDirectoryName);

                var csProj = CsProjFile.Load(Path.Combine(nuspecDirectoryName, $"{nuspecFile.Id}{CsProjFile.DefaultExtension}"));
                var projectDependencies = csProj.ProjectReferences.Select(projectReferenceName => new NuspecDependency { Id = projectReferenceName, Version = _roboNuGetFile.FullVersion }).ToList();
                var packageDependencies = packagesConfig.Packages.Select(package => new NuspecDependency { Id = package.Id, Version = package.Version }).ToList();

                var dependencyCount = projectDependencies.Count + packageDependencies.Count;

                //dependencyCount = nuspecFile.Dependencies.Count();

                if (!Short)
                {
                    Logger.ConsoleMessageLine(m => m);
                }
                Logger.ConsoleMessageLine(m => m
                    .Indent()
                    .text($"{Path.GetFileNameWithoutExtension(nuspecFile.FileName)} ")
                    .span(s => s.text($"({dependencyCount})").color(ConsoleColor.Magenta)));

                if (!Short)
                {
                    ListDependencies("Projects", projectDependencies.OrderBy(x => x.Id));
                    ListDependencies("Packages", packageDependencies.OrderBy(x => x.Id));
                }
            }

            return Task.CompletedTask;
        }

        private void ListDependencies(string header, IEnumerable<NuspecDependency> dependencies)
        {
            Logger.ConsoleMessageLine(p => p.Indent(2).span(s => s.text($"[{header}]").color(ConsoleColor.DarkGray)));

            foreach (var nuspecDependency in dependencies)
            {
                Logger.ConsoleMessageLine(p => p
                    .Indent(2)
                    .text($"- {nuspecDependency.Id} ")
                    .span(s => s.text($"v{nuspecDependency.Version}").color(ConsoleColor.DarkGray)));
            }
        }
    }
}