using System;
using System.Linq;

class P {
  static void Main() {
    var asm = System.Reflection.Assembly.LoadFrom(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll");
    var t = asm.GetType("SolidWorks.Interop.sldworks.FeatureManager");
    foreach (var m in t!.GetMethods().Where(x => x.Name.StartsWith("FeatureExtrusion")))
      Console.WriteLine(m.Name + " :: " + m.GetParameters().Length);
    foreach (var m in t.GetMethods().Where(x => x.Name.StartsWith("FeatureCut")))
      Console.WriteLine(m.Name + " :: " + m.GetParameters().Length);
  }
}
