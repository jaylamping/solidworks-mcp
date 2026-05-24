using System.Linq;
using System.Reflection;

var asm = Assembly.LoadFrom(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll");
foreach (var t in asm.GetTypes().Where(t => t.Name.Contains("FeatureManager")))
{
    Console.WriteLine("TYPE " + t.FullName);
    foreach (var m in t.GetMethods().Where(x => x.Name.StartsWith("FeatureExtrusion")))
    {
        Console.WriteLine("  " + m.Name);
        foreach (var p in m.GetParameters())
            Console.WriteLine("    " + p.ParameterType.Name + " " + p.Name);
    }
    foreach (var m in t.GetMethods().Where(x => x.Name.StartsWith("FeatureCut")))
    {
        Console.WriteLine("  " + m.Name);
        foreach (var p in m.GetParameters())
            Console.WriteLine("    " + p.ParameterType.Name + " " + p.Name);
    }
}
