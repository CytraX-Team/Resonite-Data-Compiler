#define COMPONENTS
#define LOGIX

using System.Reflection;
using System.Text;
using FrooxEngine;
using BaseX;
using FrooxEngine.LogiX;

static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
{
    try
    {
        return assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException e)
    {
        return e.Types.Where(t => t != null);
    }
}
var asms = Directory.GetFiles(Directory.GetCurrentDirectory()).Where((s) => s.EndsWith(".dll") && !s.StartsWith("System")).Select((s) =>
{
    try { return Assembly.LoadFrom(s); }
    catch (BadImageFormatException e) { }
    return null;
}).Where((asm) => asm != null);

EngineInitializer.VerboseInit = true;

var allTypes = asms.SelectMany(T => GetLoadableTypes(T)).ToList();

WorkerInitializer.Initialize(allTypes);

HashSet<string> seenOverloads = new();

StringBuilder componentsString = new();

void ProcessNode(CategoryNode<Type> node, StringBuilder builder, int depth)
{
    builder.AppendLine(new string(':', depth) + " " + node.Name);
    foreach (var subdir in node.Subcategories)
    {
        ProcessNode(subdir, builder, depth + 1);
    }
    foreach (var element in node.Elements)
    {
        var toPrint = element;
#if LOGIX
        string overloadName = LogixHelper.GetOverloadName(element);
        if (overloadName != null)
        {
            if (seenOverloads.Add(overloadName))
            {
                toPrint = LogixHelper.GetMatchingOverload(overloadName, null, typeof(LogixNodeSelector).GetMethod("GetTypeRank", BindingFlags.NonPublic | BindingFlags.Static).CreateDelegate<Func<Type, int>>());
            }
            else
            {
                continue;
            }
        }
#endif
        builder.AppendLine(new string(':', depth + 1) + " " + toPrint.GetNiceName() + ":" + toPrint.FullName);
    }
}
#if COMPONENTS
foreach (var node in WorkerInitializer.ComponentLibrary.Subcategories)
{
    if (node.Name == "LogiX") continue;
    ProcessNode(node, componentsString, 0);
}
// Writes our components list to a file.
File.WriteAllTextAsync("ComponentList.txt", componentsString.ToString());
#endif

#if LOGIX
StringBuilder logixString = new();
ProcessNode(WorkerInitializer.ComponentLibrary.GetSubcategory("LogiX"), logixString, 0);
// Writes our logix list to a file.
File.WriteAllTextAsync("LogixList.txt", componentsString.ToString());
#endif
