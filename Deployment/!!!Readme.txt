- fix path to MSBUILD in run_msbuild.bat, if neccessary (in bin folder)
- Start DBreeze\Deployment\!!!build.bat
- Wait until it makes the job done
- Put manually on codeplex, github file DBreeze\Deployment\bin\DBreeze_1_077_2016_0829_ULTIMATE.zip
- Publish manually Nuget file from DBreeze\Deployment\Nuget\Actual\DBreeze.actual.nupkg (it must be already correctly formed by deployer.exe)


Extra:
deployer.exe must be compiled in DEBUG mode
