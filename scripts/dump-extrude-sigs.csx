#r "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll"
using System;
using System.Linq;
using SolidWorks.Interop.sldworks;

foreach (var m in typeof(FeatureManager).GetMethods().Where(x => x.Name.Contains("FeatureExtrusion")))
{
    Console.WriteLine($"{m.Name}({m.GetParameters().Length})");
}

foreach (var m in typeof(FeatureManager).GetMethods().Where(x => x.Name.Contains("FeatureCut")))
{
    Console.WriteLine($"{m.Name}({m.GetParameters().Length})");
}
