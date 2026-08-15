using System.Runtime.InteropServices;

namespace Nekomata.UI.Services;

public sealed class PersonalSecretService
{
    private const string OpenAiTarget = "Nekomata Personal/OpenAI API Key";
    public string? OpenAiApiKey => Read(OpenAiTarget);
    public bool HasOpenAiApiKey => !string.IsNullOrWhiteSpace(OpenAiApiKey);
    public void SaveOpenAiApiKey(string apiKey) { if (!string.IsNullOrWhiteSpace(apiKey)) Write(OpenAiTarget, apiKey.Trim()); }
    public void DeleteOpenAiApiKey()
    {
        if (!CredDelete(OpenAiTarget, 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new InvalidOperationException($"Windows Credential Manager could not remove the API key (error {Marshal.GetLastWin32Error()}).");
    }

    private static string? Read(string target)
    {
        if (!CredRead(target, 1, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? null : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    private static void Write(string target, string value)
    {
        var blob = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var credential = new NativeCredential { Type = 1, TargetName = target, CredentialBlobSize = (uint)(value.Length * 2), CredentialBlob = blob, Persist = 2, UserName = Environment.UserName };
            if (!CredWrite(ref credential, 0)) throw new InvalidOperationException($"Windows Credential Manager rejected the API key (error {Marshal.GetLastWin32Error()}).");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags; public uint Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist;
        public uint AttributeCount; public IntPtr Attributes; public string? TargetAlias; public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}
