using System.Runtime.InteropServices;

internal static class RunningObjectTable
{
    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public static object GetActiveObject(Guid classId)
    {
        int hr = GetActiveObject(ref classId, IntPtr.Zero, out object activeObject);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return activeObject;
    }
}

internal static class ComClassId
{
    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string progId,
        out Guid classId);

    public static Guid FromProgId(string progId)
    {
        CLSIDFromProgID(progId, out Guid classId);
        return classId;
    }
}
