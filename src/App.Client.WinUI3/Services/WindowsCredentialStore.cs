using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Credentials;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Keeps secrets in the Credential Manager of Windows, as generic credentials of the user, which are encrypted for them, and can be seen and removed in it.
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    /// <inheritdoc/>
    public unsafe string? Read(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);

        // the raw pointer overload is used, as newer versions of CsWin32 wrap the friendly one in a safe handle
        CREDENTIALW* credential;
        fixed (char* targetName = target)
        {
            if (!PInvoke.CredRead(targetName, CRED_TYPE.CRED_TYPE_GENERIC, 0, &credential))
            {
                return (WIN32_ERROR)Marshal.GetLastPInvokeError() switch
                {
                    WIN32_ERROR.ERROR_NOT_FOUND => null,
                    var error => throw new Win32Exception((int)error),
                };
            }
        }

        try
        {
            return Encoding.Unicode.GetString(credential->CredentialBlob, (int)credential->CredentialBlobSize);
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    /// <inheritdoc/>
    public unsafe void Write(string target, string userName, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(secret);

        // kept as UTF-16, as the Credential Manager shows and keeps passwords
        var blob = Encoding.Unicode.GetBytes(secret);
        fixed (char* targetName = target)
        fixed (char* user = userName)
        fixed (byte* blobPointer = blob)
        {
            CREDENTIALW credential = new()
            {
                Type = CRED_TYPE.CRED_TYPE_GENERIC,
                TargetName = targetName,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
                UserName = user,
            };

            if (!PInvoke.CredWrite(credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }
}
