#define COMPONENTS
#define LOGIX

using System.Reflection;
using System.Text;
using FrooxEngine;
using BaseX;
using FrooxEngine.LogiX;

const char SEPARATOR = '#';

// This warning happens because c# thinks that my collections are nullable, but I filter out nulls on all of them
// I don't know of another way to get it to stop complaining other than to completely disable the warning
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
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
IEnumerable<Assembly> asms = Directory.GetFiles(Directory.GetCurrentDirectory()).Where((s) => s.EndsWith(".dll") && !s.StartsWith("System")).Select((s) =>
{
    try { return Assembly.LoadFrom(s); }
    // For non C# dlls in the managed folder
    catch (BadImageFormatException) { }
    return null;
}).Where((asm) => asm != null);

EngineInitializer.VerboseInit = true;

var allTypes = asms.SelectMany(T => GetLoadableTypes(T)).ToList();

WorkerInitializer.Initialize(allTypes);

StringBuilder componentsString = new();

void PrintComp(Type element, StringBuilder builder, int depth, HashSet<string> seenOverloads)
{
#if LOGIX
    if (typeof(LogixNode).IsAssignableFrom(element))
    {
        var toPrint = element;
        if (LogixHelper.IsHidden(element)) return;
        string overloadName = LogixHelper.GetOverloadName(element);
        if (overloadName != null)
        {
            if (seenOverloads.Add(overloadName))
            {
                toPrint = LogixHelper.GetMatchingOverload(overloadName, null, typeof(LogixNodeSelector).GetMethod("GetTypeRank", BindingFlags.NonPublic | BindingFlags.Static)?.CreateDelegate<Func<Type, int>>());
            }
            else
            {
                return;
            }
        }
        builder.AppendLine(new string(SEPARATOR, depth + 1) + " " + LogixHelper.GetNodeName(toPrint) + "@" + toPrint.GetNiceName() + SEPARATOR + toPrint.FullName);
        return;
    }
#endif
    builder.AppendLine(new string(SEPARATOR, depth + 1) + " " + element.GetNiceName() + SEPARATOR + element.FullName);
}
void ProcessNode(CategoryNode<Type> node, StringBuilder builder, int depth)
{
    builder.AppendLine(new string(SEPARATOR, depth) + " " + node.Name);
    foreach (var subdir in node.Subcategories)
    {
        ProcessNode(subdir, builder, depth + 1);
    }
    // the line below is only useful in logix mode
    // but commenting it out will cause the PrintComp line to error since it requires that variable
    HashSet<string> seenOverloads = new();
    foreach (var element in node.Elements)
    {
        PrintComp(element, builder, depth, seenOverloads);
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
File.Move(@"ComponentList.txt", @"..\lists\ComponentList.txt", true);

#endif

#if LOGIX
StringBuilder logixString = new();
var logixPath = WorkerInitializer.ComponentLibrary.GetSubcategory("LogiX");

foreach(var node in logixPath.Subcategories) {
    ProcessNode(node, logixString, 0);
}
var seenOverloads = new HashSet<string>();
foreach(var node in logixPath.Elements)
{
    PrintComp(node, logixString, -1, seenOverloads);
}

// Writes our logix list to a file.
File.WriteAllTextAsync("LogixList.txt", logixString.ToString());
File.Move(@"LogixList.txt", @"..\lists\LogixList.txt", true);
#endif
