using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValidatePackages
{
    using NuGet;

    public class Package
    {
        public Package()
        {
            Dependencies = new List<Package>();
        }

        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public IVersionSpec ExpectedVersion { get; set; }
        public IPackage NugetPackage { get; set; }
        public List<Package> Dependencies { get; set; }
        public Package Parent { get; set; }

        public override string ToString()
        {
            return $"{Id}";
        }
    }
}
