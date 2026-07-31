using System.Runtime.InteropServices;

internal static class SwErrorDecoder
{
    public static WorkerError FromException(Exception ex, string comInterface, Dictionary<string, object?> context)
    {
        if (ex is WorkerException workerEx)
        {
            return workerEx.Error;
        }

        string category = "worker";
        string? hresult = null;
        string code = "WORKER_ERROR";
        string[] remediation = ["Check SolidWorks is running and the document state matches command prerequisites."];

        if (ex is COMException comEx)
        {
            category = "com";
            hresult = $"0x{comEx.HResult & 0xFFFFFFFF:X8}";
            code = DecodeHresultCode(comEx.HResult);
            remediation = DecodeHresultRemediation(comEx.HResult);
        }
        else if (ex is InvalidComObjectException)
        {
            category = "com";
            code = "COM_OBJECT_DISCONNECTED";
            remediation =
            [
                "SolidWorks may have closed or crashed. Restart SolidWorks and retry.",
                "Ensure only one worker holds the COM mutex at a time.",
            ];
        }

        return new WorkerError(
            code,
            ex.Message,
            category,
            Hresult: hresult,
            ComInterface: comInterface,
            Context: context,
            Remediation: remediation,
            DocLink: $"solidworks://errors/{code}");
    }

    public static (string Name, string[] Remediation) DecodeMateError(int mateError)
    {
        // Matches SolidWorks.Interop.swconst.swAddMateError_e
        string name = mateError switch
        {
            0 => "swAddMateError_ErrorUnknown",
            1 => "swAddMateError_NoError",
            2 => "swAddMateError_IncorrectMateType",
            3 => "swAddMateError_IncorrectAlignment",
            4 => "swAddMateError_IncorrectSelections",
            5 => "swAddMateError_OverDefinedAssembly",
            6 => "swAddMateError_IncorrectGearRatios",
            _ => $"swAddMateError_Unknown_{mateError}",
        };

        string[] remediation = mateError switch
        {
            1 =>
            [
                "Mate API reported success. If mateCreated is false, inspect the mate tree anyway.",
            ],
            4 =>
            [
                "Selection marks/entities are wrong for this mate type.",
                "Angle/limit mates also need a reference axis (axis_ref) selected with the angle-mate reference mark.",
                "Run debug_mate_entities to inspect selection marks and entity types.",
            ],
            5 =>
            [
                "Assembly would be over-defined. Check existing mates with list_mates and unfix/float as needed.",
            ],
            _ =>
            [
                "Run debug_mate_entities to inspect selection marks and entity types.",
                "Confirm components are not fixed unless the mate requires it.",
            ],
        };

        return (name, remediation);
    }

    public static string DecodeHresult(int hresult) => $"0x{hresult & 0xFFFFFFFF:X8}";

    private static string DecodeHresultCode(int hresult)
    {
        uint code = (uint)hresult;
        return code switch
        {
            0x800706BA => "COM_RPC_SERVER_UNAVAILABLE",
            0x800706BE => "COM_RPC_FAILED",
            0x800401E3 => "COM_ROT_NOT_RUNNING",
            0x8002802B => "TYPE_E_ELEMENTNOTFOUND",
            0x80004005 => "COM_E_FAIL",
            _ => "COM_ERROR",
        };
    }

    private static string[] DecodeHresultRemediation(int hresult)
    {
        uint code = (uint)hresult;
        return code switch
        {
            0x800706BE =>
            [
                "RPC server unavailable — SolidWorks may be busy or hung. Close duplicate SLDWORKS.exe instances.",
                "Wait for the COM mutex to release if another script is running.",
            ],
            0x800401E3 =>
            [
                "SolidWorks ROT entry missing — launch SolidWorks and retry attach.",
            ],
            0x8002802B =>
            [
                "COM element not found — verify interface/method name and document type.",
            ],
            _ =>
            [
                "Restart SolidWorks if COM calls keep failing.",
                "Run diagnose_com to inspect attach state and interop versions.",
            ],
        };
    }
}
