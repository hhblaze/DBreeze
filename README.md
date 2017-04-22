DBreeze ![Image of DBreeze](https://github.com/hhblaze/DBreeze/blob/master/Documentation/Dbreeze.Logo.png) Database
=====================
![Image of Build](https://img.shields.io/badge/DBreeze%20build-1.084%20production-9933FF.svg) 
![Image of Build](https://img.shields.io/badge/License-BSD%203,%20FOSS-FC0574.svg) 
![Image of Build](https://img.shields.io/badge/Roadmap-completed-33CC33.svg)
![Image of Build](https://img.shields.io/badge/Powered%20by-tiesky.com-1883F5.svg)

DBreeze Database is a professional, open-source, multi-paradigm (embedded Key-Value store, objects, NoSql etc), 
multi-threaded, transactional and ACID-compliant database management system for
.NET 3.5> / Xamarin MONO Android iOS / .NET Core / Universal Windows Platform / .NET Portable 
...for servers and internet-of-things... Made with C# 

- It's a free software for those who think that it should be free.
- It has been used in our own production environment since June 2012.
- Follow the project, to be in touch with the recent optimizations and enhancements.
- DBreeze via <a href = 'https://www.nuget.org/packages/DBreeze/'  target='_blank'>NuGet</a> since January 2014. 
- DBreeze for .NETCore, UWP (Universal Windows Platform), .NET 4.5.1 grab via NuGet.
- Works on Linux, Windows, OS X. Via Xamarin on Android, iOS.
- DBreeze is listed in <a href = 'http://nosql-database.org'  target='_blank'>nosql-database.org</a>
- Read "Changes in versions" document.


Its homepage is http://dbreeze.tiesky.com or http://dbreeze.codeplex.com

- <a href = 'https://github.com/hhblaze/DBreeze/wiki/Quick-start-guides'  target='_blank'>Quick start guides</a> 
- <a href = 'https://github.com/hhblaze/DBreeze/releases'  target='_blank'>Assemblies location</a> 
- <a href='https://github.com/hhblaze/DBreeze/raw/master/Documentation/_DBreeze.Documentation.actual.pdf' target="_blank">Documentation (PDF, actual)</a>
- <a href='https://docs.google.com/document/pub?id=1IFkXoX3Tc2zHNAQN9EmGSXZGbQabMrWmpmVxFsLxLsw' target="_blank">Documentation (HTML, actual)</a>
- <a href='https://docs.google.com/document/pub?id=1VoBpzOENb24vF3ZQ10sxa0j-PAprKBGJ6uiGpEisxdM' target="_blank">Benchmark (HTML, actual)</a>
- <a href='https://docs.google.com/document/pub?id=1r1l940w4Z5p_6ntEkMTkjCWwbOQtJNr40Pq8wqI6g4o' target="_blank">Changes in versions </a>
- <a href='https://docs.google.com/document/pub?id=188hY76go8bB2tSyQYoN0NMIJbMEuCOxYXNKZs_sEcpo' target="_blank">DBreeze tuning advices</a>
- <a href='https://dbreeze.codeplex.com/discussions' target="_blank">Discussion on the forum </a>

Key features:

- Fully managed code, platform independent and without reference to other libraries. 
- ACID compliant. 
- Multi-threaded, with a solution for deadlocks resolving/elimination, parallel reads and synchronized writes/reads. 
- No fixed scheme for table names (construction and access on the fly).
- Tables can reside in mixed locations: different folders, hard drives, memory.
- Liana-Trie indexing technology. Database indexes (keys) never need to be defragmented. Speed of insert/update/remove operations doesn't grow up during the time.
- Ability to access Key/Value pair of a table by physical link, what can economize time for joining necessary data structures.
- No limits for database size (except "long" size for each table and physical resources constraints).
- Low memory and physical space consumption, also while random inserts and updates. Updates reside the same physical space, if possible.
- High performance of CRUD operations. When you need, unleash DBreeze power and get 500000 key/value pairs insert or 260K updates per second per core into sorted table on the hard drive of standard PC.
- High speed of random keys batch insert and updates (batch must be sorted in memory ascending and non-overwrite flag must be set).
- Range selects / Traversing (Forward, Backward, From/To, Skip, StartsWith etc). Remove keys, change keys.
- Keys and values, on the low level, are always byte arrays. 
- Max. key size is 65KB, max. value size is 2GB. Value can be represented as a set of columns, where can be stored data types of fixed or dynamic length. Every dynamic datablock can be of size 2GB. 
- Rich set of conversion functions from/to between byte[] and other data types.
- Nested / Fractal tables which can reside inside of master tables values.
- Incremental backup/restore option.
- Integrated text-search subsystem (full-text/partial).
- Integrated object database layer.
- Fast multi-parameter search subsystem with powerful query possibilities.
- DBreeze is a foundation for complex data storage solutions (graph/neuro, object, document, text search etc. data layers). Please, study documentation to understand all abilities of DBreeze.

hhblaze@gmail.com
