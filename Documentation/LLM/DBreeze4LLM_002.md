# DBreeze for LLM Agents
## Introduction
DBreeze is a professional, open-source, multi-paradigm database management system for .NET. This documentation is designed to help LLM agents understand and effectively use DBreeze as a library.

## Key Concepts
- DBreezeEngine: The main entry point for interacting with the database.
- Transaction: The context in which all data operations occur.
- Scheme: Used for manipulating existing database objects.

## Using DBreeze
### Initializing DBreezeEngine

To start using DBreeze, you need to instantiate `DBreezeEngine` by providing a folder path where database files will be stored. It's recommended to instantiate `DBreezeEngine` as a static variable at the beginning of the application and dispose of it at the end of the application work cycle.

```csharp
DBreezeEngine engine = new DBreezeEngine(@"D:\temp\DBR1");
```

or with configuration:

```csharp
DBreezeConfiguration conf = new DBreezeConfiguration()
{
    DBreezeDataFolderName = @"D:\temp\DBreezeTest\DBR1",
    Storage = DBreezeConfiguration.eStorage.DISK,
};
engine = new DBreezeEngine(conf);
```

### Transactions

All data operations in DBreeze occur within a transaction. Transactions can be used to ensure data consistency and integrity.

```csharp
using (var tran = engine.GetTransaction())
{
    // Perform operations
    tran.Commit();
}
```

To use transactions effectively:

1. Always dispose of a transaction after all necessary operations are done (using-statement makes it automatic).
2. One transaction can be run only in one .NET managed thread and cannot be delegated to other threads.
3. Nested transactions are not allowed (parent transaction will be terminated).

During in-transactional operations, it's highly recommended to use try-catch blocks together with the transaction and log exceptions for future analysis.

```csharp
try
{
    using (var tran = engine.GetTransaction())
    {
        // Operations here
        tran.Commit();
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
}
```

### Scheme Operations

The `Scheme` class is used to manipulate existing database objects.

```csharp
// Deleting a table
engine.Scheme.DeleteTable("tableName");

// Checking if a table exists
bool exists = engine.Scheme.IfUserTableExists("tableName");

// Getting specific table names
List<string> tableNames = engine.Scheme.GetUserTableNamesStartingWith("Articles").ToList();

// Renaming a table
engine.Scheme.RenameTable("oldTableName", "newTableName");

// Getting physical path to the file holding the table
string tablePath = engine.Scheme.GetTablePathFromTableName("tableName");
```

### Transaction Operations

The `Transaction` class offers several important public methods for data manipulation:

#### Inserting Data

Data is inserted into tables using the `Insert` method. If the table doesn't exist, it will be created automatically.

```csharp
tran.Insert<int, string>("table1", 1, "hello");
tran.Commit();
```

#### Updating Data

Updating a key is similar to inserting data. If the key exists, its value will be updated.

```csharp
tran.Insert<int, string>("table1", 1, "new value");
tran.Commit();
```

#### Removing Keys

Keys can be removed using the `RemoveKey` method.

```csharp
tran.RemoveKey<int>("table1", 1);
tran.Commit();
```

To remove all keys, use `RemoveAllKeys`.

```csharp
tran.RemoveAllKeys("table1", true); // true recreates the table file
```

#### Changing Keys

You can change a key using the `ChangeKey` method.

```csharp
tran.Insert<int, int>("t1", 10, 10);
tran.ChangeKey<int>("t1", 10, 11);
tran.Commit();
```

### Working with Objects

DBreeze supports storing objects using custom serializers like Biser.NET.

```csharp
public class Article
{
    public uint Id { get; set; }
    public string Name { get; set; }
}

using (var tran = engine.GetTransaction())
{
    Article article = new Article { Id = 1, Name = "Example" };
    tran.ObjectInsert("Articles", new DBreezeObject<Article> { Entity = article, Indexes = new List<DBreezeIndex> { new DBreezeIndex(1, article.Id) { PrimaryIndex = true } } }, false);
    tran.Commit();
}
```

