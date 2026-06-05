using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\Repositories\RevitGeoSuite\bin\Deploy\RevitGeoSuite.SharedUI.dll");
        var names = asm.GetManifestResourceNames();
        
        Console.WriteLine($"Total embedded resources: {names.Length}");
        Console.WriteLine("\nWeb dist resources:");
        
        foreach (var name in names)
        {
            if (name.Contains("Web.dist"))
            {
                Console.WriteLine($"  {name}");
            }
        }
    }
}
