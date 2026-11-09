using System.Runtime.InteropServices;

namespace App.Domain;

/// <summary>
/// What the developer environment of Visual Studio is set up with.
/// </summary>
internal static class VisualStudioEnvironment
{
    /// <summary>
    /// The architecture the tools run on and build for, which is that of the machine, as VsDevCmd would use x86 otherwise.
    /// </summary>
    public static string HostArchitecture { get; } = RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "amd64",
    };

    /// <summary>The arguments of VsDevCmd.</summary>
    public static string DevCmdArguments { get; } = $"-arch={HostArchitecture} -host_arch={HostArchitecture} -no_logo";
}
