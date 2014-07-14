More comprehensive information you can get on http://dbreeze.codeplex.com/ or http://dbreeze.tiesky.com
Here is a quick start example

```csharp
using DBreeze;
...
DBreezeEngine DBEngine = null;

//MORE INFO ON
//https://dbreeze.codeplex.com/
//or
//http://dbreeze.tiesky.com

public void InitDB()
{
	//more on https://dbreeze.codeplex.com/
	
	if (DBEngine == null)
		DBEngine = new DBreeze.DBreezeEngine("./sdcard/Download/DBreeze");
}

public void InsertDataExample()
{
	//more on https://dbreeze.codeplex.com/
	using (var tran = DBEngine.GetTransaction())
	{
		tran.Insert<int, int>("t1", 1, 1);
		tran.Insert<int, int>("t1", 6, 1);
		tran.Insert<int, int>("t1", 8, 1);
		tran.Commit();
	}
}

public void SelectDataExample()
{
	//more on https://dbreeze.codeplex.com/
	using (var tran = DBEngine.GetTransaction())
	{
		foreach (var row in tran.SelectForward<int, int>("t1"))
		{
			Console.WriteLine(row.Key);
		}
	}
}
```

## Other Resources

* [Component Documentation](https://docs.google.com/document/pub?id=1IFkXoX3Tc2zHNAQN9EmGSXZGbQabMrWmpmVxFsLxLsw)
* [Support Forums](https://dbreeze.codeplex.com/discussions)
* [Source Code Repository](https://dbreeze.codeplex.com/SourceControl/latest)
