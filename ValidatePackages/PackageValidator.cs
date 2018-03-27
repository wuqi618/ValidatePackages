namespace ValidatePackages
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.Linq;
    using System.Xml;

    using NuGet;

    public class PackageValidator
    {
        private string _nugetSource;
        private string _packageConfig;
        private List<string> _rootPackages;

        private IPackageRepository _packageRepository;

        private List<Package> _allPackages = new List<Package>();
        private List<Error> _errors = new List<Error>();

        public PackageValidator(string nugetSource, string packageConfig, List<string> rootPackages)
        {
            _nugetSource = nugetSource.TrimEnd('/');
            _packageConfig = packageConfig;
            _rootPackages = rootPackages;

            _packageRepository = PackageRepositoryFactory.Default.CreateRepository($"{_nugetSource}/api/v2");
        }

        public void Validate()
        {
            // get all packages from packages.config

            var xml = new XmlDocument();
            xml.Load(_packageConfig);
            foreach (XmlNode node in xml.SelectNodes("//package"))
            {
                var id = node.Attributes["id"].Value;
                var version = SemanticVersion.Parse(node.Attributes["version"].Value);

                Console.WriteLine($"Loading package: {id}[{version}]");
                _allPackages.Add(new Package
                {
                    Id = id,
                    Version = version,
                    NugetPackage = _packageRepository.FindPackage(id, version, true, true)
                });
            }
            Console.WriteLine("Finish loading packages.");
            Console.WriteLine();

            // build package dependency tree

            var root = new Package();
            // fint all the root packages
            foreach (var package in _allPackages)
            {
                if (_rootPackages.Contains(package.Id) ||
                    !_allPackages.Any(x => x.NugetPackage != null &&
                                           x.NugetPackage.DependencySets.SelectMany(y => y.Dependencies).Any(z => z.Id == package.Id)))
                {
                    root.Dependencies.Add(new Package { Id = package.Id, Version = package.Version, NugetPackage = package.NugetPackage, Parent = root});
                }
            }
            // build the dependencies
            root.Dependencies.ForEach(BuildPackageTree);

            // validate package dependency tree
            _allPackages.Where(o => o.NugetPackage == null).ToList().ForEach(o => AddError(ErrorType.PackageNotFound, $"{o.Id}[{o.Version}] not found in the package source"));

            foreach (var package in root.Dependencies)
            {
                PrintDependencyTree($"{package.Id}[{package.Version}]", 1);

                ValidatePackageTree(package, 2);
            }

            // output errors
            PrintErrors();
        }

        private void BuildPackageTree(Package parentPackage)
        {
            if (parentPackage.NugetPackage == null)
            {
                return;
            }

            foreach (var dependency in parentPackage.NugetPackage.DependencySets.SelectMany(y => y.Dependencies))
            {
                if (IsAncestorsDependency(parentPackage, dependency.Id) || IsCircularDependency(parentPackage, dependency.Id))
                {
                    continue;
                }

                var package = _allPackages.FirstOrDefault(o => o.Id == dependency.Id);
                if (package == null)
                {
                    parentPackage.Dependencies.Add(new Package { Id = dependency.Id, ExpectedVersion = dependency.VersionSpec, Parent = parentPackage });
                }
                else
                {
                    var childPackage = new Package { Id = package.Id, Version = package.Version, ExpectedVersion = dependency.VersionSpec, NugetPackage = package.NugetPackage, Parent = parentPackage };
                    parentPackage.Dependencies.Add(childPackage);
                }
            }

            if (parentPackage.Parent != null && parentPackage.Dependencies.All(o => o.Version == null))
            {
                parentPackage.Dependencies.Clear();
                return;
            }

            foreach (var dependency in parentPackage.Dependencies.Where(o => o.Version != null))
            {
                BuildPackageTree(dependency);
            }
        }

        private void ValidatePackageTree(Package package, int level)
        {
            foreach (var dependency in package.Dependencies)
            {
                PrintDependencyTree($"{dependency.Id}[{dependency.Version}]", level);

                if (level == 2)
                {
                    if (dependency.Version == null)
                    {
                        AddError(ErrorType.MissingDependency, $"{package.Id}[{package.Version}] is missing dependency: {dependency.Id} {dependency.ExpectedVersion}");
                    }
                    else if (dependency.ExpectedVersion != null && !dependency.ExpectedVersion.Satisfies(dependency.Version))
                    {
                        AddError(ErrorType.Incompatible, $"{package.Id}[{package.Version}] has an incompatible dependency: {dependency.Id}[{dependency.Version}], expected version: {dependency.ExpectedVersion}");
                    }
                }
                else if (level > 2)
                {
                    if (dependency.Version != null)
                    {
                        AddError(ErrorType.Redundant, $"{dependency.Id}[{dependency.Version}] is not a direct dependency of any root package, possibly redundant");
                    }
                }

                ValidatePackageTree(dependency, level + 1);
            }

        }

        private bool IsAncestorsDependency(Package package, string id)
        {
            if (package.Parent == null)
            {
                return false;
            }

            foreach (var dependency in package.Parent.Dependencies)
            {
                if (dependency.Id == id)
                {
                    return true;
                }
            }

            return IsAncestorsDependency(package.Parent, id);
        }

        private bool IsCircularDependency(Package package, string id)
        {
            if (package == null)
            {
                return false;
            }

            if (package.Id == id)
            {
                return true;
            }

            return IsCircularDependency(package.Parent, id);
        }

        private void AddError(ErrorType type, string message)
        {
            if (!_errors.Any(o => o.Type == type && o.Message == message))
            {
                _errors.Add(new Error { Type = type, Message = message });
            }
        }

        private void PrintDependencyTree(string message, int level)
        {
            string indent = string.Empty;
            for (var i = 1; i < level; i++)
            {
                indent += "   ";
            }
            Debug.WriteLine(indent + message);
        }

        private void PrintErrors()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (var error in _errors.Where(o => o.Type == ErrorType.PackageNotFound).Select(o => o.Message).Distinct().OrderBy(o => o))
            {
                Console.WriteLine(error);
            }
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            foreach (var error in _errors.Where(o => o.Type == ErrorType.MissingDependency).Select(o => o.Message).Distinct().OrderBy(o => o))
            {
                Console.WriteLine(error);
            }
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Red;
            foreach (var error in _errors.Where(o => o.Type == ErrorType.Incompatible).Select(o => o.Message).Distinct().OrderBy(o => o))
            {
                Console.WriteLine(error);
            }
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var error in _errors.Where(o => o.Type == ErrorType.Redundant).Select(o => o.Message).Distinct().OrderBy(o => o))
            {
                Console.WriteLine(error);
            }
            Console.WriteLine();

            Console.ResetColor();
        }
    }
}
