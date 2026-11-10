# Third-party notices

Terminal Manager includes or depends on the third-party material listed below. It is used under the terms of the respective owners; nothing here transfers any rights in their names, logos or other marks.

## Logos and trademarks

The project type icons in `src/App.Client.WinUI3/Assets/` depict the products and technologies a project can run in. All names and logos are trademarks or registered trademarks of their respective owners. They identify the technology and do not imply affiliation with, sponsorship by or endorsement from those owners.

| File | Depicts | Owner |
| --- | --- | --- |
| `docker.svg` | Docker | Docker, Inc. |
| `kubernetes.svg` | Kubernetes | The Linux Foundation / The Kubernetes Authors |
| `node.svg` | Node.js | OpenJS Foundation |
| `python.svg` | Python | Python Software Foundation |
| `dotnet.svg` | .NET | Microsoft Corporation / .NET Foundation |
| `pwsh.svg` | PowerShell | Microsoft Corporation |
| `azure-vm.svg` | Azure Virtual Machine | Microsoft Corporation |
| `linux.svg` | Tux, the Linux mascot | see below |

Linux is a registered trademark of Linus Torvalds. Tux was originally created by Larry Ewing using GIMP.

<!-- TODO before publishing: confirm where each of these files came from and that its license allows redistribution in this repository (for example the Azure icon set terms, the Tux vector's license, and each brand's guidelines), then add the exact source and license per file. -->

## Completion icons

`src/App.Client.WinUI3/Assets/Completion/*.svg` are the small icons shown next to completion items (class, method, property, parameter, keyword, namespace and so on). They are Microsoft assets and remain subject to the license they were obtained under.

<!-- TODO before publishing: confirm the source of these icons (for example the Visual Studio Image Library) and copy its license terms here, or replace them with icons you can relicense. -->

## NuGet packages

These are referenced from the projects in this repository and are not copied into it. They are restored at build time, and the self-contained build of the app redistributes the runtime pieces. Licenses were read from each package's metadata.

### Application dependencies

| Package | License |
| --- | --- |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Microsoft.Extensions.DependencyInjection](https://dot.net/) and its abstractions, Logging.Abstractions, ObjectPool, Primitives | MIT |
| [System.Memory.Data](https://dot.net/), [System.Numerics.Tensors](https://dot.net/) | MIT |
| [Microsoft.WindowsAppSDK](https://github.com/microsoft/windowsappsdk) and its component packages (Base, Foundation, WinUI, InteractiveExperiences, DWrite, Runtime, Widgets, Search, AI, ML) | Microsoft Software License Terms, supplied as `license.txt` in each package |
| [Microsoft.Windows.AI.MachineLearning](https://github.com/microsoft/windowsml) | Microsoft Software License Terms, supplied as `license.txt` in the package |
| [Microsoft.Web.WebView2](https://aka.ms/webview) | Microsoft license, supplied as `LICENSE.txt` in the package |
| [Microsoft.DotNet.ILCompiler](https://dot.net/) and its runtime package, [Microsoft.NET.ILLink.Tasks](https://dot.net/) | MIT |

### Build-time only

These help build the app and are not part of what it ships.

| Package | License |
| --- | --- |
| [Microsoft.Windows.CsWin32](https://github.com/Microsoft/CsWin32) | MIT |
| [Microsoft.Windows.SDK.BuildTools](https://aka.ms/WinSDKProjectURL), BuildTools.MSIX, Win32Metadata, Win32Docs, [Microsoft.Windows.WDK.Win32Metadata](https://github.com/microsoft/wdkmetadata) | Windows SDK license terms |

### Tests and benchmarks

| Package | License |
| --- | --- |
| [MSTest](https://github.com/microsoft/testfx) | MIT |
| [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) | MIT |

### Libraries by the same author

[PeterJuhasz.Repositories.FileSystem](https://github.com/Peter-Juhasz/repos), [PeterJuhasz.Repositories.Json](https://github.com/Peter-Juhasz/repos), [PeterJuhasz.Repositories.Abstractions](https://github.com/Peter-Juhasz/repos), [PeterJuhasz.Text.Yaml](https://github.com/Peter-Juhasz/yaml) and [PeterJuhasz.Extensions](https://github.com/Peter-Juhasz/extensions) are maintained by the author of this project. Their package metadata does not state a license yet.
