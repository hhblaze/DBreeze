<table border="0">
<tr>
<td>
<img src="http://download-codeplex.sec.s-msft.com/Download?ProjectName=dbreeze&DownloadId=375768&Build=20717"></img>
</td>
<td>
<b>DBreeze Database</b>
</td>
</tr>
</table>
=====================
Professional, open-source, NoSql (embedded Key/Value storage), transactional, ACID-compliant, multi-threaded, object database management system for
C# .NET 3.0> MONO.

- It's a free software for those, who thinks that it should be free.
- Since June 2012 is used in our own production environment.
- Follow the project, to be in touch with the recent optimizations and enhancements.
- Read "Changes in versions" document.

Its homepage is http://dbreeze.tiesky.com or http://dbreeze.codeplex.com

- <a href='https://docs.google.com/document/pub?id=1IFkXoX3Tc2zHNAQN9EmGSXZGbQabMrWmpmVxFsLxLsw' target="_blank">Documentation (HTML, actual)</a>
- <a href='https://docs.google.com/document/pub?id=1VoBpzOENb24vF3ZQ10sxa0j-PAprKBGJ6uiGpEisxdM' target="_blank">Benchmark (HTML, actual)</a>
- <a href='https://docs.google.com/document/pub?id=1r1l940w4Z5p_6ntEkMTkjCWwbOQtJNr40Pq8wqI6g4o' target="_blank">Changes in versions </a>
- <a href='https://dbreeze.codeplex.com/discussions' target="_blank">Discussion on the forum </a>

Key features:

- Fully managed code, platform independent and without reference to other libraries.
- ACID compliant.
- Multi-threaded, with a solution for deadlocks resolving/elimination, parallel reads and synchronized writes/reads.
- No fixed scheme for table names (construction and access on the fly).
- Tables can reside in mixed locations: different folders, hard drives, memory.
- Database indexes (keys) never need to be defragmented. Speed of insert/update/remove operations doesn't grow up during the time.
- Ability to access Key/Value pair of a table by physical link, what can economize time for joining necessary data structures.
- No limits for database size (except "long" size for each table and physical resources constraints).
- Low physical space consumption, also while random inserts and updates. Updates reside the same physical space, if possible.
- High performance of CRUD operations. When you need, unleash DBreeze power and get 500000 key/value pairs insert or 260K updates per second per core into sorted table on the hard drive of standard PC.
- High speed of random keys batch insert and updates (batch must be sorted in memory ascending and non-overwrite flag must be set).
- Range selects / Traversing (Forward, Backward, From/To, Skip, StartsWith etc). Remove keys, change keys.
- Keys and values, on the low level, are always byte arrays.
- Max. key size is 65KB, max. value size is 2GB. Value can be represented as a set of columns, where can be stored data types of fixed or dynamic length. Every dynamic datablock can be of size 2GB.
- Rich set of conversion functions from/to between byte[] and other data types.
- Nested / Fractal tables which can reside inside of master tables values.
- Incremental backup/restore option.
- DBreeze is a foundation for complex data storage solutions (graph/neuro, object, document, text search etc. data layers). Please, study documentation to understand all abilities of DBreeze.

hhblaze@gmail.com
