using System.Collections;
using System.Reflection;

// Reflection is confined to untimed diagnostics. Supports historical Queue/LinkedList DLLs
// and validates both directions of the intrusive FIFO without adding production APIs.
internal static class CacheOrderProbe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Field(object value, string name) => value.GetType().GetField(name, Flags)!.GetValue(value);
    internal static bool IsIntrusive(object manager) => manager.GetType().GetField("_globalHead", Flags) != null;
    internal static object[] Global(object manager) => Read(manager, true);
    internal static object[] Table(object table) => Read(table, false);
    internal static int GlobalCount(object manager) => Count(manager, true);
    internal static int TableCount(object table) => Count(table, false);

    private static int Count(object owner, bool global)
    {
        string prefix = global ? "_global" : "_order";
        if (owner.GetType().GetField(prefix + "Count", Flags) != null)
            return (int)Field(owner, prefix + "Count");
        object collection = Field(owner, global ? "_globalOrder" : "_order");
        return (int)collection.GetType().GetProperty("Count")!.GetValue(collection);
    }

    private static object[] Read(object owner, bool global)
    {
        string prefix = global ? "_global" : "_order";
        if (owner.GetType().GetField(prefix + "Head", Flags) == null)
            return ((IEnumerable)Field(owner, global ? "_globalOrder" : "_order")).Cast<object>().ToArray();
        string link = global ? "Global" : "Table";
        object head = Field(owner, prefix + "Head"), tail = Field(owner, prefix + "Tail");
        var entries = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        object previous = null;
        for (object current = head; current != null; current = Field(current, link + "Next"))
        {
            if (!seen.Add(current)) throw new Exception("Cycle in " + prefix + " FIFO.");
            if (!ReferenceEquals(Field(current, link + "Previous"), previous))
                throw new Exception("Broken previous link in " + prefix + " FIFO.");
            entries.Add(current); previous = current;
        }
        if (!ReferenceEquals(previous, tail) || entries.Count != Count(owner, global))
            throw new Exception("FIFO head/tail/count differ: " + prefix);
        int index = entries.Count - 1;
        for (object current = tail; current != null; current = Field(current, link + "Previous"))
            if (index < 0 || !ReferenceEquals(entries[index--], current))
                throw new Exception("Broken reverse traversal: " + prefix);
        if (index != -1) throw new Exception("Incomplete reverse traversal: " + prefix);
        return entries.ToArray();
    }
}
