using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValidatePackages
{
    public class Error
    {
        public ErrorType Type { get; set; }
        public string Message { get; set; }
    }

    public enum ErrorType
    {
        None = 0,
        PackageNotFound,
        MissingDependency,
        Incompatible,
        Redundant
    }
}
