# Third-party notices

Empiro IP Scanner's own source is licensed under GPL-3.0-only. Dependencies retain their respective copyrights and license terms. This inventory covers direct dependencies and known runtime considerations; it is not a complete transitive software bill of materials.

| Component | Version used | License / notes |
|---|---|---|
| Emgu.CV | 4.13.0.5924 | GPLv3 open-source option; commercial terms are an alternative offered by its vendor. This project uses the open-source option. |
| Emgu.CV.runtime.windows | 4.13.0.5924 | Emgu's GPLv3 open-source terms; bundled native components carry additional notices. |
| Microsoft.ML.OnnxRuntime.Gpu.Windows | 1.30.0 | Refer to the package LICENSE and ThirdPartyNotices files; GPU components may have additional redistribution terms. |
| Microsoft.NET.Test.Sdk | 17.14.1 | Build/test dependency; retain the package's applicable notices. |
| xUnit | 2.9.3 | Build/test dependency; retain the package's applicable notices. |
| xunit.runner.visualstudio | 3.1.5 | Build/test dependency; retain the package's applicable notices. |
| .NET / WPF | .NET 8 | Framework/runtime terms and notices apply to self-contained distribution. |
| Pillow | optional developer tool | Used only to regenerate branding assets; not part of the C# runtime. |

## Distribution

The repository contains project source, documentation and synthetic screenshots; it does not bundle NuGet binaries, CUDA/cuDNN, ONNX model weights or installed SDKs. Restore resolves dependencies from their publishers. A future binary release must include applicable third-party notices and satisfy GPL corresponding-source obligations as well as native-runtime redistribution requirements. Do not assume the project license replaces licenses supplied with dependency packages.

Models are user-supplied. Supported output formats do not grant rights to use or redistribute any particular model. Review the chosen model's license separately.

## References

- [Emgu licensing](https://www.emgu.com/wiki/index.php/Licensing:)
- [Emgu source](https://github.com/emgucv/emgucv)
- [ONNX Runtime license](https://github.com/microsoft/onnxruntime/blob/main/LICENSE)
- [xUnit source and license](https://github.com/xunit/xunit)
- [GNU GPL version 3](https://www.gnu.org/licenses/gpl-3.0.html)

The Emgu terms were checked against LICENSE.txt included with the restored 4.13.0.5924 NuGet package on 2026-11-25.
