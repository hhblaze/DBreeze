rem chcp 437
rem chcp 1251

"%~dp0nuget.exe" pack "%~dp0DBreeze.nuspec" -BasePath "%~dp0..\.." -OutputDirectory "%~dp0..\Nuget"