# DBreeze storage contracts

Этот host компилирует один и тот же C# 7.3 test/benchmark source против effective DBreeze assembly каждой платформы. Он не использует internals библиотеки и не меняет публичный API.

## Targets

| `StorageTarget` | Consumer/runtime | DBreeze implementation |
|---|---|---|
| `Net6` | `net6.0` | `DBreeze/Storage/FSR.cs` через `DBreeze.Net5` |
| `NetStandard` | `net8.0` consumer | `DBreeze/Storage/FSR.cs` через `DBreeze.NetStandard` |
| `NetCoreApp` | `netcoreapp3.1` | `DBreeze/Storage/FSR.cs` через `DBreeze.NetCoreApp` |
| `NetFramework` | `net472` | `DBreeze/Storage/FSR.cs` через legacy project |
| `Portable` | `net472` consumer | `NETPortable/Storage/FSR.cs` и `IFileStream` |
| `Net8` | `net8.0` | modern `DBreeze.Net8/Storage/FSR.cs` |

`DBreezeAssemblyReference` заменяет project reference. Это позволяет дважды собрать неизменный worker против baseline/current DLL и запускать версии отдельными процессами.

## Contract suite

```powershell
dotnet build .\DBreeze.Storage.Contracts\DBreeze.Storage.Contracts.csproj -c Release `
  -p:StorageTarget=Net6 -p:SignAssembly=false

$env:DBREEZE_TEST_ROOT = 'D:\Temp\DbreezeDbTest'
dotnet .\DBreeze.Storage.Contracts\bin\Release\Net6\net6.0\DBreeze.Storage.Contracts.dll `
  --storage-contracts
```

Suite проверяет baseline architecture, commit/rollback, overlapping updates после auto-flush, crash recovery, truncated journal, safe restore, recreate/reopen, backup/restore и 2/8 readers + writer с barrier и timeout.

Portable следует собирать full MSBuild, потому что `dotnet build` не поставляет `Microsoft.Portable.CSharp.targets`:

```powershell
msbuild .\DBreeze.Storage.Contracts\DBreeze.Storage.Contracts.csproj /restore /t:Build `
  /p:Configuration=Release /p:StorageTarget=Portable /p:SignAssembly=false
```

## File compatibility worker

```text
--compat-create <database-root> <seed>
--compat-verify <database-root> <seed>
--compat-extend <database-root> <seed>
```

`--compat-verify` запоминает length/SHA-256 до открытия и требует их полной неизменности после read-only прохода.

## Performance worker

```text
--performance <database-root> <records> [Scenario1,Scenario2]
```

Без фильтра выполняются direct `StorageLayer` reads и representative engine scenarios: point existing/missing, forward/range/prefix/skip, sequential/random writes, update, rollback, RandomKeySorter и restore. Формат вывода — TSV (`PERF` lines), allocations доступны только там, где runtime предоставляет точный per-thread counter.

Ни один run не принимает больше 1 000 000 records. Все fixtures детерминированы. Каталог каждого scenario уникален; contract cleanup удаляет только GUID-marked leaf directory.
