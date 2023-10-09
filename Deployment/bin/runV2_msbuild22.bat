rem "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\msbuild.exe" "%~dp0..\..\%1" /t:restore;rebuild /p:Configuration=Release /p:DefineConstants=%3 /p:TargetFramework=%2 
rem "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\msbuild.exe"
"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\msbuild.exe" "%~dp0..\..\%1" /t:restore /p:Configuration=Release /p:DefineConstants=%3 /p:TargetFramework=%2 
"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\msbuild.exe" "%~dp0..\..\%1" /t:rebuild /p:Configuration=Release /p:%4=%2 /p:DefineConstants=%3