### Byte Array Conversions

DBreeze provides utility functions for converting various data types to and from byte arrays through `DBreeze.Utils.BytesProcessing`.

```csharp
// Converting int to byte array (BigEndian)
byte[] intBytes = 123.To_4_bytes_array_BigEndian();

// Converting byte array to int (BigEndian)
int value = intBytes.To_Int32_BigEndian();
```

## Advanced Topics

### Using Pattern System for Table Names

DBreeze allows table name patterns for synchronization, which is useful for locking multiple tables that follow a certain naming convention.

```csharp
tran.SynchronizeTables("Articles*"); // Locks all tables starting with "Articles"
```

Patterns can include special symbols:
- `*`: Matches any characters after it.
- `#`: Matches characters followed by a slash and another character.
- `$`: Matches characters except a slash.

### tran.SynchronizeTables

`SynchronizeTables` is used before any table modification command inside a transaction to avoid deadlocks when modifying multiple tables.

```csharp
tran.SynchronizeTables("table1", "table2");
tran.Insert<int, int>("table1", 1, 1);
tran.Insert<int, int>("table2", 2, 2);
tran.Commit();
```

### ValuesLazyLoadingIsOn

This property controls whether iterators return `Row` with a pointer to the value or the value itself. Default is `true`, which means lazy loading.

```csharp
tran.ValuesLazyLoadingIsOn = false; // Disables lazy loading for the transaction
```

### Working with Memory Tables

Tables can be configured to reside in memory by setting an alternative storage location in the configuration.

```csharp
conf.AlternativeTablesLocations.Add("mem_*", String.Empty); // Tables starting with "mem_" will be in-memory
```

### Text Search Engine

DBreeze includes a text search engine that allows for efficient full-text search.

```csharp
tran.TextInsert("TextSearchTable", documentId, "searchable text");
var searchResults = tran.TextSearch("TextSearchTable").Block("search term").GetDocumentIDs();
```

## Best Practices
- Always dispose of `DBreezeEngine` and `Transaction` objects when appropriate.
- Use `SynchronizeTables` to avoid deadlocks when modifying multiple tables.
- Consider using `ValuesLazyLoadingIsOn` to optimize performance.

## Public Functions of Transaction

The `Transaction` class offers several important public methods:

1. `Insert<TKey, TValue>`: Inserts or updates a key-value pair in a specified table.
2. `RemoveKey<TKey>`: Removes a key from a table.
3. `ChangeKey<TKey>`: Renames a key in a table.
4. `InsertDataBlock` and `InsertDataBlockWithFixedAddress`: For storing dynamic-size data blocks.
5. `Select<TKey, TValue>` and `SelectDirect<TKey, TValue>`: For retrieving data.
6. `ObjectInsert<TObject>` and `ObjectRemove`: For working with objects and their indexes.
7. `TextInsert`, `TextAppend`, `TextRemove`, and `TextRemoveAll`: For managing text search indexes.

### Examples

```csharp
// Insert or update
tran.Insert<int, string>("table", 1, "value");

// Remove key
tran.RemoveKey<int>("table", 1);

// Change key
tran.ChangeKey<int>("table", 1, 2);

// Insert data block
byte[] dataBlockPtr = tran.InsertDataBlock("table", null, new byte[] { 1, 2, 3 });

// Select data
var row = tran.Select<int, string>("table", 1);

// Object insert
Article article = new Article { Id = 1, Name = "Example" };
tran.ObjectInsert("Articles", new DBreezeObject<Article> { Entity = article, Indexes = new List<DBreezeIndex> { new DBreezeIndex(1, article.Id) { PrimaryIndex = true } } }, false);

// Text search insert
tran.TextInsert("TextSearchTable", documentId, "searchable text");
```

## Public Functions of Scheme

