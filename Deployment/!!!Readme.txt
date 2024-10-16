- fix path to MSBUILD in runV2_msbuild22.bat, if neccessary (in bin folder)
- !!!!!!! Set new Version in !!!DBreeze\Deployment\ChangeVersionAndbuild.bat (versions in projects and DLLs will be set autocatically)
- Start DBreeze\Deployment\!!!ChangeVersionAndbuild.bat   (.NET Framework 3.5 can be installed via Windows components, the others must be installed separately)
- Wait until it makes the job done
- Put manually on codeplex, github file DBreeze\Deployment\bin\DBreeze_1_077_2016_0829_ULTIMATE.zip
- Fix version in Readme
- Publish manually Nuget (https://www.nuget.org/packages/DBreeze/) file from DBreeze\Deployment\Nuget\Actual\DBreeze.actual.nupkg (it must be already correctly formed by deployer.exe)

-Signing
    -In root folder must be file appveyor.yml when it appears it starts to recompile DBreeze on AppVeyor and then becomes visible in SignPath.io
	 We have 2 projects so we create appveyor.yml first from  _appveyor4.7.2 (upload...waiting..getting signed DLL), then the same for _appveyor6.0.yml...
     For now there is no way to make it at once.
     Then appveyor.yml should be removed, so AppVeyor doesn't recompile it on each commit.
	 Signed DLLs must be in DBreeze\Deployment\bin\SignedDLL\

	AppVeyor.com	
	https://ci.appveyor.com/project/hhblaze/dbreeze
	https://www.appveyor.com/docs/status-badges/
	
	SignPath.io
	https://app.signpath.io/Web/75ba23b1-b387-4f6d-861e-f190db22f4f3/Home/Dashboard
	https://about.signpath.io/documentation/origin-verification#
	https://about.signpath.io/documentation/artifact-configuration#predefined-configuration-for-single-portable-executable-file



Extra:
deployer.exe must be compiled in DEBUG mode



---
to use via nuspec
files and reference should be conditionaly removed via 
https://stackoverflow.com/questions/533554/how-to-use-different-files-in-a-project-for-different-build-configurations-vis