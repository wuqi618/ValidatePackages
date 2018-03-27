namespace ValidatePackages
{
    using System;
    using System.Configuration;
    using System.Linq;

    class Program
    {
        static void Main(string[] args)
        {
            var packageValidator = new PackageValidator(
                ConfigurationManager.AppSettings["NugetSource"],
                ConfigurationManager.AppSettings["PackageConfig"],
                ConfigurationManager.AppSettings["RootPackages"].Split(',').Select(o => o.Trim()).ToList());

            packageValidator.Validate();

            Console.WriteLine("Completed.");
            Console.Read();
        }
    }
}