The `Scheme` class provides methods for manipulating existing database objects:

1. `DeleteTable`: Deletes a user table.
2. `IfUserTableExists`: Checks if a user table exists.
3. `GetUserTableNamesStartingWith`: Retrieves table names starting with a given mask.
4. `RenameTable`: Renames a user table.

### Examples

```csharp
// Delete table
engine.Scheme.DeleteTable("tableName");

// Check if table exists
bool exists = engine.Scheme.IfUserTableExists("tableName");

// Get table names starting with
List<string> tableNames = engine.Scheme.GetUserTableNamesStartingWith("Articles").ToList();

// Rename table
engine.Scheme.RenameTable("oldTableName", "newTableName");
```

## Conclusion
This documentation provides a comprehensive overview of how to use DBreeze effectively. By following these guidelines and examples, LLM agents can leverage DBreeze's capabilities in their applications.
To start using DBreeze, you need to instantiate `DBreezeEngine` by providing a folder path where database files will be stored.

```csharp
DBreezeEngine engine = new DBreezeEngine(@"D:\temp\DBR1");
```

### Transactions
All data operations in DBreeze occur within a transaction.

```csharp
using (var tran = engine.GetTransaction())
{
    // Perform operations
    tran.Commit();
}
```

### Scheme Operations
The `Scheme` class is used to manipulate existing database objects.

```csharp
// Deleting a table
engine.Scheme.DeleteTable("tableName");

// Checking if a table exists
bool exists = engine.Scheme.IfUserTableExists("tableName");
```

### Transaction Operations
#### Inserting Data
Data is inserted into tables using the `Insert` method.

```csharp
tran.Insert<int, string>("table1", 1, "hello");
tran.Commit();
```

#### Updating Data
Updating a key is similar to inserting data.

```csharp
tran.Insert<int, string>("table1", 1, "new value");
tran.Commit();
```

#### Removing Keys
Keys can be removed using the `RemoveKey` method.

```csharp
tran.RemoveKey<int>("table1", 1);
tran.Commit();
```

### Working with Objects
DBreeze supports storing objects using custom serializers like Biser.NET.

```csharp
public class Article
{
    public uint Id { get; set; }
    public string Name { get; set; }
}

using (var tran = engine.GetTransaction())
{
    Article article = new Article { Id = 1, Name = "Example" };
    tran.ObjectInsert("Articles", new DBreezeObject<Article> { Entity = article, Indexes = new List<DBreezeIndex> { new DBreezeIndex(1, article.Id) { PrimaryIndex = true } } }, false);
    tran.Commit();
}
```

### Byte Array Conversions
DBreeze provides utility functions for converting various data types to and from byte arrays.

```csharp
// Converting int to byte array (BigEndian)
byte[] intBytes = 123.To_4_bytes_array_BigEndian();

// Converting byte array to int (BigEndian)
int value = intBytes.To_Int32_BigEndian();
```

## Advanced Topics
### Using Pattern System for Table Names
DBreeze allows table name patterns for synchronization.

```csharp
tran.SynchronizeTables("Articles*");
```

### Working with Memory Tables
Tables can be configured to reside in memory.

```csharp
conf.AlternativeTablesLocations.Add("mem_*", String.Empty);
```

### Text Search Engine
DBreeze includes a text search engine.

```csharp
tran.TextInsert("TextSearchTable", documentId, "searchable text");
var searchResults = tran.TextSearch("TextSearchTable").Block("search term").GetDocumentIDs();
```

## Best Practices
- Always dispose of `DBreezeEngine` and `Transaction` objects when appropriate.
- Use `SynchronizeTables` to avoid deadlocks when modifying multiple tables.
- Consider using `ValuesLazyLoadingIsOn` to optimize performance.

## Conclusion
This documentation provides a comprehensive overview of how to use DBreeze effectively. By following these guidelines and examples, LLM agents can leverage DBreeze's capabilities in their applications.